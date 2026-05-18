using Mal.DockingAid.Tests.TestUtilities;
using NUnit.Framework;
using VRageMath;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;

namespace Mal.DockingAid.Tests.Tests
{
    /// <summary>
    ///     Pins <see cref="DockingProjection.Project"/> against simple
    ///     hand-set scenarios. The projection's main responsibilities:
    ///     positions the ring on screen, sizes it according to
    ///     focal/setback/depth, foreshortens to an ellipse on tilt, and
    ///     swaps to an off-screen fallback past the near-clip plane.
    /// </summary>
    [TestFixture]
    public class DockingProjectionTests
    {
        static readonly Vector2 ScreenCenter = new Vector2(256, 256);
        const float ReticleRadius = 90f;
        const float FallbackPx = 1000f;

        // Every scenario below builds the source connector with forward +Z,
        // up +Y, so its basis equals world axes. The projection now takes the
        // viewer (screen) frame explicitly; for these geometry-focused tests
        // that frame is world +X right / +Y up. This wrapper supplies it once
        // so each test stays about the geometry, not the frame plumbing.
        // Tests that exercise the frame itself call DockingProjection.Project
        // directly with a non-world basis.
        static ProjectedRing Project(IMyShipConnector src, IMyShipConnector tgt,
            Vector2 screenCenter, float reticleRadius, float fallbackPx)
        {
            return DockingProjection.Project(src, tgt,
                Vector3D.Right, Vector3D.Up, screenCenter, reticleRadius, fallbackPx);
        }

        [Test]
        public void Centered_target_at_perfect_dock_projects_to_screen_center()
        {
            // Source and target mating faces coincident: depth = 0 by the
            // setback model, ring should sit at screen centre.
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, 2.5), // mating faces meet
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var ring = Project(src, tgt, ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That((ring.ScreenCenter - ScreenCenter).Length(), Is.LessThan(0.5f));
        }

        [Test]
        public void At_perfect_dock_diameter_caps_at_reticle_diameter()
        {
            // Setback = ringRadius × 2; focal = reticle × 2. At depth = 0,
            // depthEff = setback, so projected diameter = 2 × reticleRadius.
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, 2.5),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var ring = Project(src, tgt, ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That(ring.MajorDiameterPx, Is.EqualTo(ReticleRadius * 2f).Within(0.5f));
        }

        [Test]
        public void Anti_parallel_mating_yields_circular_ring_minor_equals_major()
        {
            // No tilt → ellipse degenerates to a circle (cosTilt = 1).
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 0, 10),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var ring = Project(src, tgt, ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That(ring.MinorDiameterPx, Is.EqualTo(ring.MajorDiameterPx).Within(1e-3f));
        }

        [Test]
        public void Lateral_target_offset_moves_ring_off_center()
        {
            // Target shifted +1 m to source's right. Projected ring should
            // land right of the screen centre (positive X).
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(1, 0, 10),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var ring = Project(src, tgt, ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That(ring.ScreenCenter.X, Is.GreaterThan(ScreenCenter.X));
            Assert.That(ring.ScreenCenter.Y, Is.EqualTo(ScreenCenter.Y).Within(0.5f));
        }

        [Test]
        public void Vertical_target_offset_inverts_to_screen_y_negative()
        {
            // Target shifted +1 m up (along source +Y). Pixel Y grows down,
            // so the ring should land *above* the screen centre (smaller Y).
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 1, 10),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var ring = Project(src, tgt, ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That(ring.ScreenCenter.Y, Is.LessThan(ScreenCenter.Y));
            Assert.That(ring.ScreenCenter.X, Is.EqualTo(ScreenCenter.X).Within(0.5f));
        }

        [Test]
        public void Far_target_yields_smaller_ring()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var near = FakeConnector.At(new Vector3D(0, 0, 5),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);
            var far = FakeConnector.At(new Vector3D(0, 0, 50),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var rNear = Project(src, near, ScreenCenter, ReticleRadius, FallbackPx);
            var rFar = Project(src, far, ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That(rFar.MajorDiameterPx, Is.LessThan(rNear.MajorDiameterPx));
        }

        [Test]
        public void Tilt_foreshortens_minor_axis_below_major()
        {
            // Target rotated 30° around its Up axis: not anti-parallel any
            // more, so cosTilt < 1 ⇒ minor < major (foreshortening).
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            // Target forward tilted in the X-Z plane:
            // (sin30, 0, -cos30) ≈ (0.5, 0, -0.866). cosTilt = -dot ≈ 0.866.
            var tgt = FakeConnector.At(new Vector3D(0, 0, 10),
                forward: new Vector3D(0.5, 0, -0.8660254), up: Vector3D.Up);

            var ring = Project(src, tgt, ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That(ring.MinorDiameterPx, Is.LessThan(ring.MajorDiameterPx));
            // cosTilt 0.866 → minor/major ≈ 0.866.
            float ratio = ring.MinorDiameterPx / ring.MajorDiameterPx;
            Assert.That(ratio, Is.EqualTo(0.866f).Within(0.01f));
        }

        // Regression guard for "the display is essentially upside-down on the
        // cockpit ship". The SOURCE connector is built rolled 180° about its
        // forward axis (up = -Y) — a legitimate build orientation. The target
        // is up AND right of the source, and the viewer/screen frame is the
        // normal world +X right / +Y up. Because the ring is now laid out in
        // the viewer frame (not the connector's own basis), the connector's
        // build-roll must NOT move the ring: an up-and-right target stays
        // upper-right. Before the fix this landed lower-left (both axes
        // negated), exactly matching the player's "Lower-left" report.
        [Test]
        public void Connector_build_roll_does_not_flip_ring_uses_viewer_frame()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, -1, 0));
            var tgt = FakeConnector.At(new Vector3D(1, 1, 10),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var ring = Project(src, tgt, ScreenCenter, ReticleRadius, FallbackPx);

            // Up-and-right target ⇒ upper-right ring (X > centre, Y < centre),
            // independent of how the connector was rolled when built.
            Assert.That(ring.ScreenCenter.X, Is.GreaterThan(ScreenCenter.X),
                "rolled connector must not mirror X");
            Assert.That(ring.ScreenCenter.Y, Is.LessThan(ScreenCenter.Y),
                "rolled connector must not mirror Y");
        }

        // The contract of the new screen-basis parameters: the ring follows
        // the VIEWER frame. With an un-rolled connector but a screen frame
        // physically inverted (mounted upside-down: right = -X, up = -Y), an
        // up-and-right (world) target must read lower-left on that glass —
        // proving the projection honours the supplied frame rather than
        // ignoring it or hard-coding world axes.
        [Test]
        public void Inverted_screen_frame_inverts_ring_placement()
        {
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(1, 1, 10),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var ring = DockingProjection.Project(src, tgt,
                -Vector3D.Right, -Vector3D.Up,
                ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That(ring.ScreenCenter.X, Is.LessThan(ScreenCenter.X),
                "inverted screen right flips X");
            Assert.That(ring.ScreenCenter.Y, Is.GreaterThan(ScreenCenter.Y),
                "inverted screen up flips Y");
        }

        [Test]
        public void Behind_camera_target_falls_back_to_off_screen_position()
        {
            // Target placed behind source (negative Z) — depth + setback < 0
            // → fallback path pushes ScreenCenter far off the viewport.
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: Vector3D.Up);
            var tgt = FakeConnector.At(new Vector3D(0, 1, -50),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            var ring = Project(src, tgt, ScreenCenter, ReticleRadius, FallbackPx);

            // Fallback offset is offScreenFallbackPx along a unit lateral
            // direction — distance from screen centre should be ~FallbackPx.
            float offset = (ring.ScreenCenter - ScreenCenter).Length();
            Assert.That(offset, Is.EqualTo(FallbackPx).Within(1f));
        }

        // ── Ship-referenced screen basis (the corrected model) ──────────────
        //
        // Camera = source connector forward; "up" = the SHIP's up projected ⊥
        // to that forward. These pin the properties the broken designs lacked.

        // THE core fix: the connector's build-roll must not move the ring.
        // Same forward, two different connector "up"s (rolled 90°); the ship
        // up-reference is identical → the ring must land in the same place.
        [Test]
        public void Connector_build_roll_does_not_move_ring_with_ship_up_reference()
        {
            var shipUp = new Vector3D(0, 1, 0);
            var shipFwd = new Vector3D(0, 0, 1);
            var srcA = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(0, 1, 0));
            var srcB = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(0, 0, 1), up: new Vector3D(1, 0, 0)); // rolled 90°
            var tgt = FakeConnector.At(new Vector3D(1, 0.5, 10),
                forward: new Vector3D(0, 0, -1), up: Vector3D.Up);

            Vector3D rA, uA, rB, uB;
            DockingProjection.ScreenBasis(srcA.WorldMatrix.Forward, shipUp, shipFwd, out rA, out uA);
            DockingProjection.ScreenBasis(srcB.WorldMatrix.Forward, shipUp, shipFwd, out rB, out uB);
            var ringA = DockingProjection.Project(srcA, tgt, rA, uA, ScreenCenter, ReticleRadius, FallbackPx);
            var ringB = DockingProjection.Project(srcB, tgt, rB, uB, ScreenCenter, ReticleRadius, FallbackPx);

            Assert.That((ringA.ScreenCenter - ringB.ScreenCenter).Length(), Is.LessThan(0.5f),
                "connector build-roll must not move the ring when up-ref is the ship");
        }

        // Non-mirrored convention, pinned to the two in-game probes the player
        // confirmed (TBM, side-mounted connector srcF≈+X, seatUp≈+Y,
        // seatFwd≈+Z):
        //   • target offset toward +seatUp   ⇒ ring ABOVE centre (vertical OK)
        //   • target offset toward +seatFwd  ⇒ ring to the RIGHT of centre
        //     (equivalently: flying the ship +forward, the target moves -fwd
        //     relative ⇒ ring goes LEFT — the player's required behaviour)
        // Connector build-roll must not affect any of this.
        [Test]
        public void Ship_referenced_view_is_not_mirrored_matches_in_game_probes()
        {
            var seatUp = new Vector3D(0, 1, 0);
            var seatFwd = new Vector3D(0, 0, 1);
            // Connector points along world +X (side-mounted, like the TBM),
            // and is rolled arbitrarily — must not matter.
            var src = FakeConnector.At(Vector3D.Zero,
                forward: new Vector3D(1, 0, 0), up: new Vector3D(0, 0, 1));

            Vector3D r, u;
            DockingProjection.ScreenBasis(src.WorldMatrix.Forward, seatUp, seatFwd, out r, out u);

            // Target ahead along the bore (+X), offset toward +seatUp (+Y).
            var tgtUp = FakeConnector.At(new Vector3D(10, 1, 0),
                forward: new Vector3D(-1, 0, 0), up: Vector3D.Up);
            var ringUp = DockingProjection.Project(src, tgtUp, r, u, ScreenCenter, ReticleRadius, FallbackPx);
            Assert.That(ringUp.ScreenCenter.Y, Is.LessThan(ScreenCenter.Y),
                "+seatUp offset ⇒ ring above centre (confirmed in-game)");

            // Target ahead along the bore (+X), offset toward +seatFwd (+Z).
            var tgtFwd = FakeConnector.At(new Vector3D(10, 0, 1),
                forward: new Vector3D(-1, 0, 0), up: Vector3D.Up);
            var ringFwd = DockingProjection.Project(src, tgtFwd, r, u, ScreenCenter, ReticleRadius, FallbackPx);
            Assert.That(ringFwd.ScreenCenter.X, Is.GreaterThan(ScreenCenter.X),
                "+seatFwd offset ⇒ ring right of centre (so ship-forward ⇒ ring left)");
            Assert.That(ringFwd.ScreenCenter.Y, Is.EqualTo(ScreenCenter.Y).Within(0.5f),
                "pure fore/aft offset stays on the horizontal axis");
        }

        // Real orientations from the captured cockpit log (TBM source / HOST
        // target, near-docked). The basis must be orthonormal, perpendicular
        // to the connector forward, and — since that cockpit's Up is already
        // ~⊥ the connector forward — screenUp must ≈ the cockpit Up.
        [Test]
        public void Logged_cockpit_orientation_yields_orthonormal_ship_up_basis()
        {
            var f = new Vector3D(0.98, -0.14, -0.14);       // src connector forward
            var shipUp = new Vector3D(-0.14, -0.99, 0.01);  // cockpit (pilot) Up
            var shipFwd = new Vector3D(-0.14, 0.01, -0.99);

            Vector3D r, u;
            DockingProjection.ScreenBasis(f, shipUp, shipFwd, out r, out u);

            var fn = Vector3D.Normalize(f);
            Assert.That(Vector3D.Dot(u, fn), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(Vector3D.Dot(r, fn), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(Vector3D.Dot(r, u), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(u.Length(), Is.EqualTo(1.0).Within(1e-6));
            Assert.That(r.Length(), Is.EqualTo(1.0).Within(1e-6));
            Assert.That(Vector3D.Dot(u, Vector3D.Normalize(shipUp)), Is.GreaterThan(0.99),
                "cockpit Up is ~⊥ forward here, so screenUp ≈ cockpit Up");
        }

        // Degenerate: connector points straight along the ship's up axis, so
        // shipUp has no usable projection — must fall back to shipForward and
        // still produce a valid orthonormal basis (no NaN, no zero vector).
        [Test]
        public void Ship_up_parallel_to_connector_forward_falls_back_cleanly()
        {
            var f = new Vector3D(0, 1, 0);
            var shipUp = new Vector3D(0, 1, 0);   // parallel to f — unusable
            var shipFwd = new Vector3D(0, 0, 1);

            Vector3D r, u;
            DockingProjection.ScreenBasis(f, shipUp, shipFwd, out r, out u);

            Assert.That(Vector3D.Dot(u, Vector3D.Normalize(f)), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(Vector3D.Dot(r, u), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(u.Length(), Is.EqualTo(1.0).Within(1e-6));
            Assert.That(r.Length(), Is.EqualTo(1.0).Within(1e-6));
        }
    }
}
