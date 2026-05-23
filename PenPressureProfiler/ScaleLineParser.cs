namespace PenPressureProfiler;

/// <summary>
/// Parses raw text lines from a serial-connected digital scale.
/// </summary>
public static class ScaleLineParser
{
    private const char MgSuffix   = 'M';
    private const char GramSuffix = 'g';

    public static ScaleParsedLine Parse(string? line)
    {
        if (line is null)
        {
            return new ScaleParsedLine(line, false, null, "Line was null");
        }

        line = line.Trim();

        if (line.Length == 0)
        {
            return new ScaleParsedLine(line, false, null, "Line was empty");
        }

        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            return new ScaleParsedLine(line, false, null, "No tokens in line");
        }

        string str_force = TrimLastCharIf(tokens[^1], MgSuffix);
        str_force = TrimLastCharIf(str_force, GramSuffix);

        if (!double.TryParse(str_force, out double value))
            return new ScaleParsedLine(line, false, null, $"Failed to parse force \"{str_force}\"");

        var sr = new ScaleRecord { Line = line, ReadingAsString = str_force, ReadingAsDouble = value };
        return new ScaleParsedLine(line, true, sr, string.Empty);
    }

    private static string TrimLastCharIf(string s, char c) =>
        (s is null or "") ? s : (s[^1] == c ? s[..^1] : s);
}
