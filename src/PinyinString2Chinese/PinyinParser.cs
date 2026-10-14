using System.Text;

namespace PinyinString2Chinese;

public sealed record PinyinParse(string Keystrokes)
{
    public string Compact => Keystrokes;
}

public static class PinyinParser
{
    public const int MaxLetters = 120;

    public static PinyinParse? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var source = text.Trim();
        if (source.IndexOfAny(['\r', '\n', '\t']) >= 0)
            return null;

        var builder = new StringBuilder(source.Length);
        foreach (var ch in source)
        {
            if (ch is ' ' or '\u00A0')
                continue;

            if (!TryFold(ch, out var lower))
                return null;

            builder.Append(lower);
            if (builder.Length > MaxLetters)
                return null;
        }

        if (builder.Length == 0)
            return null;

        return new PinyinParse(builder.ToString());
    }

    public static string Explain(string? text, bool requireAlt = false)
    {
        _ = requireAlt;
        var parsed = Parse(text);
        if (parsed == null)
            return "不是纯英文字符串，框选后不会处理。";

        var shown = parsed.Keystrokes.Length > 40
            ? parsed.Keystrokes[..40] + "…"
            : parsed.Keystrokes;
        return $"可以还原，将把全部字符送入输入法：{shown}";
    }

    internal static bool Allows(PinyinParse parsed, TriggerKind kind)
    {
        _ = kind;
        return parsed.Keystrokes.Length is > 0 and <= MaxLetters;
    }

    private static bool TryFold(char ch, out char lower)
    {
        lower = ch switch
        {
            >= 'a' and <= 'z' => ch,
            >= 'A' and <= 'Z' => (char)(ch + 32),
            >= '0' and <= '9' => ch,
            'ā' or 'á' or 'ǎ' or 'à' => 'a',
            'ō' or 'ó' or 'ǒ' or 'ò' => 'o',
            'ē' or 'é' or 'ě' or 'è' => 'e',
            'ī' or 'í' or 'ǐ' or 'ì' => 'i',
            'ū' or 'ú' or 'ǔ' or 'ù' => 'u',
            'ü' or 'Ü' or 'ǖ' or 'ǘ' or 'ǚ' or 'ǜ' => 'v',
            _ => '\0',
        };
        return lower != '\0';
    }
}
