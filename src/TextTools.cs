using System;
using System.Text.RegularExpressions;

namespace WTTG2.SafeSkyCopy
{
    public static class TextTools
    {
        // Unity legacy Text rich-text tags only. Keep arbitrary angle-bracket text.
        private static readonly Regex RichTags = new Regex(@"</?(?:b|i)>|</?(?:color|size|material)(?:=[^<>]*)?>|<quad\s+[^<>]*/?>", RegexOptions.IgnoreCase);
        // Despite the method name GenerateRandomHexCode, WTTG2 uses five ALPHANUMERIC pairs.
        // Accept conventional six-pair addresses too. Never match a fragment of a longer chain.
        private static readonly Regex Mac = new Regex(@"(?<![0-9a-zA-Z:])(?:[0-9a-zA-Z]{2}:){4,5}[0-9a-zA-Z]{2}(?![0-9a-zA-Z:])");

        public static string Plain(string text)
        {
            return Plain(text, true);
        }

        public static string Plain(string text, bool richText)
        {
            string value = text ?? string.Empty;
            if (richText) value = RichTags.Replace(value, string.Empty);
            return value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", string.Empty);
        }

        public static string[] Bssids(string text)
        {
            MatchCollection matches = Mac.Matches(text ?? string.Empty);
            string[] result = new string[matches.Count];
            for (int i = 0; i < result.Length; i++) result[i] = matches[i].Value;
            return result;
        }
    }
}
