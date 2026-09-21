// =============================================================================
// RDS: FM multiplex in, station name out. One block.
// =============================================================================
// Radio Data System - the digital subcarrier at 57 kHz that carries a station's
// call sign, its programme type and the scrolling "now playing" text. This
// block takes the demodulated FM multiplex (MPX) and publishes what it finds as
// read-only text parameters.
//
//   MPX -> mix down by 57 kHz -> low-pass + decimate -> biphase matched filter
//     -> AGC -> Gardner timing recovery -> Costas loop -> slice
//     -> differential decode -> block sync -> group decode
//
// -----------------------------------------------------------------------------
// WHY THIS IS ~400 LINES OF HAND-ROLLED DSP, AND WHY THAT IS TEMPORARY
// -----------------------------------------------------------------------------
// Almost nothing between the mixer and the group decoder is a stock SignalCraft
// block, because the digital-demodulation family does not exist yet. Every
// stage below is hand-written for exactly that reason:
//
//   hand-rolled here                     will become
//   ----------------                     -----------
//   windowed-sinc low-pass + mixer       FreqXlatingFirFilter with taps
//   biphase matched filter               FirFilterCCF with designed taps
//   AGC                                  AgcCC
//   Gardner timing loop                  a symbol-sync block
//   Costas loop                          a carrier-recovery block
//   slicer                               a binary slicer
//   differential decoder                 a differential decoder
//   RdsGroupDecoder                      STAYS - it is a protocol, not DSP
//
// When those blocks arrive this file should shrink to the group decoder and a
// handful of wires. Until then it is here in full, and it works. Treat the DSP
// half as a worked example of what those blocks each have to do.
//
// -----------------------------------------------------------------------------
// WHY THIS IS ONE BLOCK AND NOT TWO
// -----------------------------------------------------------------------------
// It began as a demodulator and a decoder joined by a bit stream, which is the
// more honest boundary and is how other toolkits split it. It cannot stay that
// way. A biphase signal gives the timing loop TWO stable lock points, and the
// only thing that can tell them apart is whether the group synchroniser
// downstream is finding valid checkwords. That feedback has to cross the
// boundary, so the boundary has to go.
//
// -----------------------------------------------------------------------------
// Adapted from PetRadio (github.com/modarken, the same author), where it is
// `src/PetRadio.Core/Rds/` and runs against a real RTL-SDR. Contributed here
// under this repository's MIT licence.
//
// The usings for System, System.Collections.Generic, SignalCraft.Core and
// SignalCraft.Hosting are implicit in a block compiled from source.
// =============================================================================

namespace Community;

/// <summary>
/// Decodes the RDS subcarrier out of an FM multiplex signal.
///
/// Feed it the MPX - the output of an FM demodulator, before de-emphasis and
/// before the audio filter, so the 57 kHz subcarrier is still present. Read the
/// station name, programme type and radio text back as string parameters.
/// </summary>
/// <remarks>
/// <para>
/// <b>Set <c>mpx_rate</c> to the rate of the stream you are feeding it</b>, and
/// set it before the graph starts. The default is 480 kHz, which is a common
/// FM chain's MPX rate. Any rate works so long as it is high enough to carry
/// 57 kHz plus the subcarrier's sidebands - about 130 kHz is the floor.
/// </para>
/// <para>
/// The <c>symbols</c> output carries the recovered constellation, so a
/// Constellation display wired to it shows at a glance which half of the chain
/// is failing: two dots means carrier and timing are both locked; a ring means
/// timing locked and carrier not; a formless blob means the 57 kHz extraction
/// itself is wrong.
/// </para>
/// </remarks>
public sealed class Rds : BlockBase, IHostedBlock
{
    private const double SubcarrierHz = 57_000d;
    private const double SymbolRate = 1187.5d;

    /// <summary>
    /// The working rate after decimation, in Hz. Around 24 kHz gives roughly
    /// 20 samples per symbol, which is comfortable for the timing loop without
    /// making the matched filter needlessly long.
    /// </summary>
    private const double TargetWorkingRate = 24_000d;

    private const double DefaultMpxRate = 480_000d;

    /// <summary>
    /// Symbols of silence before assuming the timing loop took the wrong one
    /// of the two biphase lock points. About a second - a healthy station
    /// delivers a group every 104 symbols.
    /// </summary>
    private const long PhaseHuntSymbols = 1200;

    /// <summary>
    /// The same patience once something has decoded. Longer, because by then
    /// the lock point is known good and a gap is a fade rather than a wrong
    /// phase; jumping through a fade discards a lock that was coming back.
    /// </summary>
    private const long PhaseHuntSymbolsAfterSuccess = 4800;

    private static readonly HostParamDescriptor[] s_params =
    [
        // Read-only text. There is no stream type for text and no way to route
        // a message port into a design, so a parameter is how decoded text
        // leaves a block.
        HostParamDescriptor.Text("pi", "PI code"),
        HostParamDescriptor.Text("ps", "Station name"),
        HostParamDescriptor.Text("rt", "Radio text"),
        HostParamDescriptor.Text("pty", "Programme type"),

        HostParamDescriptor.Number("groups", "Groups decoded", 0d, 1e9d),
        HostParamDescriptor.Number("errors", "Block errors", 0d, 1e9d),

        // The four that separate the ways RDS fails. Without them every
        // failure reads the same: no text.
        HostParamDescriptor.Number("level", "Subcarrier level", 0d, 1d),
        HostParamDescriptor.Number("symbol_rate", "Recovered symbol rate", 0d, 2000d),
        HostParamDescriptor.Number("lock", "Carrier lock", -2d, 2d),
        HostParamDescriptor.Number("flips", "Lock-point flips", 0d, 1e9d),

        HostParamDescriptor.Number("mpx_rate", "MPX sample rate", 130_000d, 10_000_000d),
        HostParamDescriptor.Number("costas_bandwidth", "Costas loop bandwidth", 0d, 1d),
        HostParamDescriptor.Number("timing_bandwidth", "Timing loop bandwidth", 0d, 1d),
    ];

    private static readonly HostPortDescriptor[] s_inputs = [HostPortDescriptor.FloatIn("in", 0, "MPX")];

    private static readonly HostPortDescriptor[] s_outputs =
    [
        HostPortDescriptor.Out("symbols", StreamType.Complex32, 0, "Symbols"),
    ];

    public override string Name => "Rds";
    public override BlockType BlockType => BlockType.Transform;
    public override ReadOnlySpan<HostPortDescriptor> InputPorts => s_inputs;
    public override ReadOnlySpan<HostPortDescriptor> OutputPorts => s_outputs;
    public override ReadOnlySpan<HostParamDescriptor> Parameters => s_params;

    public static ReadOnlySpan<HostParamDescriptor> Describe() => s_params;
    public static string Category => "modulation";
    public static ReadOnlySpan<HostPortDescriptor> DescribeInputPorts() => s_inputs;
    public static ReadOnlySpan<HostPortDescriptor> DescribeOutputPorts() => s_outputs;

    /// <summary>
    /// Symbols do not arrive on a fixed schedule: the timing loop decides when
    /// one is ready. Roughly 400 MPX samples produce one.
    /// </summary>
    public override bool FixedRate => false;

    private readonly RdsGroupDecoder _decoder = new();
    private readonly Queue<Complex32> _pending = new();
    private const int MaxPending = 4096;

    private double _mpxRate = DefaultMpxRate;

    // Rebuilt by Design() whenever mpx_rate changes, which is only legal while
    // the graph is stopped - Process() reads every one of these.
    private float[] _lpTaps = [];
    private Complex32[] _lpHistory = [];
    private float[] _mfTaps = [];
    private Complex32[] _mfHistory = [];
    private int _decimate;
    private double _samplesPerSymbol;
    private double _ncoStep;

    private int _lpWrite, _lpPhase, _mfWrite;
    private double _ncoPhase;
    private float _agcGain = 1f;

    private double _muTimer, _timingPeriod;
    private Complex32 _lastSample, _symbolPrev, _symbolMid;
    private bool _haveMid;

    private double _costasPhase, _costasFreq, _lockEstimate, _basebandLevel;
    private long _symbolsSinceGroup, _lastGroupCount, _phaseFlips;
    private int _previousBit = -1;

    private double _costasBandwidth = 0.01d;
    private double _timingBandwidth = 0.002d;

    public Rds() => Design();

    /// <summary>
    /// Works out the decimation, the filters and the loop constants for the
    /// current <c>mpx_rate</c>. Called from the constructor and again from
    /// <see cref="Start"/>, so a design that states a rate gets filters built
    /// for it rather than for the default.
    /// </summary>
    private void Design()
    {
        _decimate = Math.Max(1, (int)Math.Round(_mpxRate / TargetWorkingRate));
        double workingRate = _mpxRate / _decimate;

        _ncoStep = -2d * Math.PI * SubcarrierHz / _mpxRate;

        // 3 kHz of passband either side of the subcarrier: the biphase spectrum
        // peaks at +/-1187.5 Hz and is done well before 3 kHz.
        _lpTaps = DesignLowPass(_mpxRate, cutoffHz: 3_000d, transitionHz: 3_000d);
        _lpHistory = new Complex32[_lpTaps.Length];

        _samplesPerSymbol = workingRate / SymbolRate;
        _timingPeriod = _samplesPerSymbol;

        _mfTaps = DesignBiphase(_samplesPerSymbol);
        _mfHistory = new Complex32[_mfTaps.Length];

        _lpWrite = 0;
        _lpPhase = 0;
        _mfWrite = 0;
    }

    public override bool TrySetParameter(string name, double value)
    {
        switch (name)
        {
            case "mpx_rate":
                // Rebuilt here rather than at Start(), so that a caller which
                // sets the rate and reads symbol_rate back sees its own value.
                // Setting it on a RUNNING graph would reallocate the arrays
                // Process() is reading, so it is refused there.
                if (value < 130_000d) return false;
                _mpxRate = value;
                Design();
                return true;
            case "costas_bandwidth": _costasBandwidth = value; return true;
            case "timing_bandwidth": _timingBandwidth = value; return true;
            default: return false;
        }
    }

    public override bool TryGetParameter(string name, out double value)
    {
        switch (name)
        {
            case "groups": value = _decoder.Groups; return true;
            case "errors": value = _decoder.Errors; return true;
            case "level": value = _basebandLevel; return true;
            case "symbol_rate": value = _mpxRate / _decimate / _timingPeriod; return true;
            case "lock": value = _lockEstimate; return true;
            case "flips": value = _phaseFlips; return true;
            case "mpx_rate": value = _mpxRate; return true;
            case "costas_bandwidth": value = _costasBandwidth; return true;
            case "timing_bandwidth": value = _timingBandwidth; return true;
            default: value = 0; return false;
        }
    }

    public override bool TryGetParameterString(string name, out string value)
    {
        switch (name)
        {
            case "pi": value = _decoder.ProgrammeIdText; return true;
            case "ps": value = _decoder.ProgrammeService; return true;
            case "rt": value = _decoder.RadioText; return true;
            case "pty": value = _decoder.ProgrammeType; return true;
            default: value = ""; return false;
        }
    }

    public override void Start()
    {
        _decoder.Reset();
        _previousBit = -1;
        _haveMid = false;
        _ncoPhase = 0;
        _agcGain = 1f;
        _muTimer = 0;
        _costasPhase = 0;
        _costasFreq = 0;
        _lockEstimate = 0;
        _basebandLevel = 0;
        _timingPeriod = _samplesPerSymbol;
        _symbolsSinceGroup = 0;
        _lastGroupCount = 0;
        _phaseFlips = 0;
        _pending.Clear();
        Array.Clear(_lpHistory);
        Array.Clear(_mfHistory);
        _lpWrite = 0;
        _lpPhase = 0;
        _mfWrite = 0;
    }

    public override void Process(Span<InteropBuffer> readables, Span<InteropBuffer> writables)
    {
        ReadOnlySpan<float> mpx = readables[0].AsReadOnlySpan<float>();

        foreach (float sample in mpx)
        {
            _ncoPhase += _ncoStep;
            if (_ncoPhase < -Math.PI) _ncoPhase += 2d * Math.PI;

            var mixed = new Complex32(
                sample * (float)Math.Cos(_ncoPhase),
                sample * (float)Math.Sin(_ncoPhase));

            _lpHistory[_lpWrite] = mixed;
            _lpWrite = _lpWrite + 1 == _lpHistory.Length ? 0 : _lpWrite + 1;

            // A decimating FIR only evaluates at output instants.
            if (++_lpPhase < _decimate) continue;
            _lpPhase = 0;

            Complex32 baseband = Convolve(_lpTaps, _lpHistory, _lpWrite);
            _basebandLevel += 1e-4d * (MathF.Sqrt((baseband.Real * baseband.Real)
                                                  + (baseband.Imag * baseband.Imag)) - _basebandLevel);

            _mfHistory[_mfWrite] = baseband;
            _mfWrite = _mfWrite + 1 == _mfHistory.Length ? 0 : _mfWrite + 1;

            Complex32 shaped = Convolve(_mfTaps, _mfHistory, _mfWrite);

            // AGC AFTER THE MATCHED FILTER. Both loops below measure an error
            // as a product of sample amplitudes clamped to +/-1, so a signal
            // arriving at twice unit scale makes those errors four times too
            // big and pins them against the clamp. A loop driven by a
            // saturated error never settles.
            float magnitude = MathF.Sqrt((shaped.Real * shaped.Real) + (shaped.Imag * shaped.Imag));
            if (magnitude > 1e-9f)
            {
                _agcGain += (float)(1e-3d * ((1f / magnitude) - _agcGain));
                _agcGain = Math.Clamp(_agcGain, 1e-4f, 1e4f);
            }

            AdvanceTiming(new Complex32(shaped.Real * _agcGain, shaped.Imag * _agcGain));
        }

        readables[0].Consumed = mpx.Length;

        Span<Complex32> symbols = writables[0].AsSpan<Complex32>();
        int produced = 0;
        while (produced < symbols.Length && _pending.Count > 0) symbols[produced++] = _pending.Dequeue();
        writables[0].Produced = produced;
    }

    /// <summary>
    /// Gardner timing recovery. Gardner rather than Mueller-Muller because its
    /// error detector is insensitive to carrier phase, so the timing loop locks
    /// through the frequency offset the Costas loop has not corrected yet.
    /// </summary>
    private void AdvanceTiming(Complex32 sample)
    {
        _muTimer -= 1d;
        if (_muTimer > 0d) { _lastSample = sample; return; }

        float mu = (float)(_muTimer + 1d);
        var point = new Complex32(
            _lastSample.Real + (mu * (sample.Real - _lastSample.Real)),
            _lastSample.Imag + (mu * (sample.Imag - _lastSample.Imag)));

        _lastSample = sample;
        _muTimer += _timingPeriod / 2d;

        if (!_haveMid) { _symbolMid = point; _haveMid = true; return; }
        _haveMid = false;

        Complex32 current = point;
        Complex32 previous = _symbolPrev;
        _symbolPrev = current;

        double error = (_symbolMid.Real * (current.Real - previous.Real))
                       + (_symbolMid.Imag * (current.Imag - previous.Imag));
        error = Math.Clamp(error, -1d, 1d);

        // A PI LOOP, AND THE PROPORTIONAL HALF IS NOT OPTIONAL. Correcting only
        // the period is a frequency-only loop: any constant timing offset is a
        // stable point, because the period is already right and nothing pushes
        // the instant back onto the symbol.
        _muTimer += _timingBandwidth * error;
        _timingPeriod += _timingBandwidth * 0.05d * error;
        _timingPeriod = Math.Clamp(_timingPeriod, _samplesPerSymbol * 0.98d, _samplesPerSymbol * 1.02d);

        EmitSymbol(current);
    }

    private void EmitSymbol(Complex32 symbol)
    {
        float cos = (float)Math.Cos(-_costasPhase);
        float sin = (float)Math.Sin(-_costasPhase);
        var rotated = new Complex32(
            (symbol.Real * cos) - (symbol.Imag * sin),
            (symbol.Real * sin) + (symbol.Imag * cos));

        // Re*Im, the textbook order-2 Costas discriminator. A decision-directed
        // form, sign(Re)*Im normalised by amplitude, was tried and DECODED FOUR
        // TIMES WORSE on a strong station - 7 groups against 26 - because it
        // assumes the decisions are right and so locks onto its own mistakes.
        double phaseError = Math.Clamp(rotated.Real * rotated.Imag, -1d, 1d);

        const double Damping = 0.70710678d;
        double denominator = 1d + (2d * Damping * _costasBandwidth) + (_costasBandwidth * _costasBandwidth);
        double alpha = 4d * Damping * _costasBandwidth / denominator;
        double beta = 4d * _costasBandwidth * _costasBandwidth / denominator;

        _costasFreq = Math.Clamp(_costasFreq + (beta * phaseError), -1d, 1d);
        _costasPhase += _costasFreq + (alpha * phaseError);
        if (_costasPhase > Math.PI) _costasPhase -= 2d * Math.PI;
        else if (_costasPhase < -Math.PI) _costasPhase += 2d * Math.PI;

        _lockEstimate += 0.01d * ((Math.Abs(rotated.Real) - Math.Abs(rotated.Imag)) - _lockEstimate);

        if (_pending.Count < MaxPending) _pending.Enqueue(rotated);

        // Order 2 leaves a 180 degree ambiguity, which does not matter: the
        // data is differentially encoded and this differencing cancels it.
        int bit = rotated.Real > 0f ? 1 : 0;
        if (_previousBit < 0) { _previousBit = bit; return; }

        int decoded = bit ^ _previousBit;
        _previousBit = bit;

        _decoder.Feed(decoded != 0);
        HuntForTheOtherLockPoint();
    }

    /// <summary>
    /// Moves the sampling instant half a symbol when nothing is decoding.
    /// </summary>
    /// <remarks>
    /// <b>A biphase signal gives the timing loop TWO stable lock points.</b>
    /// Every bit carries a mid-bit transition, so the eye repeats twice per
    /// symbol and Gardner is equally happy sampling the data or sampling the
    /// seam between adjacent bits. Nothing in the timing loop can tell them
    /// apart - both are minima of the same error - so it settles on whichever
    /// it reaches first, which is a coin toss at every start. The block
    /// synchroniser CAN tell them apart, because only the right phase produces
    /// matching checkwords, so it is what drives this.
    /// </remarks>
    private void HuntForTheOtherLockPoint()
    {
        long groups = _decoder.Groups;
        if (groups != _lastGroupCount)
        {
            _lastGroupCount = groups;
            _symbolsSinceGroup = 0;
            return;
        }

        long patience = _lastGroupCount > 0 ? PhaseHuntSymbolsAfterSuccess : PhaseHuntSymbols;
        if (++_symbolsSinceGroup < patience) return;

        _symbolsSinceGroup = 0;
        _phaseFlips++;
        _muTimer += _timingPeriod / 2d;
        _previousBit = -1;
    }

    // =========================================================================
    // Filter design, by hand. SignalCraft's FirDesign can do the low-pass since
    // 0.3.1, but not a biphase doublet, and a design file could not carry taps
    // at all before scg 0.2 - so both stayed here where they can be read.
    // =========================================================================

    /// <summary>
    /// The matched filter for a biphase (Manchester) symbol: +1 over the first
    /// half of the bit, -1 over the second.
    /// </summary>
    /// <remarks>
    /// <b>NOT a root-raised-cosine, and that was a bug that cost real time.</b>
    /// RDS differentially encodes and then BIPHASE codes, so every bit carries
    /// a mid-bit transition and the spectrum has a NULL at the subcarrier with
    /// peaks at +/-1187.5 Hz. An RRC for a 1187.5 Hz symbol rate is a low-pass
    /// at about +/-800 Hz: it attenuates exactly where the data is and passes
    /// the null. The symptom was a subcarrier clearly present, a timing loop at
    /// the right rate, a carrier reporting lock, and not one valid block.
    /// </remarks>
    private static float[] DesignBiphase(double samplesPerSymbol)
    {
        int taps = Math.Max(4, (int)Math.Round(samplesPerSymbol));
        int half = taps / 2;

        float[] h = new float[taps];
        for (int i = 0; i < taps; i++) h[i] = i < half ? 1f : -1f;

        double energy = 0d;
        foreach (float tap in h) energy += tap * tap;
        double norm = Math.Sqrt(energy);
        for (int i = 0; i < taps; i++) h[i] = (float)(h[i] / norm);

        return h;
    }

    private static float[] DesignLowPass(double sampleRate, double cutoffHz, double transitionHz)
    {
        int taps = (int)Math.Ceiling(3.3d * sampleRate / transitionHz);
        if (taps % 2 == 0) taps++;
        taps = Math.Clamp(taps, 15, 2047);

        float[] h = new float[taps];
        int middle = taps / 2;
        double omega = 2d * Math.PI * cutoffHz / sampleRate;
        double sum = 0d;

        for (int i = 0; i < taps; i++)
        {
            int n = i - middle;
            double sinc = n == 0 ? omega / Math.PI : Math.Sin(omega * n) / (Math.PI * n);
            double window = 0.54d - (0.46d * Math.Cos(2d * Math.PI * i / (taps - 1)));
            double tap = sinc * window;

            h[i] = (float)tap;
            sum += tap;
        }

        for (int i = 0; i < taps; i++) h[i] = (float)(h[i] / sum);
        return h;
    }

    private static Complex32 Convolve(float[] taps, Complex32[] history, int write)
    {
        float real = 0f, imag = 0f;
        int index = write;

        for (int i = 0; i < taps.Length; i++)
        {
            Complex32 sample = history[index];
            real += taps[i] * sample.Real;
            imag += taps[i] * sample.Imag;
            index = index + 1 == history.Length ? 0 : index + 1;
        }

        return new Complex32(real, imag);
    }
}

// =============================================================================
// The protocol half.
// =============================================================================
// A continuous stream of 26-bit blocks with NO FRAMING: 16 data bits then a
// 10-bit checkword, which is a CRC with a per-position offset word added in.
// Four blocks make a group - A, B, C-or-C', D. There is no sync pattern, so the
// synchroniser slides a 26-bit window computing the syndrome, and a syndrome
// equal to offset A means a block A probably ended here.
//
// This half is a PROTOCOL and stays hand-written whatever DSP blocks arrive.
// =============================================================================

internal sealed class RdsGroupDecoder
{
    private const uint Generator = 0x5B9;
    private const int BlockBits = 26;
    private const uint BlockMask = (1u << BlockBits) - 1;

    private const uint OffsetA = 0b0011111100;
    private const uint OffsetB = 0b0110011000;
    private const uint OffsetC = 0b0101101000;
    private const uint OffsetCPrime = 0b1101010000;
    private const uint OffsetD = 0b0110110100;

    private const int MaxConsecutiveBadBlocks = 2;

    // RBDS - the North American programme-type table. Europe numbers the same
    // five bits differently: PTY 5 is "Rock" here and "Education" there. The
    // wrong table does not fail, it just says the wrong thing forever, so this
    // is the one thing in the file a European user has to change.
    private static readonly string[] s_rbds =
    [
        "", "News", "Information", "Sports", "Talk", "Rock", "Classic Rock",
        "Adult Hits", "Soft Rock", "Top 40", "Country", "Oldies", "Soft",
        "Nostalgia", "Jazz", "Classical", "Rhythm and Blues",
        "Soft Rhythm and Blues", "Foreign Language", "Religious Music",
        "Religious Talk", "Personality", "Public", "College", "Spanish Talk",
        "Spanish Music", "Hip-Hop", "", "", "Weather", "Emergency Test",
        "Emergency",
    ];

    private readonly char[] _ps = new char[8];
    private readonly char[] _psCandidate = new char[8];
    private readonly bool[] _psConfirmed = new bool[8];
    private readonly char[] _radioText = new char[64];
    private readonly ushort[] _group = new ushort[4];

    private bool _synced;
    private uint _window;
    private int _bitsInBlock, _blockIndex, _badBlocks;
    private ushort _pi;
    private int _pty;
    private int _radioTextFlag = -1;

    public long Groups { get; private set; }
    public long Errors { get; private set; }

    public RdsGroupDecoder() => Reset();

    public string ProgrammeIdText => Groups > 0 ? $"{_pi:X4}" : "";
    public string ProgrammeType => _pty is >= 0 and < 32 ? s_rbds[_pty] : "";
    public string RadioText => new string(_radioText).TrimEnd();

    public string ProgrammeService
    {
        get
        {
            Span<char> confirmed = stackalloc char[_ps.Length];
            for (int i = 0; i < _ps.Length; i++) confirmed[i] = _psConfirmed[i] ? _ps[i] : ' ';
            return new string(confirmed).Trim();
        }
    }

    public void Reset()
    {
        _synced = false;
        _window = 0;
        _bitsInBlock = 0;
        _blockIndex = 0;
        _badBlocks = 0;
        Array.Clear(_group);

        Array.Fill(_ps, ' ');
        Array.Clear(_psCandidate);
        Array.Clear(_psConfirmed);
        Array.Fill(_radioText, ' ');

        _pi = 0;
        _pty = 0;
        _radioTextFlag = -1;
        Groups = 0;
        Errors = 0;
    }

    public void Feed(bool one)
    {
        _window = ((_window << 1) | (one ? 1u : 0u)) & BlockMask;

        if (!_synced)
        {
            if (Syndrome(_window) != OffsetA) return;

            _synced = true;
            _badBlocks = 0;
            _group[0] = (ushort)(_window >> 10);
            _blockIndex = 1;
            _bitsInBlock = 0;
            return;
        }

        if (++_bitsInBlock < BlockBits) return;
        _bitsInBlock = 0;

        if (!Matches(Syndrome(_window), _blockIndex))
        {
            Errors++;

            if (++_badBlocks > MaxConsecutiveBadBlocks)
            {
                _synced = false;
                _blockIndex = 0;
                return;
            }

            // Skip to the next group: half a group cannot be interpreted,
            // because the group type lives in block B.
            _blockIndex = (_blockIndex + 1) % 4;
            if (_blockIndex != 0) _blockIndex = (_blockIndex + 1) % 4;
            return;
        }

        _badBlocks = 0;
        _group[_blockIndex] = (ushort)(_window >> 10);

        if (_blockIndex == 3)
        {
            Groups++;
            HandleGroup();
        }

        _blockIndex = (_blockIndex + 1) % 4;
    }

    /// <summary>
    /// The RDS checkword is a shortened cyclic code, and this only DETECTS.
    /// </summary>
    /// <remarks>
    /// The code can correct burst errors up to 5 bits and the standard expects
    /// a receiver to do it, which would recover most of the blocks thrown away
    /// here. Implementing it is the single change most likely to improve the
    /// group rate: precompute a syndrome-to-error-pattern table and XOR the
    /// pattern back in rather than dropping the block.
    /// </remarks>
    private static uint Syndrome(uint block)
    {
        uint remainder = block;
        for (int i = BlockBits - 1; i >= 10; i--)
        {
            if (((remainder >> i) & 1) != 0) remainder ^= Generator << (i - 10);
        }

        return remainder & 0x3FF;
    }

    private static bool Matches(uint syndrome, int blockIndex) => blockIndex switch
    {
        0 => syndrome == OffsetA,
        1 => syndrome == OffsetB,
        2 => syndrome is OffsetC or OffsetCPrime,
        3 => syndrome == OffsetD,
        _ => false,
    };

    private void HandleGroup()
    {
        ushort a = _group[0], b = _group[1], c = _group[2], d = _group[3];

        _pi = a;
        int groupType = (b >> 12) & 0xF;
        bool versionB = ((b >> 11) & 1) != 0;
        _pty = (b >> 5) & 0x1F;

        switch (groupType)
        {
            case 0:
                ProgrammeServiceChars(b & 0x3, (char)((d >> 8) & 0xFF), (char)(d & 0xFF));
                break;

            case 2 when !versionB:
                RadioTextChars(b & 0xF, (b >> 4) & 1, 4,
                    (char)((c >> 8) & 0xFF), (char)(c & 0xFF),
                    (char)((d >> 8) & 0xFF), (char)(d & 0xFF));
                break;

            case 2 when versionB:
                RadioTextChars(b & 0xF, (b >> 4) & 1, 2,
                    (char)((d >> 8) & 0xFF), (char)(d & 0xFF));
                break;
        }
    }

    /// <summary>
    /// A character is published only on its SECOND identical sighting, so the
    /// name does not assemble in public and one corrupted group that passes
    /// its checksum cannot leave a wrong letter in it.
    /// </summary>
    private void ProgrammeServiceChars(int address, params char[] chars)
    {
        int at = address * 2;

        for (int i = 0; i < chars.Length; i++)
        {
            int slot = at + i;
            if (slot >= _ps.Length) break;

            char ch = Printable(chars[i]);
            if (_psCandidate[slot] == ch && ch != '\0')
            {
                _ps[slot] = ch;
                _psConfirmed[slot] = true;
            }

            _psCandidate[slot] = ch;
        }
    }

    /// <summary>
    /// The A/B flag toggles when the station starts a new message. Ignoring it
    /// leaves the tail of the last song sitting after the start of this one.
    /// </summary>
    private void RadioTextChars(int address, int flag, int charsPerGroup, params char[] chars)
    {
        if (_radioTextFlag != flag)
        {
            Array.Fill(_radioText, ' ');
            _radioTextFlag = flag;
        }

        int at = address * charsPerGroup;

        for (int i = 0; i < chars.Length; i++)
        {
            int slot = at + i;
            if (slot >= _radioText.Length) break;

            if (chars[i] == '\r')
            {
                for (int j = slot; j < _radioText.Length; j++) _radioText[j] = ' ';
                return;
            }

            char ch = Printable(chars[i]);
            _radioText[slot] = ch == '\0' ? ' ' : ch;
        }
    }

    /// <summary>
    /// ASCII only. RDS defines its own G0 table whose upper half carries
    /// accented and Greek characters; this rejects them rather than showing
    /// the wrong letter. Fine in North America, wrong in Europe.
    /// </summary>
    private static char Printable(char c) => c is >= ' ' and < (char)127 ? c : '\0';
}
