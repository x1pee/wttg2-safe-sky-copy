using System;
using WTTG2.SafeSkyCopy;

internal static class TextToolsTests
{
    private static int assertions;
    private static void Equal(string expected, string actual, string name)
    {
        assertions++;
        if (expected != actual) throw new Exception(name + ": unexpected output");
    }
    private static void Addresses(string input, string[] expected, string name)
    {
        string[] actual = TextTools.Bssids(input);
        assertions++;
        if (actual.Length != expected.Length) throw new Exception(name + ": unexpected address count " + actual.Length);
        for (int i = 0; i < expected.Length; i++) Equal(expected[i], actual[i], name + " item " + i);
    }
    public static int Main()
    {
        try
        {
            Equal("", TextTools.Plain(null), "null transcript");
            Equal("a\nb\nc\nd", TextTools.Plain("a\r\nb\rc\nd"), "mixed line endings");
            Equal("password: pA55!#$%&*()[]=+/?", TextTools.Plain("password: pA55!#$%&*()[]=+/?"), "password punctuation preserved");
            Equal("Пароль: Ёжик_日本", TextTools.Plain("Пароль: Ёжик_日本"), "unicode preserved");
            Equal("<address> [value] a < 5 > 2", TextTools.Plain("<address> [value] a < 5 > 2"), "literal angle bracket content preserved");
            Equal("AB text x", TextTools.Plain("<b>A</b><I>B</I> <color=#00ff00>text</color> <size=14>x</size>"), "legacy rendered formatting removed");
            Equal("abc", TextTools.Plain("a\0b\0c"), "clipboard NUL removed");
            Equal("<col", TextTools.Plain("<col"), "partially typed markup preserved");
            Equal("<b>literal-password</b>", TextTools.Plain("<b>literal-password</b>", false), "literal tags preserved when rich text is disabled");
            Addresses("BSSID: 12:34:56:78:9A", new string[] { "12:34:56:78:9A" }, "actual game five-octet format");
            Addresses("(aa:BB:cc:DD:ee:FF)", new string[] { "aa:BB:cc:DD:ee:FF" }, "six octets and case preserved");
            Addresses("12:34:56:78:90 00:11:22:33:44:55 12:34:56:78:90", new string[] { "12:34:56:78:90", "00:11:22:33:44:55", "12:34:56:78:90" }, "multiple addresses order preserved");
            Addresses("00:11:22:33:44:55:66", new string[0], "no partial seven-octet chain");
            Addresses("00:11:22:33:44:55:66:77", new string[0], "no partial eight-octet chain");
            Addresses(":00:11:22:33:44:", new string[0], "no colon-delimited fragment");
            Addresses("A00:11:22:33:44B", new string[0], "no partial hex byte");
            Addresses("00:11:22:33 00:11:22:33:4? 00-11-22-33-44", new string[0], "short malformed and unrelated formats");
            Addresses("ZZ:XY:K8:W2:9G", new string[] { "ZZ:XY:K8:W2:9G" }, "actual game alphanumeric pairs including non-hex letters");
            Addresses(null, new string[0], "null addresses");
            Console.WriteLine("PASS: " + assertions + " assertions; five/six octets, no partial chains, transcript normalization, passwords and Unicode.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.Message);
            return 1;
        }
    }
}
