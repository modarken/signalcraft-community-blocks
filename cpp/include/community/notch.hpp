// A C++ block shipped by a library.
//
// The same core::IBlock every SignalCraft C++ block implements. What differs is
// only that it is compiled and linked by whoever uses it, rather than being
// compiled into SignalCraft - so the studio cannot ask it what it is, and
// blocks.json says instead.

#ifndef COMMUNITY_NOTCH_HPP
#define COMMUNITY_NOTCH_HPP

#include <signalcraft/core/block.hpp>

#include <cmath>
#include <numbers>

namespace community {

/// A two-pole notch: removes one frequency and leaves the rest.
///
/// The usual reason is a tone you did not ask for - mains hum at 50 or 60 Hz, a
/// pilot leaking into audio, a birdie from a mixer. `frequency` is what to
/// remove and `width` is how sharply, in Hz; a narrower notch takes less of the
/// signal with it and rings longer.
class NotchBlock final : public signalcraft::core::IBlock {
public:
    NotchBlock() { design(); }

    [[nodiscard]] const char* name() const noexcept override { return "Notch"; }

    [[nodiscard]] signalcraft::core::BlockType block_type() const noexcept override {
        return signalcraft::core::BlockType::Transform;
    }

    void process(std::span<signalcraft::core::Buffer> in,
                 std::span<signalcraft::core::Buffer> out) override {
        const auto* src = in[0].data<float>();
        auto* dst = out[0].data<float>();
        const std::size_t n = std::min(in[0].count(), out[0].count());

        for (std::size_t i = 0; i < n; ++i) {
            const double x = static_cast<double>(src[i]);
            const double y = b0_ * x + b1_ * x1_ + b2_ * x2_ - a1_ * y1_ - a2_ * y2_;
            x2_ = x1_;
            x1_ = x;
            y2_ = y1_;
            y1_ = y;
            dst[i] = static_cast<float>(y);
        }

        in[0].set_consumed(n);
        out[0].set_produced(n);
    }

    [[nodiscard]] bool try_set_parameter(std::string_view key, double value) override {
        if (key == "frequency") {
            frequency_ = value;
            design();
            return true;
        }
        if (key == "width") {
            width_ = value;
            design();
            return true;
        }
        if (key == "sample_rate") {
            sample_rate_ = value;
            design();
            return true;
        }
        return false;
    }

    [[nodiscard]] bool try_get_parameter(std::string_view key, double* out) const override {
        if (out == nullptr) return false;
        if (key == "frequency") { *out = frequency_; return true; }
        if (key == "width")     { *out = width_;     return true; }
        if (key == "sample_rate") { *out = sample_rate_; return true; }
        return false;
    }

private:
    /// The standard RBJ notch. Recomputed whenever a parameter moves, which is
    /// cheap and keeps the coefficients and the parameters from disagreeing.
    void design() {
        const double w0 = 2.0 * std::numbers::pi * frequency_ / sample_rate_;
        const double alpha = std::sin(w0) * std::sinh(
            std::log(2.0) / 2.0 * (width_ / frequency_) * (w0 / std::sin(w0)));
        const double a0 = 1.0 + alpha;

        b0_ = 1.0 / a0;
        b1_ = -2.0 * std::cos(w0) / a0;
        b2_ = 1.0 / a0;
        a1_ = -2.0 * std::cos(w0) / a0;
        a2_ = (1.0 - alpha) / a0;
    }

    double sample_rate_ = 48000.0;
    double frequency_ = 50.0;
    double width_ = 4.0;

    double b0_ = 1.0, b1_ = 0.0, b2_ = 0.0, a1_ = 0.0, a2_ = 0.0;
    double x1_ = 0.0, x2_ = 0.0, y1_ = 0.0, y2_ = 0.0;
};

}  // namespace community

#endif  // COMMUNITY_NOTCH_HPP
