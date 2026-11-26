// SPDX-License-Identifier: GPL-3.0-only
using UnityEngine;

namespace DreamForge.World
{
    /// <summary>
    /// Debug colours for the untextured level. Deliberately NOT the game palette.
    /// Wall types: 16 hues x 4 tones. Within each block of 16 types the hue steps by 7/16 of the
    /// wheel, so consecutive types look different; the four blocks differ in saturation and brightness.
    /// </summary>
    public static class DebugPalette
    {
        public static readonly Color Floor = new Color(0.26f, 0.29f, 0.36f);        // slate blue-grey
        public static readonly Color Ceiling = new Color(0.58f, 0.52f, 0.64f);      // muted lavender
        public static readonly Color SkyCeiling = new Color(0.50f, 0.50f, 0.50f);   // plain grey: ceiling is sky (-1)
        public static readonly Color MapEdge = new Color(0.12f, 0.12f, 0.12f);      // outside the grid
        public static readonly Color Background = new Color(0.02f, 0.02f, 0.03f);

        /// <summary>Faces whose normal points along X (east/west) are drawn this much darker.</summary>
        public const float EastWestShade = 0.78f;

        /// <summary>Colour for wall type 1-63. Type 0 is open and should never be drawn (magenta).</summary>
        public static Color WallType(int type)
        {
            if (type <= 0 || type > 63) return Color.magenta;
            float hue = (type * 7 % 16) / 16f;
            int tone = type / 16;
            return Color.HSVToRGB(hue, ToneSaturation[tone], ToneValue[tone]);
        }

        static readonly float[] ToneSaturation = { 0.80f, 0.45f, 0.85f, 0.40f };
        static readonly float[] ToneValue = { 0.95f, 0.95f, 0.65f, 0.70f };

        /// <summary>Vanishing walls (M: cell byte 1, bit 0x08): their wall colour, washed towards white.</summary>
        public static Color Vanishing(Color c) => Color.Lerp(c, Color.white, 0.45f);

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
