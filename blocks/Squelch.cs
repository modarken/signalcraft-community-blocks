// A C# block, shipped by a library rather than written beside a design.
//
// Nothing about the class says it came from a library: it is the same BlockBase
// every SignalCraft C# block derives from, with the same descriptors and the
// same Process. What differs is only where the file lives and how it got onto
// the machine.
//
// The usings for System, System.Collections.Generic, SignalCraft.Core and
// SignalCraft.Hosting are implicit in a block compiled from source.

namespace Community;

/// <summary>
/// Passes audio through while it is loud enough, and mutes it when it is not.
///
/// The classic squelch: below the threshold you get silence instead of hiss.
/// `threshold` is the level a sample has to reach, and `hold` is how many
/// samples of quiet to tolerate before muting - without it, a waveform passing
/// through zero would chop the signal into fragments.
/// </summary>
public sealed class Squelch : BlockBase, IHostedBlock
{
    private static readonly HostParamDescriptor[] s_params =
    [
        HostParamDescriptor.Number("threshold", "Threshold", 0.0, 1.0),
        HostParamDescriptor.Number("hold", "Hold (samples)", 0.0, 48000.0),
    ];
    private static readonly HostPortDescriptor[] s_inputs = [HostPortDescriptor.FloatIn()];
    private static readonly HostPortDescriptor[] s_outputs = [HostPortDescriptor.FloatOut()];

    private float _threshold = 0.02f;
    private int _hold = 480;
    private int _quiet;

    public override string Name => "Squelch";
    public override BlockType BlockType => BlockType.Transform;
    public override bool SupportsInPlace => true;
    public override ReadOnlySpan<HostPortDescriptor> InputPorts => s_inputs;
    public override ReadOnlySpan<HostPortDescriptor> OutputPorts => s_outputs;
    public override ReadOnlySpan<HostParamDescriptor> Parameters => s_params;

    public static ReadOnlySpan<HostParamDescriptor> Describe() => s_params;
    public static string Category => "level";
    public static ReadOnlySpan<HostPortDescriptor> DescribeInputPorts() => s_inputs;
    public static ReadOnlySpan<HostPortDescriptor> DescribeOutputPorts() => s_outputs;

    public override bool TrySetParameter(string name, double value)
    {
        switch (name)
        {
            case "threshold":
                _threshold = (float)value;
                return true;
            case "hold":
                _hold = (int)value;
                return true;
            default:
                return false;
        }
    }

    public override bool TryGetParameter(string name, out double value)
    {
        switch (name)
        {
            case "threshold":
                value = _threshold;
                return true;
            case "hold":
                value = _hold;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    public override void Process(Span<InteropBuffer> readables, Span<InteropBuffer> writables)
    {
        ReadOnlySpan<float> input = readables[0].AsReadOnlySpan<float>();
        Span<float> output = writables[0].AsSpan<float>();
        int n = Math.Min(input.Length, output.Length);

        for (int i = 0; i < n; i++)
        {
            float sample = input[i];
            if (Math.Abs(sample) >= _threshold)
            {
                _quiet = 0;
            }
            else if (_quiet < _hold)
            {
                _quiet++;
            }
            output[i] = _quiet >= _hold ? 0f : sample;
        }

        readables[0].Consumed = n;
        writables[0].Produced = n;
    }
}
