using NUnit.Framework;
using VRageMath;

namespace Mal.DockingAid.Tests.Tests
{
    [TestFixture]
    public class DockingAidPaletteTests
    {
        // Saturation/value floors and ceilings the palette enforces on status
        // colours (Good/Warn/Critical). Mirrored here so drift in the
        // production constants flips the assertions.
        const float MinStatusSat = 0.55f;
        const float MaxStatusSat = 0.85f;
        const float MinStatusVal = 0.55f;
        const float MaxStatusVal = 0.95f;

        [Test]
        public void Default_passes_through_white_on_black()
        {
            var p = DockingAidPalette.Default;
            Assert.That(p.Foreground, Is.EqualTo(Color.White));
            Assert.That(p.Background, Is.EqualTo(Color.Black));
        }

        [Test]
        public void From_carries_supplied_foreground_and_background()
        {
            var fg = new Color(120, 200, 255);
            var bg = new Color(20, 25, 30);
            var p = DockingAidPalette.From(fg, bg);
            Assert.That(p.Foreground, Is.EqualTo(fg));
            Assert.That(p.Background, Is.EqualTo(bg));
        }

        [Test]
        public void Body_chrome_faint_are_progressively_darker_than_foreground()
        {
            var p = DockingAidPalette.From(Color.White, Color.Black);
            // Fade preserves hue; intensity is monotonic so just compare R.
            Assert.That(p.Body.R, Is.LessThan(p.Foreground.R));
            Assert.That(p.Chrome.R, Is.LessThan(p.Body.R));
            Assert.That(p.Faint.R, Is.LessThan(p.Chrome.R));
            Assert.That(p.Faint.R, Is.GreaterThan(0));
        }

        [Test]
        public void Status_colors_clamp_saturation_when_foreground_is_muted()
        {
            // A nearly-grey foreground would otherwise produce desaturated
            // status colours. The palette pulls them up to MinStatusSat so
            // red still reads as a warning.
            var fg = new Color(150, 152, 150);
            var p = DockingAidPalette.From(fg, Color.Black);

            AssertSatValInBand(p.Good);
            AssertSatValInBand(p.Warn);
            AssertSatValInBand(p.Critical);
        }

        [Test]
        public void Status_colors_clamp_saturation_when_foreground_is_oversaturated()
        {
            // Pure-saturation fg would otherwise yield a 1.0-saturation
            // traffic-light. The palette pulls it back to MaxStatusSat.
            var fg = new Color(255, 0, 0);
            var p = DockingAidPalette.From(fg, Color.Black);

            AssertSatValInBand(p.Good);
            AssertSatValInBand(p.Warn);
            AssertSatValInBand(p.Critical);
        }

        [Test]
        public void Status_colors_have_distinct_dominant_channels()
        {
            var p = DockingAidPalette.From(Color.White, Color.Black);
            // Good ≈ green hue ⇒ G dominant
            Assert.That(p.Good.G, Is.GreaterThan(p.Good.R));
            Assert.That(p.Good.G, Is.GreaterThan(p.Good.B));
            // Warn ≈ yellow ⇒ R and G both > B
            Assert.That(p.Warn.R, Is.GreaterThan(p.Warn.B));
            Assert.That(p.Warn.G, Is.GreaterThan(p.Warn.B));
            // Critical ≈ red ⇒ R dominant
            Assert.That(p.Critical.R, Is.GreaterThan(p.Critical.G));
            Assert.That(p.Critical.R, Is.GreaterThan(p.Critical.B));
        }

        [Test]
        public void Accent_for_grey_foreground_falls_back_to_blue_family()
        {
            var p = DockingAidPalette.From(Color.Gray, Color.Black);
            // Grey is below the saturation threshold so accent uses the
            // hardcoded blue family — B should clearly dominate.
            Assert.That(p.Accent.B, Is.GreaterThan(p.Accent.R));
            Assert.That(p.Accent.B, Is.GreaterThan(p.Accent.G));
        }

        [Test]
        public void Accent_for_saturated_foreground_rotates_hue()
        {
            // Pure red ⇒ accent should be roughly cyan (the 180° opposite),
            // i.e. R is much smaller than G or B.
            var p = DockingAidPalette.From(new Color(255, 0, 0), Color.Black);
            Assert.That(p.Accent.R, Is.LessThan(p.Accent.G));
            Assert.That(p.Accent.R, Is.LessThan(p.Accent.B));
        }

        [Test]
        public void Fade_with_zero_brightness_collapses_to_black()
        {
            var faded = DockingAidPalette.Fade(Color.White, 0);
            Assert.That(faded.R, Is.EqualTo(0));
            Assert.That(faded.G, Is.EqualTo(0));
            Assert.That(faded.B, Is.EqualTo(0));
        }

        [Test]
        public void Fade_with_full_brightness_is_lossless_for_white()
        {
            var faded = DockingAidPalette.Fade(Color.White, 255);
            Assert.That(faded.R, Is.EqualTo(255));
            Assert.That(faded.G, Is.EqualTo(255));
            Assert.That(faded.B, Is.EqualTo(255));
        }

        static void AssertSatValInBand(Color c)
        {
            // ε of 2/255 covers the worst-case byte-channel quantization error
            // of the HSV → RGB → Color roundtrip the palette goes through; the
            // clamp itself is still pinning to the band, the roundtrip just
            // bumps the readback by < 1 byte.
            const float Eps = 2f / 255f;
            var hsv = c.ColorToHSV();
            Assert.That(hsv.Y, Is.InRange(MinStatusSat - Eps, MaxStatusSat + Eps),
                "saturation outside [MinStatusSat, MaxStatusSat] band");
            Assert.That(hsv.Z, Is.InRange(MinStatusVal - Eps, MaxStatusVal + Eps),
                "value outside [MinStatusVal, MaxStatusVal] band");
        }
    }
}
