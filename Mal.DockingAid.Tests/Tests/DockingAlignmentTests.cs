using System;
using Mal.DockingAid.Tests.TestUtilities;
using NUnit.Framework;
using VRageMath;

namespace Mal.DockingAid.Tests.Tests
{
    /// <summary>
    ///     Pins <see cref="DockingAlignment.Compute"/> against a small set of
    ///     hand-set geometric configurations. The alignment math is the heart
    ///     of the indicator, so these scenarios are kept simple enough that
    ///     the expected values are obvious by inspection.
    /// </summary>
    [TestFixture]
    public class DockingAlignmentTests
    {
        // Connectors mate front-to-front: source forward = -target forward.
        // Place source at origin facing +Z, target N metres ahead facing -Z.
        const double Sep = 8.0;

        // Screen basis for these scenarios is world +X right / +Y up, passed
        // straight to Compute. It equals the source frame here, so the
        // expected values stay obvious by inspection.

        [Test]
        public void Perfectly_aligned_connectors_report_zero_lateral_and_zero_alignment_deg()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            // Range = mate-to-mate distance: source mate is at (0,0,1.25),
            // target mate is at (0,0,Sep − 1.25) = (0,0,6.75); range = 5.5.
            Assert.That(a.Range, Is.EqualTo(Sep - 1.25 - 1.25).Within(1e-9));
            Assert.That(a.LateralLength, Is.LessThan(1e-9));
            // Forward alignment is the rotation from -180° (anti-parallel,
            // perfect) to 0° (parallel, worst). Perfect mating ⇒ 0° from goal.
            Assert.That(a.AlignmentDeg, Is.LessThan(1e-6));
            Assert.That(a.RollRadians, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void Lateral_offset_shows_up_in_lateral_length_and_lcd_axes()
        {
            // Target shifted +1 m to source's right (along source +X). With
            // LCD-aligned-to-source, LateralXLcd should pick up that 1 m, Y zero.
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(1, 0, Sep),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(a.LateralLength, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(a.LateralXLcd, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(a.LateralYLcd, Is.EqualTo(0.0).Within(1e-9));
        }


        [Test]
        public void Roll_error_is_signed_and_matches_the_target_up_rotation()
        {
            // Target rotated 30° around source forward — its Up tilts toward
            // source's Right. RollRadians is signed (-Atan2(rightDot, upDot))
            // so the chevron sweeps in "fly to needle" direction; a +30°
            // physical rotation reads as a negative roll value.
            const double thetaDeg = 30.0;
            double t = thetaDeg * (Math.PI / 180.0);

            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1),
                up: new Vector3D(Math.Sin(t), Math.Cos(t), 0));

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            // Magnitude should match the input rotation; sign is a single
            // documented flip — pin both.
            Assert.That(Math.Abs(a.RollRadians), Is.EqualTo(t).Within(1e-6));
            Assert.That(a.RollRadians, Is.LessThan(0.0));
        }

        [Test]
        public void Pitch_yaw_components_are_zero_when_axes_are_anti_parallel()
        {
            // nose-error = (-tgt.Forward) ⊥ src.Forward; when the forwards are
            // already anti-parallel that's the zero vector → both zero.
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 1, Sep),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(a.PitchComponent, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(a.YawComponent, Is.EqualTo(0.0).Within(1e-9));
        }

        // Regression guard for the "expecting me to turn the ship upside down"
        // report. Geometry is from a real designated-pair log of two connectors
        // the player had aligned and which connect in-game: axes[F.F=-0.976
        // U.U=-0.971 R.R=0.993] — idealised, the target frame = source rotated
        // 180° about the source's Right axis (forwards anti-parallel/dockable,
        // Ups anti-parallel, Rights parallel).
        //
        // SE connectors lock at any roll and a 180°-apart mount is an identical
        // connectable face, so this pose needs NO roll. The roll error is folded
        // by π, so it must read ~0 here (and never exceed a quarter-turn).
        [Test]
        public void Dockable_pose_with_antiparallel_ups_needs_no_roll()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            // 180° about source Right (X): forward (0,0,-1), up (0,-1,0).
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1), up: new Vector3D(0, -1, 0));

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            // Forwards anti-parallel — the connectors are mate-aligned and lock
            // in-game.
            Assert.That(a.AlignmentDeg, Is.LessThan(1e-6),
                "forwards anti-parallel: this pose docks in-game");
            // Folded roll: no flip demanded.
            Assert.That(a.RollRadians, Is.EqualTo(0.0).Within(1e-6),
                "180°-apart mount is connectable as-is: roll error must fold to ~0");
        }

        // The fold must never ask for more than a quarter-turn: a 135° physical
        // up-rotation (raw roll −135°) is equivalent to +45° for a roll-free
        // connector after folding by 180°.
        [Test]
        public void Roll_error_never_exceeds_a_quarter_turn()
        {
            const double rawDeg = 135.0;
            double t = rawDeg * (Math.PI / 180.0);

            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1),
                up: new Vector3D(Math.Sin(t), Math.Cos(t), 0));

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(Math.Abs(a.RollRadians), Is.LessThanOrEqualTo(Math.PI / 2.0 + 1e-9),
                "folded roll must never exceed 90°");
            // Raw roll −135° folds to the shortest equivalent: +45°.
            Assert.That(a.RollRadians * (180.0 / Math.PI), Is.EqualTo(45.0).Within(1e-6));
        }

        // ── Unified cross: same screen frame as the ring ────────────────────
        //
        // The notch must be a fly-to-needle in the SAME screen basis the ring
        // uses, so it can't disagree with the ring. screenUp=+Y, screenRight
        // =+X here. src forward +Z; the target forward is tilted so the
        // connector's nose must swing toward +screenUp.
        [Test]
        public void Pitch_notch_points_along_screen_up()
        {
            const double thetaDeg = 20.0;
            double t = thetaDeg * (Math.PI / 180.0);
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            // -tgt.Forward leans toward +Y ⇒ nose must pitch toward screen-up.
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, -Math.Sin(t), -Math.Cos(t)),
                up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(a.PitchComponent, Is.EqualTo(Math.Sin(t)).Within(1e-6),
                "nose-error along +screenUp ⇒ +pitch ⇒ notch above centre (renderer c - pitch)");
            Assert.That(a.YawComponent, Is.EqualTo(0.0).Within(1e-9));
        }

        // Symmetric: target forward tilted so the nose must swing toward
        // +screenRight ⇒ +yaw ⇒ notch right of centre (renderer c + yaw).
        [Test]
        public void Yaw_notch_points_along_screen_right()
        {
            const double thetaDeg = 20.0;
            double t = thetaDeg * (Math.PI / 180.0);
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(-Math.Sin(t), 0, -Math.Cos(t)),
                up: Vector3D.Up);

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(a.YawComponent, Is.EqualTo(Math.Sin(t)).Within(1e-6));
            Assert.That(a.PitchComponent, Is.EqualTo(0.0).Within(1e-9));
        }

        // The cross, like the ring, must not depend on the connector's
        // arbitrary build-roll: same forwards + same screen basis ⇒ identical
        // pitch/yaw regardless of how the source connector is rolled.
        [Test]
        public void Notch_is_independent_of_connector_build_roll()
        {
            double t = 25.0 * (Math.PI / 180.0);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, -Math.Sin(t), -Math.Cos(t)),
                up: Vector3D.Up);
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            var srcRolled = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(1, 0, 0)); // 90° roll

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);
            var b = DockingAlignment.Compute(srcRolled, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(b.PitchComponent, Is.EqualTo(a.PitchComponent).Within(1e-9));
            Assert.That(b.YawComponent, Is.EqualTo(a.YawComponent).Within(1e-9));
        }
    }
}
