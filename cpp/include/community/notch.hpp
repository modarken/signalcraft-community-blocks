#pragma once

// =============================================================================
// A C++ block shipped by a library
// =============================================================================
// The same core::WritableBlock every SignalCraft C++ block derives from, with
// the same port table, the same parameter container and the same process().
// Nothing about the class says it came from outside SignalCraft.
//
// What differs is only that it is compiled and linked by whoever USES it,
// rather than being compiled into SignalCraft - so nothing can ask the binary
// what blocks it holds, and `blocks.json` beside this repository's root says
// instead. That file carries the class and the header a generated project needs
// to construct this, which is the whole reason it exists.
// =============================================================================

#include "signalcraft/core/block_base.hpp"
#include "signalcraft/core/parameters.hpp"

#include <array>
#include <vector>
#include <cmath>
#include <numbers>

namespace community {

namespace core = signalcraft::core;

/// Construction parameters for NotchBlock.
struct NotchParams {
    double sample_rate = 48000.0;  ///< Samples per second the filter is designed for
    double frequency = 50.0;       ///< The frequency to remove, in Hz
    double width = 4.0;            ///< How wide the notch is, in Hz (-3 dB)
};

enum class NotchParam : std::size_t {
    SampleRate = 0,
    Frequency,
    Width,
    Count_
};

inline const std::array<core::ParamDescriptor, static_cast<std::size_t>(NotchParam::Count_)>
NotchDescriptors{{
    {
        .id = "sample_rate",
        .label = "Sample rate",
        .doc = "Samples per second the filter is designed for",
        .type = core::ParamType::Float,
        .access = core::ParamAccess::ReadWrite,
        .constraints = {.min = 1.0, .max = 100000000.0},
    },
    {
        .id = "frequency",
        .label = "Frequency",
        .doc = "The frequency to remove, in Hz",
        .type = core::ParamType::Float,
        .access = core::ParamAccess::ReadWrite,
        .constraints = {.min = 1.0, .max = 1000000.0},
    },
    {
        .id = "width",
        .label = "Width",
        .doc = "How wide the notch is, in Hz. Narrower takes less of the signal with it and rings longer",
        .type = core::ParamType::Float,
        .access = core::ParamAccess::ReadWrite,
        .constraints = {.min = 0.1, .max = 10000.0},
    },
}};

/// Removes one frequency and leaves the rest.
///
/// The usual reason is a tone you did not ask for - mains hum at 50 or 60 Hz, a
/// pilot leaking into audio, a birdie from a mixer. A standard two-pole RBJ
/// notch; the coefficients are recomputed whenever a parameter moves, which is
/// cheap and keeps them from disagreeing with the parameters.
class NotchBlock : public core::WritableBlock {
public:
    static constexpr core::PortDecl kInputs[] = {
        {"in", "In", core::StreamType::Float32},
    };
    static constexpr core::PortDecl kOutputs[] = {
        {"out", "Out", core::StreamType::Float32},
    };
    static constexpr core::PortTable kPorts{kInputs, kOutputs};

    using Params = NotchParams;

    explicit NotchBlock(const NotchParams& p = {}) {
        params_.define(idx(NotchParam::SampleRate),
                       NotchDescriptors[idx(NotchParam::SampleRate)], p.sample_rate);
        params_.define(idx(NotchParam::Frequency),
                       NotchDescriptors[idx(NotchParam::Frequency)], p.frequency);
        params_.define(idx(NotchParam::Width),
                       NotchDescriptors[idx(NotchParam::Width)], p.width);
        design();
    }

    [[nodiscard]] std::string_view name() const noexcept override { return "Notch"; }

    [[nodiscard]] std::span<const core::PortDescriptor> inputs() const noexcept override {
        return in_;
    }
    [[nodiscard]] std::span<const core::PortDescriptor> outputs() const noexcept override {
        return out_;
    }

    [[nodiscard]] std::span<const core::ParamDescriptor> parameters() const noexcept override {
        return NotchDescriptors;
    }

    [[nodiscard]] core::ParamValue get_param(std::string_view key) const override {
        return params_.get(key);
    }

    bool set_param(std::string_view key, const core::ParamValue& value) override {
        if (!params_.set(key, value)) {
            return false;
        }
        design();
        return true;
    }

    void process(std::span<core::ReadableBuffer> readables,
                 std::span<core::WritableBuffer> writables) override {
        const auto in = readables[0].as<float>();
        const auto out = writables[0].as<float>();
        const std::size_t n = std::min(in.size(), out.size());

        for (std::size_t i = 0; i < n; ++i) {
            const double x = static_cast<double>(in[i]);
            const double y = b0_ * x + b1_ * x1_ + b2_ * x2_ - a1_ * y1_ - a2_ * y2_;
            x2_ = x1_;
            x1_ = x;
            y2_ = y1_;
            y1_ = y;
            out[i] = static_cast<float>(y);
        }

        // The block says how much it used and how much it wrote; the scheduler
        // reads these back. A block that leaves them at zero stalls the graph.
        readables[0].consumed = static_cast<int>(n);
        writables[0].produced = static_cast<int>(n);
    }

private:
    static constexpr std::size_t idx(NotchParam p) noexcept {
        return static_cast<std::size_t>(p);
    }

    /// The standard RBJ notch.
    void design() {
        const double fs = params_.get<double>(idx(NotchParam::SampleRate));
        const double f0 = params_.get<double>(idx(NotchParam::Frequency));
        const double bw = params_.get<double>(idx(NotchParam::Width));
        if (fs <= 0.0 || f0 <= 0.0 || bw <= 0.0) {
            return;
        }

        const double w0 = 2.0 * std::numbers::pi * f0 / fs;
        const double sin_w0 = std::sin(w0);
        if (sin_w0 == 0.0) {
            return;
        }
        const double alpha = sin_w0 * std::sinh(
            std::log(2.0) / 2.0 * (bw / f0) * (w0 / sin_w0));
        const double a0 = 1.0 + alpha;

        b0_ = 1.0 / a0;
        b1_ = -2.0 * std::cos(w0) / a0;
        b2_ = 1.0 / a0;
        a1_ = -2.0 * std::cos(w0) / a0;
        a2_ = (1.0 - alpha) / a0;
    }

    core::Parameters params_;

    // Built from kPorts, so the ports are declared ONCE (ADR-013): the table
    // above is what an authoring tool reads with no instance, and these are the
    // same thing for the running block.
    std::vector<core::PortDescriptor> in_ = kPorts.make_inputs();
    std::vector<core::PortDescriptor> out_ = kPorts.make_outputs();

    double b0_ = 1.0, b1_ = 0.0, b2_ = 0.0, a1_ = 0.0, a2_ = 0.0;
    double x1_ = 0.0, x2_ = 0.0, y1_ = 0.0, y2_ = 0.0;
};

}  // namespace community
