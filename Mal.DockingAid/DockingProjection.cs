using System;
using VRageMath;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;

namespace Mal.DockingAid
{
    /// <summary>
    ///     Result of projecting the target connector's mating ring into the
    ///     source connector's screen frame. Major/minor diameters describe an
    ///     ellipse (foreshortened circle); rotation is the major-axis angle.
    ///     <see cref="ScreenCenter"/> may sit far outside the viewport when
    ///     the source has gone behind the target — the renderer is expected
    ///     to test that and switch to an off-screen arrow.
    /// </summary>
    public struct ProjectedRing
    {
        public Vector2 ScreenCenter;
        public float MajorDiameterPx;
        public float MinorDiameterPx;
        public float RotationRadians;
    }

    /// <summary>
    ///     Perspective projection of the target ring for the on-glass
    ///     indicator. The approach (depth) axis is the SOURCE CONNECTOR's
    ///     forward — the mating axis, which is roll-independent and physically
    ///     meaningful. The on-glass right/up come from the VIEWER's frame
    ///     (the host text-surface block, passed in as screenRight/screenUp),
    ///     NOT the connector's own basis: a connector's roll about its forward
    ///     axis is an arbitrary build choice the pilot can't see, so letting
    ///     it rotate the indicator made the display flip on ships whose
    ///     connector was built rolled. Sharing the host frame with the
    ///     pitch/yaw cross keeps every element in one consistent screen frame.
    /// </summary>
    public static class DockingProjection
    {
        // Mating-face radius as a fraction of grid size. SE connectors:
        // large grid (2.5 m blocks) → ~0.5 m radius; small grid (0.5 m
        // blocks) → ~0.1 m radius. 0.2 × gridSize matches both well enough
        // for an indicator sprite.
        public const double RingRadiusFraction = 0.20;

        // Virtual focal length and camera setback, both expressed as multiples
        // of the reticle / target ring radius. With Focal = Setback, the
        // projected target ring exactly matches the reticle at depth = 0
        // (perfect docking), regardless of grid size — and the projection
        // never blows up because depthEff stays > 0 for any non-pathological
        // depth.
        public const float FocalMultiplier = 2.0f;
        public const float SetbackMultiplier = 2.0f;

        // Don't try to project points closer than this; avoids divide-by-zero
        // and the visual chaos of points wrapping behind the camera.
        public const double NearClipMetres = 0.05;

        /// <summary>
        ///     Projects target's mating ring into screen space. <paramref name="screenCenter"/>
        ///     is where (0, 0) of the connector frame projects to — usually the
        ///     reticle centre. <paramref name="reticleRadius"/> is the source
        ///     reticle's pixel radius; sets the perspective focal length and
        ///     thus how fast the target ring grows as the source approaches.
        ///     <paramref name="offScreenFallbackPx"/> is how far to push the
        ///     screen position out along the lateral direction when the source
        ///     has passed the target (depth + setback below
        ///     <see cref="NearClipMetres"/>); pass screen size diagonal so the
        ///     viewport-test always treats it as off-screen.
        /// </summary>
        public static ProjectedRing Project(IMyShipConnector src, IMyShipConnector tgt,
            Vector3D screenRight, Vector3D screenUp,
            Vector2 screenCenter, float reticleRadius, float offScreenFallbackPx)
        {
            var srcMtx = src.WorldMatrix;
            var tgtMtx = tgt.WorldMatrix;

            var srcMate = ConnectorGeometry.MatingPosition(src);
            var tgtMate = ConnectorGeometry.MatingPosition(tgt);

            var rel = tgtMate - srcMate;
            double depth = Vector3D.Dot(rel, srcMtx.Forward);

            double ringRadiusM = tgt.CubeGrid.GridSize * RingRadiusFraction;
            float focal = reticleRadius * FocalMultiplier;

            // Setback model: clamp depth at 0 (the MatingPosition approximation
            // overshoots the real face, so depth goes negative when physically
            // docked) and add a virtual setback so depthEff is always positive
            // and the ring caps at reticle diameter.
            double setback = ringRadiusM * SetbackMultiplier;
            double depthEff = Math.Max(depth, 0.0) + setback;

            // Lateral placement in the VIEWER (screen) frame, so the ring
            // sits where the pilot sees the target regardless of how the
            // connector block was rolled when built. Y inverted because pixel
            // Y grows down but screenUp grows up.
            double sx = Vector3D.Dot(rel, screenRight);
            double sy = Vector3D.Dot(rel, screenUp);

            ProjectedRing result;

            if (depth + setback > NearClipMetres)
            {
                result.ScreenCenter = screenCenter + new Vector2(
                    (float)(sx / depthEff * focal),
                    -(float)(sy / depthEff * focal));
            }
            else
            {
                // Behind-camera fallback: emit a screen-space direction so the
                // renderer's viewport test pushes us into the off-screen-arrow
                // path. Magnitude doesn't matter beyond "off the visible area".
                var lateralDir = new Vector2((float)sx, -(float)sy);
                if (lateralDir.LengthSquared() < 1e-6f)
                    lateralDir = new Vector2(0f, 1f);
                else
                    lateralDir = Vector2.Normalize(lateralDir);
                result.ScreenCenter = screenCenter + lateralDir * offScreenFallbackPx;
            }

            // Apparent diameter (face-on) in pixels; depthEff caps at setback
            // so this caps at reticle diameter.
            double diameterPx = (ringRadiusM * 2.0 / depthEff) * focal;
            result.MajorDiameterPx = (float)diameterPx;

            // Minor axis foreshortening = cos(tilt). Mating requires
            // source.Forward = -tgt.Forward, so cos(tilt) = -dot. ConnectorLogic
            // only accepts candidates within 45° of anti-parallel, so cosTilt
            // is always positive — no abs needed.
            double cosTilt = -Vector3D.Dot(tgtMtx.Forward, srcMtx.Forward);
            if (cosTilt > 1.0) cosTilt = 1.0;
            result.MinorDiameterPx = (float)(diameterPx * cosTilt);

            // Major-axis direction: project the 3D tilt axis onto the VIEWER
            // frame (same frame as the ring's screen offset, so the ellipse's
            // tilt reads consistently). When tilt is zero the vector is zero
            // and atan2(0,0) returns 0 — harmless, the ellipse is a circle.
            var tiltAxis3D = Vector3D.Cross(tgtMtx.Forward, srcMtx.Forward);
            double axRight = Vector3D.Dot(tiltAxis3D, screenRight);
            double axUp = Vector3D.Dot(tiltAxis3D, screenUp);
            result.RotationRadians = (float)Math.Atan2(-axUp, axRight);

            return result;
        }

        // Builds the on-glass basis for the connector-bore view. The camera
        // looks along the SOURCE connector's forward (the mating axis); the
        // image's "up" is the SHIP's up (the controlling cockpit / reference),
        // projected into the plane perpendicular to that forward. Result:
        // independent of the connector's arbitrary build-roll AND of where the
        // display is mounted — only the pilot's own up enters the picture.
        // Caller resolves shipUp/shipForward (controlled controller, else a
        // configured reference, else fallbacks) and feeds the output straight
        // into <see cref="Project"/>.
        //
        // shipForward is a secondary reference, used only when shipUp is
        // (near-)parallel to the connector forward — i.e. docking straight
        // along the ship's own up axis, where shipUp has no projection.
        public static void ScreenBasis(Vector3D connectorForward, Vector3D shipUp,
            Vector3D shipForward, out Vector3D screenRight, out Vector3D screenUp)
        {
            var f = SafeNormalize(connectorForward, new Vector3D(0, 0, 1));

            var u = shipUp - Vector3D.Dot(shipUp, f) * f;
            if (u.LengthSquared() < 1e-9)
            {
                // shipUp ∥ connector forward — fall back to ship forward.
                u = shipForward - Vector3D.Dot(shipForward, f) * f;
                if (u.LengthSquared() < 1e-9)
                {
                    // Both refs ∥ f (pathological): any stable perpendicular.
                    var seed = Math.Abs(f.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
                    u = seed - Vector3D.Dot(seed, f) * f;
                }
            }
            screenUp = Vector3D.Normalize(u);
            // Observer behind the source connector looking ALONG +f toward the
            // target, with screenUp as up: a NON-mirrored view ⇒ right =
            // f × up (the cross(up, f) order is the mirror image — that was
            // the long-standing horizontal bug, confirmed in-game).
            screenRight = Vector3D.Normalize(Vector3D.Cross(f, screenUp));
        }

        static Vector3D SafeNormalize(Vector3D v, Vector3D fallback)
        {
            return v.LengthSquared() < 1e-12 ? fallback : Vector3D.Normalize(v);
        }
    }
}
