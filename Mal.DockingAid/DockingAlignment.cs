using System;
using VRageMath;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;

namespace Mal.DockingAid
{
    /// <summary>
    ///     Alignment readouts in the PILOT INPUT frame. The three Input* fields
    ///     are axis-angle scalars: each says how much input on the matching
    ///     control stick will null the docking error. For a forward-mounted
    ///     connector InputPitch/Yaw/Roll collapse to the same bore-frame
    ///     pitch/yaw/roll values you'd expect; for non-axial mounts they
    ///     permute automatically so the chevron the pilot sees always maps to
    ///     the stick they need to push.
    /// </summary>
    public struct AlignmentData
    {
        public double Range;
        public double LateralLength;
        public double InputPitch;        // ship pitch input needed (radians; axis-angle)
        public double InputYaw;          // ship yaw input needed
        public double InputRoll;         // ship roll input needed
        public double MatingRoll;        // mating roll about bore, post-fold, for the status threshold
        public double AlignmentDeg;      // 0 = perfectly anti-parallel forwards
    }

    /// <summary>
    ///     Pure docking math. Inputs: source/target connectors and the pilot's
    ///     own (right, up, forward) axes — the same triple the ScreenBasis
    ///     consumes. The three Input* outputs are decomposed in this pilot
    ///     frame, so each maps to one stick on the controller regardless of
    ///     how the source connector is mounted.
    /// </summary>
    public static class DockingAlignment
    {
        // Alignment thresholds — all three (lateral / forwards / mating-roll)
        // must be inside the band to qualify for that status colour.
        const double GoodLateralMetres = 0.3;
        const double GoodAlignDeg = 5.0;
        const double GoodRollDeg = 10.0;
        const double WarnLateralMetres = 1.5;
        const double WarnAlignDeg = 20.0;
        const double WarnRollDeg = 30.0;

        public static AlignmentData Compute(IMyShipConnector src, IMyShipConnector tgt,
            Vector3D pilotRight, Vector3D pilotUp, Vector3D pilotForward)
        {
            var srcMate = ConnectorGeometry.MatingPosition(src);
            var tgtMate = ConnectorGeometry.MatingPosition(tgt);

            var delta = tgtMate - srcMate; // src -> tgt
            var range = delta.Length();

            // Mate axes, not WorldMatrix.Forward: the Structural Platform
            // Connector (and any modded equivalent) overrides ConnectDirection
            // and mates through a different face. tgtMateUp gives the
            // mating-roll calculation a real perpendicular reference even when
            // tgt.Up runs along the bore.
            var bore = ConnectorGeometry.MateAxis(src);
            var tgtAxis = ConnectorGeometry.MateAxis(tgt);
            var tgtMateUp = ConnectorGeometry.MateUp(tgt);

            // Target's lateral offset perpendicular to its mate axis - drives
            // the status thresholds.
            var deltaAlongTgt = Vector3D.Dot(delta, tgtAxis);
            var targetLateralFromSrc = delta - deltaAlongTgt * tgtAxis;

            // Forwards alignment: -1 dot = perfectly anti-parallel = mating-aligned.
            var fwdDot = Vector3D.Dot(bore, tgtAxis);
            var fwdAngleDeg = Math.Acos(MathHelper.Clamp(fwdDot, -1.0, 1.0)) * (180.0 / Math.PI);
            var alignmentDeg = 180.0 - fwdAngleDeg;

            // Screen basis (bore-perpendicular pilot frame) — used only to
            // measure mating roll. Shared with DockingProjection.Project so the
            // "zero roll" reference is the same axis the target ring sits on.
            Vector3D screenRight, screenUp;
            DockingProjection.ScreenBasis(bore, pilotRight, pilotUp, pilotForward,
                out screenRight, out screenUp);

            // Mating roll: angle of target's mate-up around the bore, folded
            // by pi/2 (4 cardinal mounts all count as docked). Cap +/-45deg.
            var tgtUpRight = Vector3D.Dot(tgtMateUp, screenRight);
            var tgtUpUp = Vector3D.Dot(tgtMateUp, screenUp);
            var rawRoll = Math.Atan2(tgtUpRight, tgtUpUp);
            const double Quarter = Math.PI / 2.0;
            var matingRoll = rawRoll - Quarter * Math.Round(rawRoll / Quarter);

            // Nose-error: where the bore needs to swing to point at the target
            // (component of -tgtAxis perpendicular to bore).
            var antiTarget = -tgtAxis;
            var noseError = antiTarget - Vector3D.Dot(antiTarget, bore) * bore;

            // Small-angle axis-angle representation of the required correction:
            //   R_err ≈ (bore × noseError) + matingRoll · (screenUp × screenRight)
            // The first term tilts the bore toward the target. The second
            // rotates the connector about the bore-aligned screen-frame third
            // axis to align target Up with screen up — `screenUp × screenRight`
            // equals +bore on every mount EXCEPT aft (where lateral-preserved
            // gives a left-handed screen frame), so `matingRoll · bore` would
            // get the rotation sign wrong on aft. Decomposed in pilot frame
            // with the stick-convention adjustments below, each component is
            // the input on the matching stick that nulls the error.
            var rollAxis = Vector3D.Cross(screenUp, screenRight);
            var rErr = Vector3D.Cross(bore, noseError) + matingRoll * rollAxis;

            // Pilot stick conventions:
            //   +pitch stick = nose up      = +rotation about +pilotRight
            //   +yaw stick   = nose right   = −rotation about +pilotUp
            //   +roll stick  = right wing dn = +rotation about +pilotForward
            // The yaw axis convention disagrees with the right-hand rule sign,
            // so we decompose against −pilotUp to keep "+input value = +stick
            // direction = chevron on the +side" consistent with the other two.
            var inputPitch = Vector3D.Dot(rErr, pilotRight);
            var inputYaw = -Vector3D.Dot(rErr, pilotUp);
            var inputRoll = Vector3D.Dot(rErr, pilotForward);

            return new AlignmentData
            {
                Range = range,
                LateralLength = targetLateralFromSrc.Length(),
                InputPitch = inputPitch,
                InputYaw = inputYaw,
                InputRoll = inputRoll,
                MatingRoll = matingRoll,
                AlignmentDeg = alignmentDeg,
            };
        }

        public static Color ColorFor(AlignmentData a, DockingAidPalette palette)
        {
            // Status uses geometric mating-roll, not InputRoll: InputRoll mixes
            // bore-tilt and mating-roll contributions, and bore tilt is already
            // covered by AlignmentDeg.
            double absRollDeg = Math.Abs(a.MatingRoll) * (180.0 / Math.PI);
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
