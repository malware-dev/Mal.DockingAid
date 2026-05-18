using NUnit.Framework;

namespace Mal.DockingAid.Tests.Tests
{
    [TestFixture]
    public class ClosureTrackerTests
    {
        // SE physics tick is 60 Hz, mirroring ClosureTracker.Update's dt = ticks/60.
        const int Hz = 60;

        [Test]
        public void First_sample_for_new_target_returns_zero_and_seeds_baseline()
        {
            var t = new ClosureTracker();
            // No prior sample → can't compute a closure rate yet.
            Assert.That(t.Update(targetId: 1, currentRange: 100.0, currentTick: 0),
                Is.EqualTo(0f));
        }

        [Test]
        public void Same_tick_resample_holds_smoothed_value_steady()
        {
            var t = new ClosureTracker();
            t.Update(1, 100.0, 0);
            // First non-seed sample at tick 60: range fell 1 m in 1 s → 1 m/s.
            float first = t.Update(1, 99.0, Hz);
            // Re-querying at the same tick is a no-op (dt <= 0): no double counting.
            float repeat = t.Update(1, 50.0, Hz);
            Assert.That(repeat, Is.EqualTo(first));
        }

        [Test]
        public void Approaching_yields_positive_closure_signed_correctly()
        {
            var t = new ClosureTracker();
            t.Update(1, 100.0, 0);
            float v = t.Update(1, 99.0, Hz);
            // Range is decreasing → closure is positive (approaching).
            Assert.That(v, Is.GreaterThan(0f));
        }

        [Test]
        public void Receding_yields_negative_closure()
        {
            var t = new ClosureTracker();
            t.Update(1, 100.0, 0);
            float v = t.Update(1, 105.0, Hz);
            Assert.That(v, Is.LessThan(0f));
        }

        [Test]
        public void Smoothing_low_passes_a_step_change_below_the_raw_value()
        {
            var t = new ClosureTracker();
            t.Update(1, 100.0, 0);
            // Raw closure = 1.0 m/s. EMA with α=0.4 → 0.4 m/s on the first
            // post-seed sample (smoothed *= 0.6 + raw *= 0.4, smoothed starts
            // at 0). The exact constant is locked by the production code.
            float v = t.Update(1, 99.0, Hz);
            Assert.That(v, Is.EqualTo(0.4f).Within(1e-4f));
        }

        [Test]
        public void Switching_target_resets_smoothing_state()
        {
            var t = new ClosureTracker();
            t.Update(1, 100.0, 0);
            float t1Closure = t.Update(1, 99.0, Hz);
            Assert.That(t1Closure, Is.GreaterThan(0f));
            // New target ID means a fresh lock — last-target velocity must
            // not bleed in. First sample on the new target reads 0.
            float t2First = t.Update(2, 50.0, 2 * Hz);
            Assert.That(t2First, Is.EqualTo(0f));
        }

        [Test]
        public void Reset_zeroes_smoothing_and_target_id()
        {
            var t = new ClosureTracker();
            t.Update(1, 100.0, 0);
            t.Update(1, 99.0, Hz); // smoothed != 0 now
            t.Reset();
            // After Reset, the next Update for any target sees no prior
            // sample — first reading is 0 again.
            Assert.That(t.Update(1, 99.0, 2 * Hz), Is.EqualTo(0f));
        }

        [Test]
        public void Smoothing_converges_toward_the_raw_value_under_steady_state()
        {
            var t = new ClosureTracker();
            t.Update(1, 100.0, 0);
            // 60 consecutive 1-tick samples at constant 1 m/s closure should
            // drive the EMA arbitrarily close to 1.0 m/s.
            float v = 0f;
            for (int i = 1; i <= 60; i++)
                v = t.Update(1, 100.0 - i * (1.0 / Hz), i);
            Assert.That(v, Is.EqualTo(1f).Within(1e-3f));
        }
    }
}
