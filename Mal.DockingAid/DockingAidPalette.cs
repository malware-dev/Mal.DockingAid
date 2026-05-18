using System;
using VRageMath;

namespace Mal.DockingAid
{
    /// <summary>
    ///     Colors for the docking-aid LCD app, all derived from the surface's
    ///     ScriptForegroundColor / ScriptBackgroundColor. Mirrors the approach
    ///     used by Mal.AutoPilot's MenuPalette so different LCDs feel consistent
    ///     when re-themed by the player.
    /// </summary>
    public struct DockingAidPalette
    {
        public Color Background;
        public Color Foreground;     // primary text, active indicators
        public Color Body;           // ~60% fg — secondary text, target name
        public Color Chrome;         // ~30% fg — rails, reticle ring, static roll notch
        public Color Faint;          // ~15% fg — "no target" placeholder
        public Color Accent;         // hue-rotated fg — READY / LOCKED highlight
        public Color Good;           // alignment within tight tolerance (green-hue)
        public Color Warn;           // alignment within loose tolerance (yellow-hue)
        public Color Critical;       // alignment outside tolerance (red-hue)

        // Status-color saturation / value bands. Floor keeps colors legible when
        // fg is muted (otherwise red wouldn't read as a warning); ceiling keeps
        // them tasteful when fg is fully saturated (otherwise pure traffic-light
        // primaries clash with the rest of the palette).
        const float MinStatusSat = 0.55f;
        const float MaxStatusSat = 0.85f;
        const float MinStatusVal = 0.55f;
        const float MaxStatusVal = 0.95f;

        // Semantic hues, expressed as 0..1 fractions of the colour wheel.
        const float HueRed = 0f;
        const float HueYellow = 60f / 360f;
        const float HueGreen = 120f / 360f;

        public static readonly DockingAidPalette Default = From(Color.White, Color.Black);

        public static DockingAidPalette From(Color foreground, Color background)
        {
            return new DockingAidPalette
            {
                Background = background,
                Foreground = foreground,
                Body = Fade(foreground, 150),
                Chrome = Fade(foreground, 80),
                Faint = Fade(foreground, 40),
                Accent = ComputeAccent(foreground),
                Good = StatusColor(HueGreen, foreground),
                Warn = StatusColor(HueYellow, foreground),
                Critical = StatusColor(HueRed, foreground),
            };
        }

        // Lock the hue to the semantic colour, but borrow the foreground's
        // saturation and value (clamped to a band). Result: status colours
        // feel like siblings of the user's chosen palette — never duller
        // than the floor, never gaudier than the ceiling.
        static Color StatusColor(float hue, Color foreground)
        {
            var hsv = foreground.ColorToHSV();
            float sat = MathHelper.Clamp(hsv.Y, MinStatusSat, MaxStatusSat);
            float val = MathHelper.Clamp(hsv.Z, MinStatusVal, MaxStatusVal);
            var rgb = new Vector3(hue, sat, val).HSVtoColor();
            return new Color(rgb.R, rgb.G, rgb.B);
        }

        public static Color Fade(Color c, byte brightness)
        {
            float f = brightness / 255f;
            return new Color((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
        }

        // Same accent recipe as MenuPalette: rotate hue 180°, ensure adequate
        // saturation and value separation from the base. Gray foregrounds
        // fall back to a default blue.
        static Color ComputeAccent(Color baseColor)
        {
            const float GraySatThreshold = 0.03f;
            const float MinSaturation = 0.45f;
            const float MinValueDistance = 0.45f;

            var hsv = baseColor.ColorToHSV();
            Vector3 accent;
            if (hsv.Y <= GraySatThreshold)
            {
                accent = new Color(0, 180, 255).ColorToHSV();
                accent.Z = hsv.Z;
            }
            else
            {
                accent = hsv;
                accent.X += 0.5f;
                if (accent.X >= 1f) accent.X -= 1f;
            }

            if (accent.Y < MinSaturation)
                accent.Y = MinSaturation;

            if (Math.Abs(accent.Z - hsv.Z) < MinValueDistance)
            {
                accent.Z = hsv.Z < 0.5f
                    ? MathHelper.Clamp(hsv.Z + MinValueDistance, 0f, 1f)
                    : MathHelper.Clamp(hsv.Z - MinValueDistance, 0f, 1f);
            }

            var rgb = accent.HSVtoColor();
            return new Color(rgb.R, rgb.G, rgb.B);
        }
    }
}
