namespace SkipWatch.Core.Services.Utilities;

public static class TextFormatter
{
    public static string Truncate(string? text, int maxLength, string fallbackText = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return fallbackText;
        if (text.Length <= maxLength) return text;
        return text[..maxLength].TrimEnd() + "...";
    }

    public static string FormatCount(int count, string singularText, string pluralText) =>
        count == 1 ? $"{count} {singularText}" : $"{count} {pluralText}";

    public static string FormatCountWithZero(int count, string zeroText, string singularText, string pluralText) =>
        count switch
        {
            0 => zeroText,
            1 => singularText,
            _ => $"{count} {pluralText}"
        };
}
