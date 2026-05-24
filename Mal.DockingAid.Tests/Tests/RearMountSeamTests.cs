using System;
using Mal.DockingAid.Tests.TestUtilities;
using NUnit.Framework;
using VRageMath;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;

namespace Mal.DockingAid.Tests.Tests
{
    /// <summary>
    ///     Pins the nav-camera convention the architect chose for rear-mount
    ///     bores: a target laterally offset in the pilot's +Right direction
    ///     shows on the screen's RIGHT, regardless of which way the bore
    ///     points. Same convention as a backup camera that happens to also
    ///     point in every other direction: lateral is preserved across all
    ///     mounts; only the depth axis (= the bore) flips.
    ///
    ///     Earlier the code applied a presentation-only mirrorX for the aft
    ///     hemisphere — that inverted AFT laterally vs. the windscreen, which
    ///     is NOT what we want. The rule now: screenRight = bore × screenUp,
    ///     no per-face flips. Validated in-game (Jackdaw aft mount): pilot
    ///     moves ship right to align with a target that's to their right.
    /// </summary>
    [TestFixture]
    public class RearMountSeamTests
    {
        static readonly Vector2 Center = new Vector2(256, 256);
        const float ReticleRadius = 90f;
        const float FallbackPx = 1000f;

        static readonly Vector3D PilotRight = new Vector3D(1, 0, 0);
        static readonly Vector3D PilotUp = new Vector3D(0, 1, 0);
        static readonly Vector3D PilotFwd = new Vector3D(0, 0, 1);

        static Vector3D AnyPerp(Vector3D v)
        {
            var seed = Math.Abs(v.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            return Vector3D.Normalize(seed - Vector3D.Dot(seed, v) * v);
        }

        // The user-confirmed nav-camera convention: target offset toward the
        // pilot's +Right shows on the screen's RIGHT. This is the case the
        // previous mirrorX patch got backwards on aft mounts.
        [Test]
        public void Aft_mount_target_to_pilot_right_shows_on_screen_right()
        {
            var bore = -PilotFwd;
            Vector3D r, u;
            DockingProjection.ScreenBasis(bore, PilotRight, PilotUp, PilotFwd, out r, out u);

            var src = FakeConnector.At(Vector3D.Zero, bore, AnyPerp(bore));
            var srcMate = ConnectorGeometry.MatingPosition(src);

            // Target 30 m down the bore, 2 m to pilot-right.
            var tgt = FakeConnector.At(srcMate + bore * 30.0 + PilotRight * 2.0 + bore * 1.25,
                -bore, AnyPerp(bore));
            var ring = DockingProjection.Project(src, tgt, r, u,
                Center, ReticleRadius, FallbackPx);

            Assert.That(ring.ScreenCenter.X, Is.GreaterThan(Center.X),
                "aft: target to pilot-right reads screen-RIGHT (nav-camera, lateral preserved)");
        }

        // Same convention applied to a forward mount, for symmetry: it's
        // literally a windscreen view.
        [Test]
        public void Forward_mount_matches_windscreen()
        {
            var bore = PilotFwd;
            Vector3D r, u;
            DockingProjection.ScreenBasis(bore, PilotRight, PilotUp, PilotFwd, out r, out u);

            var src = FakeConnector.At(Vector3D.Zero, bore, AnyPerp(bore));
            var srcMate = ConnectorGeometry.MatingPosition(src);

            var tgt = FakeConnector.At(srcMate + bore * 30.0 + PilotRight * 2.0 + bore * 1.25,
                -bore, AnyPerp(bore));
            var ring = DockingProjection.Project(src, tgt, r, u,
                Center, ReticleRadius, FallbackPx);

            Assert.That(ring.ScreenCenter.X, Is.GreaterThan(Center.X),
                "forward: target to pilot-right reads screen-RIGHT (windscreen)");
        }

        // Vertical stays natural on the aft mount: target offset toward
        // pilot-up reads screen-UP, not screen-DOWN.
        [Test]
        public void Aft_mount_target_above_pilot_shows_on_screen_top()
        {
            var bore = -PilotFwd;
            Vector3D r, u;
            DockingProjection.ScreenBasis(bore, PilotRight, PilotUp, PilotFwd, out r, out u);

            var src = FakeConnector.At(Vector3D.Zero, bore, AnyPerp(bore));
            var srcMate = ConnectorGeometry.MatingPosition(src);

            var tgt = FakeConnector.At(srcMate + bore * 30.0 + PilotUp * 2.0 + bore * 1.25,
                -bore, AnyPerp(bore));
            var ring = DockingProjection.Project(src, tgt, r, u,
                Center, ReticleRadius, FallbackPx);

            Assert.That(ring.ScreenCenter.Y, Is.LessThan(Center.Y),
                "aft: target above pilot reads screen-UP (pixel-Y grows down)");
        }
    }
}
