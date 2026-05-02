using System.Text;
using System.Text.RegularExpressions;

namespace SkipWatch.Core.Services.Utilities;

public static class SrtConverter
{
    /// <summary>
    /// Converts SRT to the prd's transcript line format: <c>[mm:ss] line text</c>.
    /// Used by ApifyTranscriptSource. Videos &gt; 60 minutes are filtered out before reaching
    /// the transcript worker (MAX_VIDEO_DURATION_MINUTES), so mm:ss is always sufficient.
    /// </summary>
    public static string ConvertSrtToPrdFormat(string srtContent)
    {
        var output = new StringBuilder();
        var entries = srtContent.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var lines = entry.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) continue;

            var match = Regex.Match(lines[1], @"(\d+):(\d+):(\d+),\d+");
            if (!match.Success) continue;

            var hours = int.Parse(match.Groups[1].Value);
            var minutes = int.Parse(match.Groups[2].Value);
            var seconds = int.Parse(match.Groups[3].Value);
            var totalMinutes = hours * 60 + minutes;
            var formattedTime = $"{totalMinutes:D2}:{seconds:D2}";

            var textBuilder = new StringBuilder();
            for (int i = 2; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                textBuilder.Append(lines[i].Trim() + " ");
            }

            var subtitleText = textBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(subtitleText)) continue;

            output.AppendLine($"[{formattedTime}] {subtitleText}");
        }

        return output.ToString();
    }

    public static string ConvertSrtToSimpleFormat(string srtContent)
    {
        var simpleOutput = new StringBuilder();
        var entries = srtContent.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var lines = entry.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) continue;

            var match = Regex.Match(lines[1], @"(\d+):(\d+):(\d+),\d+");
            if (!match.Success) continue;

            var hours = match.Groups[1].Value.PadLeft(2, '0');
            var minutes = match.Groups[2].Value.PadLeft(2, '0');
            var seconds = match.Groups[3].Value.PadLeft(2, '0');
            var formattedTime = $"{hours}:{minutes}:{seconds}";

            var textBuilder = new StringBuilder();
            for (int i = 2; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                textBuilder.Append(lines[i].Trim() + " ");
            }

            var subtitleText = textBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(subtitleText)) continue;

            simpleOutput.AppendLine($"{formattedTime}  {subtitleText}");
        }

        return simpleOutput.ToString();
    }
}
