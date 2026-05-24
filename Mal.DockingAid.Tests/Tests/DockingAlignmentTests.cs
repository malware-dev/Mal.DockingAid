using System;
using Mal.DockingAid.Tests.TestUtilities;
using NUnit.Framework;
using VRageMath;

namespace Mal.DockingAid.Tests.Tests
{
    /// <summary>
    ///     Pins <see cref="DockingAlignment.Compute"/> against hand-set
    ///     geometric configurations. Compute now returns Input{Pitch,Yaw,Roll}
    ///     in the PILOT frame (axis-angle scalars: "stick input that nulls the
    ///     error"), so the test setup picks a pilot frame and translates the
    ///     expected bore-frame values into input-frame ones.
    ///
    ///     The source connector in every scenario faces +Z. With SE-standard
    ///     pilot (right=+X, up=+Y, forward=−Z) that makes it an AFT mount
    ///     (bore = −pilot.forward). Sign consequences for the input frame:
    ///       InputPitch = −(bore-frame pitch)   (ship-pitch axis is shared with
    ///                                           the bore-tilt direction but the
    ///                                           bore points backward)
    ///       InputYaw   = +(bore-frame yaw)      (ship-yaw axis is symmetric)
    ///       InputRoll  = −MatingRoll            (ship-roll axis is shared
    ///                                           with the bore but reversed)
    /// </summary>
    [TestFixture]
    public class DockingAlignmentTests
    {
        const double Sep = 8.0;

        // SE-standard pilot frame used by every test below.
        static readonly Vector3D PR = new Vector3D(1, 0, 0);
        static readonly Vector3D PU = new Vector3D(0, 1, 0);
        static readonly Vector3D PF = new Vector3D(0, 0, -1);

        [Test]
        public void Perfectly_aligned_connectors_report_zero_lateral_and_zero_alignment_deg()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            // Range = mate-to-mate distance: source mate at (0,0,1.25), target
            // mate at (0,0,Sep − 1.25); range = 5.5.
            Assert.That(a.Range, Is.EqualTo(Sep - 1.25 - 1.25).Within(1e-9));
            Assert.That(a.LateralLength, Is.LessThan(1e-9));
            Assert.That(a.AlignmentDeg, Is.LessThan(1e-6));
            Assert.That(a.InputPitch, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(a.InputYaw, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(a.InputRoll, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(a.MatingRoll, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void Lateral_offset_shows_up_in_lateral_length()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(1, 0, Sep),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            Assert.That(a.LateralLength, Is.EqualTo(1.0).Within(1e-9));
        }


        [Test]
        public void Roll_error_signed_matches_target_up_rotation()
        {
            // Target rotated +30° about source forward (+Z): MatingRoll = +t.
            // Aft mount ⇒ InputRoll = −MatingRoll = −t.
            const double thetaDeg = 30.0;
            double t = thetaDeg * (Math.PI / 180.0);

            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1),
                up: new Vector3D(Math.Sin(t), Math.Cos(t), 0));

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            Assert.That(a.MatingRoll, Is.EqualTo(t).Within(1e-6));
            Assert.That(a.InputRoll, Is.EqualTo(t).Within(1e-6),
                "stick-direction rule: target up tilted toward screen-right ⇒ "
                + "pilot rolls right ⇒ +InputRoll, same sign on every mount");
        }

        [Test]
        public void Input_pitch_yaw_zero_when_axes_are_anti_parallel()
        {
            // nose-error = 0 ⇒ no bore-tilt contribution. mating-up matches
            // screen up ⇒ no roll contribution. All three input axes zero.
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 1, Sep),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            Assert.That(a.InputPitch, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(a.InputYaw, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(a.InputRoll, Is.EqualTo(0.0).Within(1e-9));
        }

        // SE connectors lock at any roll; a 180°-apart mount is the same
        // connectable face. Folded MatingRoll must read ~0 here (and the
        // downstream InputRoll too).
        [Test]
        public void Dockable_pose_with_antiparallel_ups_needs_no_roll()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            // 180° about source Right (X): forward (0,0,-1), up (0,-1,0).
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1), up: new Vector3D(0, -1, 0));

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            Assert.That(a.AlignmentDeg, Is.LessThan(1e-6),
                "forwards anti-parallel: this pose docks in-game");
            Assert.That(a.MatingRoll, Is.EqualTo(0.0).Within(1e-6),
                "180°-apart mount is connectable as-is");
            Assert.That(a.InputRoll, Is.EqualTo(0.0).Within(1e-6));
        }

        // Fold-by-π/2 caps MatingRoll at ±45°.
        [Test]
        public void Mating_roll_never_exceeds_45_degrees()
        {
            const double rawDeg = 135.0;
            double t = rawDeg * (Math.PI / 180.0);

            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1),
                up: new Vector3D(Math.Sin(t), Math.Cos(t), 0));

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            Assert.That(Math.Abs(a.MatingRoll), Is.LessThanOrEqualTo(Math.PI / 4.0 + 1e-9),
                "fold by π/2 ⇒ |MatingRoll| ≤ 45°");
            // Raw +135° sits exactly between +90° and +180° cardinals; banker's
            // rounding picks +180° as the nearest, leaving residual −45°.
            Assert.That(a.MatingRoll * (180.0 / Math.PI), Is.EqualTo(-45.0).Within(1e-6));
        }

        // (B) regression: target connector built rolled 90° on a 4-symmetric
        // face must not demand any roll; all 4 cardinals read as docked.
        [Test]
        public void Target_connector_built_rolled_90deg_does_not_demand_roll()
        {
            foreach (var tgtUp in new[]
            {
                new Vector3D(0, 1, 0),    //   0° build-roll
                new Vector3D(1, 0, 0),    //  90°
                new Vector3D(0, -1, 0),   // 180°
                new Vector3D(-1, 0, 0),   // 270°
            })
            {
                var src = FakeConnector.At(Vector3D.Zero,
                    forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
                var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                    forward: new Vector3D(0, 0, -1), up: tgtUp);

                var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

                Assert.That(a.MatingRoll, Is.EqualTo(0.0).Within(1e-6),
                    "target tgtUp=" + tgtUp + " is a cardinal mount");
                Assert.That(a.InputRoll, Is.EqualTo(0.0).Within(1e-6));
            }
        }

        // Target nose tilted so its anti-forward leans toward +Y (pilot up).
        // Aft mount: rotating about pilot.right by +pitch tilts bore DOWN
        // (because bore = +Z = −pilot.fwd). So bringing bore UP needs −pitch.
        [Test]
        public void Pitch_input_points_against_screen_up_for_aft_mount()
        {
            const double thetaDeg = 20.0;
            double t = thetaDeg * (Math.PI / 180.0);
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, -Math.Sin(t), -Math.Cos(t)),
                up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            Assert.That(a.InputPitch, Is.EqualTo(-Math.Sin(t)).Within(1e-6),
                "aft mount + target above ⇒ pitch DOWN to swing rear bore up");
            Assert.That(a.InputYaw, Is.EqualTo(0.0).Within(1e-9));
        }

        // Aft mount + target's anti-forward leans toward +X (pilot right):
        // bore is at +Z, needs to swing toward +X. That's +rotation about
        // +pilotUp, which is −yaw stick (yaw LEFT). So InputYaw is NEGATIVE,
        // chevron LEFT — pilot pushes yaw left to align.
        [Test]
        public void Aft_mount_target_right_needs_yaw_left()
        {
            const double thetaDeg = 20.0;
            double t = thetaDeg * (Math.PI / 180.0);
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(-Math.Sin(t), 0, -Math.Cos(t)),
                up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            Assert.That(a.InputYaw, Is.EqualTo(-Math.Sin(t)).Within(1e-6),
                "aft mount: target right ⇒ pilot yaws LEFT to swing rear bore right");
            Assert.That(a.InputPitch, Is.EqualTo(0.0).Within(1e-9));
        }

        // Pitch/yaw input must not depend on the source connector's arbitrary
        // build-roll: same forwards + same pilot frame ⇒ identical input
        // demands regardless of source-up choice.
        [Test]
        public void Input_pitch_yaw_independent_of_connector_build_roll()
        {
            double t = 25.0 * (Math.PI / 180.0);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, -Math.Sin(t), -Math.Cos(t)),
                up: Vector3D.Up);
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            var srcRolled = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(1, 0, 0)); // 90° roll

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);
            var b = DockingAlignment.Compute(srcRolled, tgt, PR, PU, PF);

            Assert.That(b.InputPitch, Is.EqualTo(a.InputPitch).Within(1e-9));
            Assert.That(b.InputYaw, Is.EqualTo(a.InputYaw).Within(1e-9));
        }

        // Roll input also must not depend on source connector build-roll.
        [Test]
        public void Input_roll_independent_of_connector_build_roll()
        {
            double t = 22.0 * (Math.PI / 180.0);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1),
                up: new Vector3D(Math.Sin(t), Math.Cos(t), 0));
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            var srcRolled = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(1, 0, 0)); // 90° roll

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);
            var b = DockingAlignment.Compute(srcRolled, tgt, PR, PU, PF);

            Assert.That(b.InputRoll, Is.EqualTo(a.InputRoll).Within(1e-9));
        }

        // Regression for the rear-mounted connector built rolled 90°: the
        // mating face is connectable as-is, so MatingRoll and InputRoll both
        // read ~0 (not a fabricated 90° demand).
        [Test]
        public void Rear_mounted_connector_does_not_fabricate_90deg_roll()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(1, 0, 0)); // built rolled 90°
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1), up: new Vector3D(0, 1, 0));

            var a = DockingAlignment.Compute(src, tgt, PR, PU, PF);

            Assert.That(a.MatingRoll, Is.EqualTo(0.0).Within(1e-6),
                "rear-mounted connector must not demand a phantom 90° roll");
            Assert.That(a.InputRoll, Is.EqualTo(0.0).Within(1e-6));
        }
    }
}
