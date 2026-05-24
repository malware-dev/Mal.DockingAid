using System;
using Mal.DockingAid.Tests.TestUtilities;
using NUnit.Framework;
using VRageMath;

namespace Mal.DockingAid.Tests.Tests
{
    /// <summary>
    ///     Pins the input-centric chevron semantics: mating roll on a side
    ///     mount goes to the PITCH input axis, mating roll on an aft mount
    ///     goes to the ROLL input axis (with sign flipped vs forward), and the
    ///     same "rotate the cockpit about the bore" physical correction shrinks
    ///     the displayed error on every mount — even though it corresponds to
    ///     different sticks per mount.
    /// </summary>
    [TestFixture]
    public class RollSenseTests
    {
        // SE-standard pilot frame.
        static readonly Vector3D PilotFwd = new Vector3D(0, 0, -1);
        static readonly Vector3D PilotUp = new Vector3D(0, 1, 0);
        static readonly Vector3D PilotRight = new Vector3D(1, 0, 0);

        // Pequod: side connector pointing to ship's left (bore = −pilotRight).
        // Jackdaw: aft connector pointing backward (bore = −pilotFwd = +Z).
        static Vector3D PequodBore { get { return -PilotRight; } }
        static Vector3D JackdawBore { get { return -PilotFwd; } }

        // Rodrigues rotation of v about a unit axis by angle (right-handed).
        static Vector3D Rot(Vector3D v, Vector3D axis, double angle)
        {
            var k = Vector3D.Normalize(axis);
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return v * c
                 + Vector3D.Cross(k, v) * s
                 + k * (Vector3D.Dot(k, v) * (1.0 - c));
        }

        // Build a scenario: source connector pointed along bore, target
        // anti-parallel and N metres ahead, target's mating-up rotated by
        // `injectedRoll` about the bore from the "natural" screen up.
        static AlignmentData Setup(Vector3D bore, double injectedRoll,
            Vector3D pilotRight, Vector3D pilotUp, Vector3D pilotFwd)
        {
            var boreN = Vector3D.Normalize(bore);
            Vector3D screenRight, screenUp;
            DockingProjection.ScreenBasis(boreN, pilotRight, pilotUp, pilotFwd,
                out screenRight, out screenUp);

            // Arbitrary build-roll on the source (must not matter).
            var buildUp = Math.Abs(boreN.Z) < 0.9
                ? Vector3D.Normalize(Vector3D.Cross(boreN, new Vector3D(0, 0, 1)))
                : Vector3D.Normalize(Vector3D.Cross(boreN, new Vector3D(1, 0, 0)));
            var src = FakeConnector.At(Vector3D.Zero, boreN, buildUp);

            var tgtUp = Rot(screenUp, boreN, injectedRoll);
            var srcMate = ConnectorGeometry.MatingPosition(src);
            var tgt = FakeConnector.At(srcMate + boreN * 31.25, -boreN, tgtUp);

            return DockingAlignment.Compute(src, tgt, pilotRight, pilotUp, pilotFwd);
        }

        static double ErrorMagnitude(AlignmentData a)
        {
            return Math.Sqrt(a.InputPitch * a.InputPitch
                + a.InputYaw * a.InputYaw
                + a.InputRoll * a.InputRoll);
        }

        // Side mount: mating-roll error must go to the PITCH input axis
        // (rotating about the bore = rotating about pilot.right = ship pitch).
        // Roll input axis stays at 0.
        [Test]
        public void Side_mount_mating_roll_goes_to_pitch_input_not_roll_input()
        {
            foreach (var th in new[] { -0.4, -0.15, 0.15, 0.4 })
            {
                var a = Setup(PequodBore, th, PilotRight, PilotUp, PilotFwd);

                Assert.That(Math.Abs(a.InputRoll), Is.LessThan(1e-9),
                    "side mount: ship roll has no effect on mating roll; InputRoll = 0 (θ=" + th + ")");
                Assert.That(Math.Abs(a.InputPitch), Is.EqualTo(Math.Abs(th)).Within(1e-6),
                    "side mount: mating roll maps to pitch input magnitude (θ=" + th + ")");
            }
        }

        // Aft mount: mating-roll error goes to the ROLL input axis. Stick
        // direction matches what the pilot sees on the screen (matingRoll's
        // screen-frame Atan2 sign), regardless of bore direction — the roll
        // rotation axis is `screenUp × screenRight`, not bore, so aft and
        // forward both report the same chevron direction for a given visible
        // tilt. Injected θ rotates target.up about +bore (= −pilotForward on
        // aft), which puts target.up at screen-frame matingRoll = −θ, so the
        // resulting InputRoll = −θ.
        [Test]
        public void Aft_mount_mating_roll_goes_to_roll_input_screen_frame_sign()
        {
            foreach (var th in new[] { -0.4, -0.15, 0.15, 0.4 })
            {
                var a = Setup(JackdawBore, th, PilotRight, PilotUp, PilotFwd);

                Assert.That(Math.Abs(a.InputPitch), Is.LessThan(1e-9),
                    "aft mount: ship pitch has no effect on mating roll (θ=" + th + ")");
                Assert.That(a.InputRoll, Is.EqualTo(-th).Within(1e-6),
                    "aft mount: injection about +bore lands at matingRoll=−θ in "
                    + "screen frame, so InputRoll = −θ (θ=" + th + ")");
            }
        }

        // Closed-loop fly-to-needle: rotate the cockpit about the bore by the
        // physically-correct counter-rotation, then check that the TOTAL error
        // magnitude shrinks. Works on every mount because "rotate about bore"
        // is what nulls mating-roll regardless of which stick that maps to.
        [Test]
        public void Correcting_about_bore_shrinks_total_error_on_every_mount()
        {
            foreach (var m in new[]
            {
                new { Name = "Pequod 90° side", Bore = PequodBore },
                new { Name = "Jackdaw 180° aft", Bore = JackdawBore },
            })
            {
                foreach (var rho in new[] { -0.4, 0.4 })
                {
                    var bore = Vector3D.Normalize(m.Bore);

                    // Frac 0.0: no correction applied — full ρ error remains.
                    var a0 = Setup(m.Bore, rho, PilotRight, PilotUp, PilotFwd);

                    // Frac 0.5: cockpit + source rotated by ρ*0.5 about bore.
                    // Target stays fixed at world-roll ρ; the residual is ρ/2.
                    double dShip = rho * 0.5;
                    var pR = Rot(PilotRight, bore, dShip);
                    var pU = Rot(PilotUp, bore, dShip);
                    var pF = Rot(PilotFwd, bore, dShip);
                    var a1 = SetupRotatedSource(m.Bore, rho, dShip, pR, pU, pF);

                    double e0 = ErrorMagnitude(a0);
                    double e1 = ErrorMagnitude(a1);
                    TestContext.Out.WriteLine(m.Name + " ρ=" + rho.ToString("+0.0;-0.0")
                        + " |err| " + e0.ToString("F3") + " → " + e1.ToString("F3"));
                    Assert.That(e1, Is.LessThan(e0),
                        m.Name + ": applying the physically-correct rotation must "
                        + "shrink the total error (ρ=" + rho + ")");
                }
            }
        }

        // Same as Setup but the source connector and pilot frame have both been
        // rotated about the bore by `dShip` (simulating the pilot driving the
        // ship by dShip). Target is world-fixed at the original ρ injection.
        static AlignmentData SetupRotatedSource(Vector3D bore, double rho, double dShip,
            Vector3D pR, Vector3D pU, Vector3D pF)
        {
            var boreN = Vector3D.Normalize(bore);
            var buildUp = Rot(Math.Abs(boreN.Z) < 0.9
                ? Vector3D.Normalize(Vector3D.Cross(boreN, new Vector3D(0, 0, 1)))
                : Vector3D.Normalize(Vector3D.Cross(boreN, new Vector3D(1, 0, 0))),
                boreN, dShip);
            var src = FakeConnector.At(Vector3D.Zero, boreN, buildUp);
            var srcMate = ConnectorGeometry.MatingPosition(src);

            // Target up = original aligned-up rotated by world-fixed ρ.
            Vector3D sR0, sU0;
            DockingProjection.ScreenBasis(boreN, PilotRight, PilotUp, PilotFwd,
                out sR0, out sU0);
            var tgtUp = Rot(sU0, boreN, rho);
            var tgt = FakeConnector.At(srcMate + boreN * 31.25, -boreN, tgtUp);

            return DockingAlignment.Compute(src, tgt, pR, pU, pF);
        }

        // Both side and aft mounts must be monotonic in injected roll on
        // whichever input axis is the active one for that mount.
        [Test]
        public void Active_input_axis_is_monotonic_in_injected_roll()
        {
            // Pequod: active axis = InputPitch
            var p0 = Setup(PequodBore, 0.0, PilotRight, PilotUp, PilotFwd);
            var pS = Setup(PequodBore, 0.15, PilotRight, PilotUp, PilotFwd);
            var pB = Setup(PequodBore, 0.45, PilotRight, PilotUp, PilotFwd);
            Assert.That(Math.Abs(p0.InputPitch), Is.LessThan(1e-9), "Pequod zero ⇒ pitch 0");
            Assert.That(Math.Sign(pS.InputPitch), Is.EqualTo(Math.Sign(pB.InputPitch)));
            Assert.That(Math.Abs(pB.InputPitch), Is.GreaterThan(Math.Abs(pS.InputPitch)));

            // Jackdaw: active axis = InputRoll
            var j0 = Setup(JackdawBore, 0.0, PilotRight, PilotUp, PilotFwd);
            var jS = Setup(JackdawBore, 0.15, PilotRight, PilotUp, PilotFwd);
            var jB = Setup(JackdawBore, 0.45, PilotRight, PilotUp, PilotFwd);
            Assert.That(Math.Abs(j0.InputRoll), Is.LessThan(1e-9), "Jackdaw zero ⇒ roll 0");
            Assert.That(Math.Sign(jS.InputRoll), Is.EqualTo(Math.Sign(jB.InputRoll)));
            Assert.That(Math.Abs(jB.InputRoll), Is.GreaterThan(Math.Abs(jS.InputRoll)));
        }
    }
}
