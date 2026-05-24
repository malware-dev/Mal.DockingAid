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
            // source's Right (positive screenRight). Fly-to-needle: pilot
            // should roll RIGHT to bring their up onto target's up, so the
            // chevron must sit at angle > 0 (right of top). RollRadians is
            // therefore positive in this scenario.
            const double thetaDeg = 30.0;
            double t = thetaDeg * (Math.PI / 180.0);

            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1),
                up: new Vector3D(Math.Sin(t), Math.Cos(t), 0));

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(Math.Abs(a.RollRadians), Is.EqualTo(t).Within(1e-6));
            Assert.That(a.RollRadians, Is.GreaterThan(0.0),
                "target up tilted toward +screenRight ⇒ chevron right ⇒ +rollRadians");
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

        // SE connector faces are 4-fold symmetric and there's no canonical "up"
        // for a target in space, so the fold is by π/2: the cue always points
        // at the nearest of 4 cardinal orientations. Cap is ±45°, never more.
        [Test]
        public void Roll_error_never_exceeds_45_degrees()
        {
            const double rawDeg = 135.0;
            double t = rawDeg * (Math.PI / 180.0);

            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1),
                up: new Vector3D(Math.Sin(t), Math.Cos(t), 0));

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(Math.Abs(a.RollRadians), Is.LessThanOrEqualTo(Math.PI / 4.0 + 1e-9),
                "fold by π/2 ⇒ |rollRadians| ≤ 45°");
            // Raw +135° sits exactly between +90° and +180° cardinals; banker's
            // rounding picks +180° as the nearest, leaving residual −45°.
            Assert.That(a.RollRadians * (180.0 / Math.PI), Is.EqualTo(-45.0).Within(1e-6));
        }

        // (B) regression: a target connector built rolled 90° on its mount
        // (its Up axis perpendicular to the target ship's Up) used to fabricate
        // a phantom 90° roll demand, resting the chevron at 9 o'clock instead
        // of 12. With fold-by-π/2 the chevron sits at 0 regardless of the
        // build-roll: all 4 cardinal mounts read as docked.
        [Test]
        public void Target_connector_built_rolled_90deg_does_not_demand_roll()
        {
            foreach (var tgtUp in new[]
            {
                new Vector3D(0, 1, 0),    //   0° build-roll
                new Vector3D(1, 0, 0),    //  90° build-roll (target Up = +screenRight)
                new Vector3D(0, -1, 0),   // 180°
                new Vector3D(-1, 0, 0),   // 270°
            })
            {
                var src = FakeConnector.At(Vector3D.Zero,
                    forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
                var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                    forward: new Vector3D(0, 0, -1), up: tgtUp);

                var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

                Assert.That(a.RollRadians, Is.EqualTo(0.0).Within(1e-6),
                    "target tgtUp=" + tgtUp + " is a cardinal mount; should read as docked");
            }
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

        // Roll, too, must be measured in the screen frame — independent of the
        // connector's build-roll. Same target + same screen basis ⇒ identical
        // RollRadians no matter how the source connector is rolled.
        [Test]
        public void Roll_is_independent_of_connector_build_roll()
        {
            double t = 22.0 * (Math.PI / 180.0);
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1),
                up: new Vector3D(Math.Sin(t), Math.Cos(t), 0));
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            var srcRolled = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(1, 0, 0)); // 90° roll

            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);
            var b = DockingAlignment.Compute(srcRolled, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(b.RollRadians, Is.EqualTo(a.RollRadians).Within(1e-9));
        }

        // Regression for the "Jackdaw" rear-mounted connector: built rolled 90°
        // (up = +X) on a forward-+Z connector, but the connectors are mate-
        // aligned and the target's Up matches the pilot/screen Up. Connector-
        // basis roll fabricated +90°; screen-basis roll must read ~0.
        [Test]
        public void Rear_mounted_connector_does_not_fabricate_90deg_roll()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(1, 0, 0)); // built rolled 90°
            var tgt = FakeConnector.At(new Vector3D(0, 0, Sep),
                forward: new Vector3D(0, 0, -1), up: new Vector3D(0, 1, 0));

            // Screen frame = pilot frame (+X right / +Y up), NOT the connector.
            var a = DockingAlignment.Compute(src, tgt, Vector3D.Right, Vector3D.Up);

            Assert.That(a.RollRadians, Is.EqualTo(0.0).Within(1e-6),
                "rear-mounted connector must not demand a phantom 90° roll");
        }
    }
}
