using System;
using VRageMath;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;

namespace Mal.DockingAid
{
    /// <summary>
    ///     Frame-mixed alignment readouts for the LCD app. Lateral and notch
    ///     components are projected into the LCD's frame so they line up with
    ///     the player's pitch/yaw inputs regardless of how the source connector
    ///     is mounted; range/roll/alignment are intrinsic.
    /// </summary>
    public struct AlignmentData
    {
        public double Range;
        public double LateralLength;
        public double LateralXLcd;       // metres along screen-right (unused by renderer; kept for tests)
        public double LateralYLcd;       // metres along screen-up   (unused by renderer; kept for tests)
        public double PitchComponent;    // notch displacement, screen-up axis, sin-like [-1, 1]
        public double YawComponent;      // notch displacement, screen-right axis, sin-like [-1, 1]
        public double AlignmentDeg;      // 0 = perfectly anti-parallel forwards
        public double RollRadians;       // signed roll error around source.Forward
    }

    /// <summary>
    ///     Pure docking math. Inputs: source/target connectors and the
    ///     viewer screen basis (screenRight, screenUp) — the SAME basis the
    ///     ring is projected in (see <see cref="DockingProjection.ScreenBasis"/>),
    ///     so the pitch/yaw notch and the target ring share one frame and
    ///     can't disagree. No SE-API calls beyond the connector world matrices.
    /// </summary>
    public static class DockingAlignment
    {
        // Alignment thresholds — all three (lateral / forwards / roll) must be
        // inside the band to qualify for that status colour.
        const double GoodLateralMetres = 0.3;
        const double GoodAlignDeg = 5.0;
        const double GoodRollDeg = 10.0;
        const double WarnLateralMetres = 1.5;
        const double WarnAlignDeg = 20.0;
        const double WarnRollDeg = 30.0;

        public static AlignmentData Compute(IMyShipConnector src, IMyShipConnector tgt,
            Vector3D screenRight, Vector3D screenUp)
        {
            var srcMate = ConnectorGeometry.MatingPosition(src);
            var tgtMate = ConnectorGeometry.MatingPosition(tgt);

            var delta = tgtMate - srcMate; // src -> tgt
            var range = delta.Length();

            var srcMtx = src.WorldMatrix;
            var tgtMtx = tgt.WorldMatrix;

            // Target's lateral position relative to the source, in the screen
            // frame. Not drawn by the renderer (the ring sprite carries this);
            // kept because tests pin it and it's a cheap, useful readout.
            var deltaAlongTgtFwd = Vector3D.Dot(delta, tgtMtx.Forward);
            var targetLateralFromSrc = delta - deltaAlongTgtFwd * tgtMtx.Forward;

            var lateralXLcd = Vector3D.Dot(targetLateralFromSrc, screenRight);
            var lateralYLcd = Vector3D.Dot(targetLateralFromSrc, screenUp);

            // Pitch / yaw notch: which way the connector's nose must swing to
            // mate. It must point at -target.Forward; the in-plane direction
            // from the current bore toward that goal (component of -tgt.Forward
            // perpendicular to src.Forward) is exactly "where to aim the nose".
            // Projected onto the SAME screen basis as the ring and fed through
            // the SAME renderer mapping (notchY = c - pitch, notchX = c + yaw),
            // so the notch is a fly-to-needle that agrees with the ring by
            // construction — no independent sign convention to drift.
            var srcFwd = srcMtx.Forward;
            var antiTarget = -tgtMtx.Forward;
            var noseError = antiTarget - Vector3D.Dot(antiTarget, srcFwd) * srcFwd;
            var pitchComponent = Vector3D.Dot(noseError, screenUp);
            var yawComponent = Vector3D.Dot(noseError, screenRight);

            // Forwards alignment: -1 dot is perfectly anti-parallel = mating-aligned.
            var fwdDot = Vector3D.Dot(srcMtx.Forward, tgtMtx.Forward);
            var fwdAngleDeg = Math.Acos(MathHelper.Clamp(fwdDot, -1.0, 1.0)) * (180.0 / Math.PI);
            var alignmentDeg = 180.0 - fwdAngleDeg;

            // Signed roll error: angle of target.Up relative to source.Up
            // around source.Forward. Sign flipped so the chevron sweeps in
            // the "fly to needle" direction.
            var tgtUpRight = Vector3D.Dot(tgtMtx.Up, srcMtx.Right);
            var tgtUpUp = Vector3D.Dot(tgtMtx.Up, srcMtx.Up);
            var rawRoll = -Math.Atan2(tgtUpRight, tgtUpUp);

            // SE connectors lock at ANY roll, and a connector mounted 180° apart
            // presents an identical, equally-connectable face — depending on how
            // each connector is built, a mate-aligned pair can sit Up-to-Up
            // (raw roll ~0) or Up-to-anti-Up (raw roll ~±180°). Treating the
            // latter as a real error makes the aid demand a pointless flip.
            // Fold the error by π so it's always the shortest turn to a
            // connectable orientation: never more than a quarter-turn, never
            // upside-down.
            var rollRadians = rawRoll - Math.PI * Math.Round(rawRoll / Math.PI);

            return new AlignmentData
            {
                Range = range,
                LateralLength = targetLateralFromSrc.Length(),
                LateralXLcd = lateralXLcd,
                LateralYLcd = lateralYLcd,
                PitchComponent = pitchComponent,
                YawComponent = yawComponent,
                AlignmentDeg = alignmentDeg,
                RollRadians = rollRadians,
            };
        }

        public static Color ColorFor(AlignmentData a, DockingAidPalette palette)
        {
            double absRollDeg = Math.Abs(a.RollRadians) * (180.0 / Math.PI);
            if (a.LateralLength <= GoodLateralMetres
                && a.AlignmentDeg <= GoodAlignDeg
                && absRollDeg <= GoodRollDeg)
                return palette.Good;
            if (a.LateralLength <= WarnLateralMetres
                && a.AlignmentDeg <= WarnAlignDeg
                && absRollDeg <= WarnRollDeg)
                return palette.Warn;
            return palette.Critical;
        }
    }

    /// <summary>
    ///     Stateful low-pass smoother for closure rate (m/s, positive ⇒
    ///     approaching). Resets when the target ID changes so we don't bleed
    ///     last-target velocity into a fresh lock.
    /// </summary>
    public class ClosureTracker
    {
        const float Alpha = 0.4f;

        long _lastTargetId;
        double _lastRange;
        int _lastRangeTick;
        float _smoothedMps;

        public void Reset()
        {
            _lastTargetId = 0;
            _smoothedMps = 0f;
        }

        public float Update(long targetId, double currentRange, int currentTick)
        {
            if (targetId != _lastTargetId)
            {
                _lastTargetId = targetId;
                _lastRange = currentRange;
                _lastRangeTick = currentTick;
                _smoothedMps = 0f;
                return 0f;
            }

            int dtTicks = currentTick - _lastRangeTick;
            if (dtTicks <= 0) return _smoothedMps;

            float dtSec = dtTicks / 60.0f;
            float instantaneous = (float)((_lastRange - currentRange) / dtSec);

            _smoothedMps = _smoothedMps * (1f - Alpha) + instantaneous * Alpha;

            _lastRange = currentRange;
            _lastRangeTick = currentTick;
            return _smoothedMps;
        }
    }
}
