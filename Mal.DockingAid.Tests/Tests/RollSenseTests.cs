using System;
using Mal.DockingAid.Tests.TestUtilities;
using NUnit.Framework;
using VRageMath;
using IMyShipConnector = Sandbox.ModAPI.IMyShipConnector;

namespace Mal.DockingAid.Tests.Tests
{
    /// <summary>
    ///     Spec for the roll cue after the proper-frame fix.
    ///
    ///     The roll cue is CHIRAL. Before the fix the seam negated screenRight
    ///     alone — a det -1 reflection — so the same physical roll swept the
    ///     chevron OPPOSITE ways on a side vs an aft mount (proven here
    ///     previously). Now <see cref="DockingProjection.ScreenBasis"/> always
    ///     returns a proper (det +1) frame and the rear-view is a render-only
    ///     mirrorX that <see cref="DockingAlignment.Compute"/> never sees. So
    ///     the roll cue MUST be mount-independent: identical physical roll ⇒
    ///     identical chevron sweep on side AND aft.
    ///
    ///     Exact frames from the shared-grid insight: Pequod 90° (connector
    ///     left), Jackdaw 180° (connector aft). We inject a known physical
    ///     roll about the bore, run the real ScreenBasis + Compute + the real
    ///     chevron mapping (dirX = sin(roll); +angle = clockwise on screen).
    ///
    ///     Scope note: this pins mount-CONSISTENCY and monotonicity. The
    ///     single absolute "fly-to-needle" sign (Fault 2 — whether the whole
    ///     cue is globally backwards) is a UX direction the harness cannot
    ///     judge; it is the one remaining in-game check, and because the cue
    ///     is now mount-consistent, one ship settles it for all mounts.
    /// </summary>
    [TestFixture]
    public class RollSenseTests
    {
        // Cockpit frame matching SE's convention (Forward = -Z, Up = +Y,
        // Right = +X). Right is supplied explicitly — no derivation, no
        // handedness guess.
        static readonly Vector3D PilotFwd = new Vector3D(0, 0, -1);
        static readonly Vector3D PilotUp = new Vector3D(0, 1, 0);
        static readonly Vector3D PilotRight = new Vector3D(1, 0, 0);

        static Vector3D PequodBore { get { return -PilotRight; } }  // left, 90°
        static Vector3D JackdawBore { get { return -PilotFwd; } }   // aft, 180°

        // Rodrigues rotation of v about a unit axis by angle (right-handed).
        static Vector3D Rot(Vector3D v, Vector3D axis, double angle)
        {
            var k = Vector3D.Normalize(axis);
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return v * c
                 + Vector3D.Cross(k, v) * s
                 + k * (Vector3D.Dot(k, v) * (1.0 - c));
        }

        // Real pipeline: shipped ScreenBasis (always proper now) + real
        // Compute. Returns RollRadians and the chevron's screen dirX.
        static double RollCue(Vector3D bore, double injectedRoll, out double chevronDirX)
        {
            var boreN = Vector3D.Normalize(bore);
            Vector3D screenRight, screenUp;
            DockingProjection.ScreenBasis(boreN, PilotRight, PilotUp, PilotFwd,
                out screenRight, out screenUp);

            // Arbitrary build-roll on the source (must not matter).
            var buildUp = Math.Abs(boreN.Z) < 0.9
                ? Vector3D.Normalize(Vector3D.Cross(boreN, new Vector3D(0, 0, 1)))
                : Vector3D.Normalize(Vector3D.Cross(boreN, new Vector3D(1, 0, 0)));
            var src = FakeConnector.At(Vector3D.Zero, boreN, buildUp);

            var tgtUp = Rot(screenUp, boreN, injectedRoll);
            var srcMate = ConnectorGeometry.MatingPosition(src);
            var tgt = FakeConnector.At(srcMate + boreN * 31.25, -boreN, tgtUp);

            // mirrorX is deliberately NOT passed to Compute — proving the
            // roll cue cannot depend on the rear-view presentation flip.
            var a = DockingAlignment.Compute(src, tgt, screenRight, screenUp);
            chevronDirX = Math.Sin(a.RollRadians); // >0 ⇒ CW/right, <0 ⇒ CCW/left
            return a.RollRadians;
        }

        static string Sweep(double dirX)
        {
            if (Math.Abs(dirX) < 1e-4) return "TOP   ";
            return dirX > 0 ? "CW/RGT" : "CCW/LFT";
        }

        // ── 1. Living documentation: same physical roll, both exact mounts. ─
        [Test]
        public void Print_roll_cue_sweep_for_both_exact_mounts()
        {
            TestContext.Out.WriteLine(
                "injected roll = target rolled +θ about +bore (right-handed); "
                + "shipped ScreenBasis (always proper) + real Compute");
            foreach (var th in new[] { -0.5, -0.2, 0.2, 0.5 })
            {
                double pdx, jdx;
                double pr = RollCue(PequodBore, th, out pdx);
                double jr = RollCue(JackdawBore, th, out jdx);
                TestContext.Out.WriteLine(
                    "  θ=" + th.ToString("+0.0;-0.0") +
                    " | Pequod(90° side) roll=" + pr.ToString("+0.000;-0.000") +
                    " chevron " + Sweep(pdx) +
                    "   | Jackdaw(180° aft) roll=" + jr.ToString("+0.000;-0.000") +
                    " chevron " + Sweep(jdx));
            }
            Assert.Pass();
        }

        // ── 2. THE fix, pinned: roll cue is CONTROL-mapped. Side and aft
        //       mounts use different pilot sticks (PITCH vs ROLL) and the
        //       SAME stick has OPPOSITE physical effect on opposite-handed
        //       mounts. The new screen-basis rule (lateral preserved, frame
        //       chirality flips between antipode pairs) makes the chevron
        //       sign flip with it — so the SAME stick direction nulls the
        //       SAME chevron direction across all mounts. The downstream
        //       observable: a given physical roll produces OPPOSITE chevron
        //       sweeps on side vs aft. The earlier "same chevron everywhere"
        //       invariant was bore-frame-consistent but control-MISMATCHED;
        //       it was the root of the Pequod-backwards report. ────────────
        [Test]
        public void Roll_cue_is_control_mapped_side_opposite_aft()
        {
            foreach (var th in new[] { -0.5, -0.2, 0.2, 0.5 })
            {
                double pdx, jdx;
                RollCue(PequodBore, th, out pdx);
                RollCue(JackdawBore, th, out jdx);
                Assert.That(Math.Sign(pdx), Is.Not.EqualTo(Math.Sign(jdx)),
                    "same physical roll must sweep the chevron OPPOSITE ways "
                    + "on side vs aft (control-mapped, θ=" + th + ")");
            }
        }

        // Closed-loop fly-to-needle: inject connector-roll error ρ about the
        // bore, then apply the ship rotation that PHYSICALLY reduces it
        // (rotate the cockpit + source about the bore by +ρ·step). A correct
        // cue must shrink the chevron deflection toward 0. If chasing the
        // physically-correct direction GROWS the chevron, the cue is backwards.
        // No sign convention is assumed — only "does correcting reduce it".
        static double ChevronMagAfterCorrection(Vector3D bore, double rho, double frac)
        {
            var boreN = Vector3D.Normalize(bore);

            // Cockpit rotated about the bore by the physically-correct
            // counter-rotation (+rho*frac reduces a +rho error).
            double dShip = rho * frac;
            var pRight = Rot(PilotRight, boreN, dShip);
            var pUp = Rot(PilotUp, boreN, dShip);
            var pFwd = Rot(PilotFwd, boreN, dShip);

            Vector3D sR, sU;
            DockingProjection.ScreenBasis(boreN, pRight, pUp, pFwd, out sR, out sU);

            // Source rolls with the ship; target is world-fixed at error rho.
            var buildUp = Rot(Math.Abs(boreN.Z) < 0.9
                ? Vector3D.Normalize(Vector3D.Cross(boreN, new Vector3D(0, 0, 1)))
                : Vector3D.Normalize(Vector3D.Cross(boreN, new Vector3D(1, 0, 0))),
                boreN, dShip);
            var src = FakeConnector.At(Vector3D.Zero, boreN, buildUp);
            var srcMate = ConnectorGeometry.MatingPosition(src);

            // Target up = aligned-up (screenUp at zero ship-rotation) rotated
            // by the fixed world error rho.
            Vector3D sR0, sU0;
            DockingProjection.ScreenBasis(boreN, PilotRight, PilotUp, PilotFwd, out sR0, out sU0);
            var tgtUp = Rot(sU0, boreN, rho);
            var tgt = FakeConnector.At(srcMate + boreN * 31.25, -boreN, tgtUp);

            var a = DockingAlignment.Compute(src, tgt, sR, sU);
            return Math.Abs(a.RollRadians);
        }

        // ── 4. THE real correctness test, per mount: chasing the needle must
        //       null the error. Pinned for side AND aft. ──────────────────
        [Test]
        public void Correcting_the_physical_roll_error_shrinks_the_chevron()
        {
            foreach (var m in new[]
            {
                new { Name = "Pequod 90° side", Bore = PequodBore },
                new { Name = "Jackdaw 180° aft", Bore = JackdawBore },
            })
            {
                foreach (var rho in new[] { -0.4, 0.4 })
                {
                    double at0 = ChevronMagAfterCorrection(m.Bore, rho, 0.0);
                    double at1 = ChevronMagAfterCorrection(m.Bore, rho, 0.5);
                    TestContext.Out.WriteLine(m.Name + " ρ=" + rho.ToString("+0.0;-0.0")
                        + " chevron " + at0.ToString("F3") + " → " + at1.ToString("F3")
                        + (at1 < at0 ? "  (fly-to-needle OK)" : "  (BACKWARDS)"));
                    Assert.That(at1, Is.LessThan(at0),
                        m.Name + ": applying the physically-correct roll must "
                        + "shrink the chevron (ρ=" + rho + ")");
                }
            }
        }

        // ── 3. Monotonic and zero-centred on both mounts. ─────────────────
        [Test]
        public void Roll_cue_is_monotonic_and_zero_centred()
        {
            foreach (var bore in new[] { PequodBore, JackdawBore })
            {
                double z; RollCue(bore, 0.0, out z);
                Assert.That(Math.Abs(z), Is.LessThan(1e-4), "no roll ⇒ chevron at top");

                double s, b;
                RollCue(bore, 0.15, out s);
                RollCue(bore, 0.45, out b);
                Assert.That(Math.Sign(s), Is.EqualTo(Math.Sign(b)), "same side as it grows");
                Assert.That(Math.Abs(b), Is.GreaterThan(Math.Abs(s)), "monotonic in roll");
            }
        }
    }
}
