using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nullroute.Core
{
    /// <summary>
    /// Text helpers. Colour markup uses Unity rich-text tags (&lt;color=#hex&gt;); the headless
    /// simulator strips them with <see cref="Strip"/>.
    /// </summary>
    public static class Tx
    {
        public const string Green = "#39ff7a";
        public const string Dim = "#1f9a4c";
        public const string Cyan = "#4fd6ff";
        public const string Yellow = "#ffd24a";
        public const string Red = "#ff5252";
        public const string White = "#e6ffe9";
        public const string Magenta = "#ff6ad5";
        public const string Orange = "#ff9f40";

        public static string C(string hex, string s) { return "<color=" + hex + ">" + s + "</color>"; }
        public static string G(string s) { return C(Green, s); }
        public static string D(string s) { return C(Dim, s); }
        public static string Cy(string s) { return C(Cyan, s); }
        public static string Y(string s) { return C(Yellow, s); }
        public static string R(string s) { return C(Red, s); }
        public static string W(string s) { return C(White, s); }
        public static string M(string s) { return C(Magenta, s); }
        public static string O(string s) { return C(Orange, s); }

        static readonly Regex TagRx = new Regex("<color=#[0-9a-fA-F]{6}>|</color>", RegexOptions.Compiled);

        public static string Strip(string s) { return TagRx.Replace(s, ""); }

        /// <summary>Removes characters that would be interpreted as markup.</summary>
        public static string Safe(string s) { return s.Replace("<", "‹").Replace(">", "›"); }

        /// <summary>Pads to a visible width, ignoring colour tags.</summary>
        public static string Pad(string s, int width)
        {
            int vis = Strip(s).Length;
            return vis >= width ? s : s + new string(' ', width - vis);
        }

        public static string PadL(string s, int width)
        {
            int vis = Strip(s).Length;
            return vis >= width ? s : new string(' ', width - vis) + s;
        }

        public static string Bar(double frac, int width)
        {
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            int n = (int)Math.Round(frac * width);
            return new string('█', n) + new string('░', width - n);
        }

        public static string Money(long v) { return v.ToString("N0", CultureInfo.InvariantCulture) + "cr"; }

        public static string Num(long v) { return v.ToString("N0", CultureInfo.InvariantCulture); }

        public static string Clock(double secs)
        {
            if (secs < 0) secs = 0;
            int s = (int)Math.Ceiling(secs);
            if (s >= 3600) return (s / 3600) + ":" + ((s / 60) % 60).ToString("00") + ":" + (s % 60).ToString("00");
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        public static string Pct(double frac) { return ((int)Math.Floor(frac * 100)).ToString(CultureInfo.InvariantCulture) + "%"; }

        public static string F1(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        public static string F2(double v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }

        public static string Roman(int n)
        {
            if (n <= 0) return n.ToString(CultureInfo.InvariantCulture);
            int[] v = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] s = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++)
                while (n >= v[i]) { sb.Append(s[i]); n -= v[i]; }
            return sb.ToString();
        }
    }
}
