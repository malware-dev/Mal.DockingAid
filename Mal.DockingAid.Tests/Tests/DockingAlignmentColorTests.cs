using System;
using NUnit.Framework;
using VRageMath;

namespace Mal.DockingAid.Tests.Tests
{
    /// <summary>
    ///     Pins the <see cref="DockingAlignment.ColorFor"/> Good/Warn/Critical
    ///     ladder against the documented thresholds. The thresholds live as
    ///     private constants in production code, so these tests use the same
    ///     numeric values to detect drift.
    /// </summary>
    [TestFixture]
    public class DockingAlignmentColorTests
    {
        // Mirror of the private thresholds in DockingAlignment.cs.
        const double GoodLateralM = 0.3;
        const double GoodAlignDeg = 5.0;
        const double GoodRollDeg = 10.0;
        const double WarnLateralM = 1.5;
        const double WarnAlignDeg = 20.0;
        const double WarnRollDeg = 30.0;

        static readonly DockingAidPalette P = DockingAidPalette.From(Color.White, Color.Black);

        static AlignmentData At(double lateralM, double alignDeg, double rollDeg)
        {
            return new AlignmentData
            {
                Range = 5.0,
                LateralLength = lateralM,
                AlignmentDeg = alignDeg,
                RollRadians = rollDeg * (Math.PI / 180.0),
            };
        }

        [Test]
        public void All_three_inside_good_band_returns_good()
        {
            Assert.That(DockingAlignment.ColorFor(At(0.1, 1.0, 2.0), P),
                Is.EqualTo(P.Good));
        }

        [Test]
        public void Just_outside_good_lateral_falls_to_warn()
        {
            // 0.31 m exceeds the 0.30 m good threshold but is well inside the
            // 1.5 m warn threshold; the other two stay inside good — picking
            // out the lateral axis as the demoter.
            Assert.That(DockingAlignment.ColorFor(At(0.31, 1.0, 2.0), P),
                Is.EqualTo(P.Warn));
        }

        [Test]
        public void Just_outside_good_alignment_falls_to_warn()
        {
            Assert.That(DockingAlignment.ColorFor(At(0.1, GoodAlignDeg + 0.01, 2.0), P),
                Is.EqualTo(P.Warn));
        }

        [Test]
        public void Just_outside_good_roll_falls_to_warn()
        {
            Assert.That(DockingAlignment.ColorFor(At(0.1, 1.0, GoodRollDeg + 0.01), P),
                Is.EqualTo(P.Warn));
        }

        [Test]
        public void Sign_of_roll_does_not_matter()
        {
            // ColorFor takes |RollRadians|; -8° must be treated identically to +8°.
            Assert.That(DockingAlignment.ColorFor(At(0.1, 1.0, -8.0), P),
                Is.EqualTo(P.Good));
            Assert.That(DockingAlignment.ColorFor(At(0.1, 1.0, 8.0), P),
                Is.EqualTo(P.Good));
        }

        [Test]
        public void Outside_warn_lateral_falls_to_critical()
        {
            Assert.That(DockingAlignment.ColorFor(At(WarnLateralM + 0.01, 1.0, 2.0), P),
                Is.EqualTo(P.Critical));
        }

        [Test]
        public void Outside_warn_alignment_falls_to_critical()
        {
            Assert.That(DockingAlignment.ColorFor(At(0.1, WarnAlignDeg + 0.01, 2.0), P),
                Is.EqualTo(P.Critical));
        }

        [Test]
        public void Outside_warn_roll_falls_to_critical()
        {
            Assert.That(DockingAlignment.ColorFor(At(0.1, 1.0, WarnRollDeg + 0.01), P),
                Is.EqualTo(P.Critical));
        }

        [Test]
        public void Single_axis_outside_warn_demotes_even_with_other_axes_perfect()
        {
            // The ladder is conjunctive — ANY axis outside warn means critical,
            // regardless of how good the others are.
            Assert.That(DockingAlignment.ColorFor(At(WarnLateralM + 0.01, 0.0, 0.0), P),
                Is.EqualTo(P.Critical));
        }

        [TestCase(GoodLateralM, GoodAlignDeg, GoodRollDeg)]
        public void Boundary_values_count_as_inside_good(double lat, double ang, double roll)
        {
            // The thresholds are inclusive (`<=`), so exactly-at-the-limit
            // readings still qualify for green. Locks the boundary semantics.
            Assert.That(DockingAlignment.ColorFor(At(lat, ang, roll), P),
                Is.EqualTo(P.Good));
        }

        [TestCase(WarnLateralM, WarnAlignDeg, WarnRollDeg)]
        public void Boundary_values_count_as_inside_warn(double lat, double ang, double roll)
        {
            // Just inside warn — `<=` treats the warn boundary as warn, not critical.
            Assert.That(DockingAlignment.ColorFor(At(lat, ang, roll), P),
                Is.EqualTo(P.Warn));
        }
    }
}
