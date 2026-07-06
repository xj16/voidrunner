using UnityEngine;

namespace VoidRunner.Gameplay
{
    /// <summary>Parses the "#RRGGBB"/"#RRGGBBAA" hex strings used throughout content packs.</summary>
    public static class ColorUtil
    {
        public static Color Parse(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }

        public static Color Parse(string hex) => Parse(hex, Color.white);
    }
}
