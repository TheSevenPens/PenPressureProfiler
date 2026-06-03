using System.Globalization;

namespace PenPressureProfiler;

/// <summary>Parses raw text lines from a serial-connected digital scale.</summary>
public static class ScaleLineParser
{
    private const char MgSuffix   = 'M';
    private const char GramSuffix = 'g';

    public static ScaleParsedLine Parse(string? line)
    {
        if (line is null)
            return new ScaleParsedLine(line, false, null, "Line was null");

        line = line.Trim();

        if (line.Length == 0)
            return new ScaleParsedLine(line, false, null, "Line was empty");

        // Normalise the line for tokenising: map the various dash / minus
        // glyphs instruments emit to ASCII '-', and turn control characters
        // (e.g. the STX/ETX framing bytes some scales wrap each line in) into
        // spaces so they act as separators instead of gluing onto the sign.
        string norm = Normalize(line);

        // Split on ANY whitespace (space, tab, etc.) — some scales pad the sign
        // away from the digits with tabs, which a space-only split would miss.
        var tokens = norm.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return new ScaleParsedLine(line, false, null, "No tokens in line");

        // The value lives in the last token; strip the unit suffix (g / M).
        string strForce = TrimLastCharIf(tokens[^1], MgSuffix);
        strForce = TrimLastCharIf(strForce, GramSuffix);

        // The sign may be glued to the digits ("-50.00"), trail them ("50.00-"),
        // or arrive as its own token somewhere before the value ("- 50.00 g",
        // "ST,GS - 50.00g"). Collect the sign from all of those positions.
        bool negative = false;

        if (strForce.EndsWith('-'))   { negative = !negative; strForce = strForce[..^1]; }
        if (strForce.StartsWith('-')) { negative = !negative; strForce = strForce[1..]; }
        if (strForce.StartsWith('+')) strForce = strForce[1..];

        // A bare "-" token anywhere before the value carries the sign.
        for (int i = 0; i < tokens.Length - 1; i++)
            if (tokens[i] == "-") negative = !negative;

        if (!double.TryParse(strForce, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return new ScaleParsedLine(line, false, null, $"Failed to parse force \"{strForce}\"");

        if (negative) value = -value;

        var sr = new ScaleRecord
        {
            Line            = line,
            ReadingAsString = strForce,
            ReadingAsDouble = value
        };
        return new ScaleParsedLine(line, true, sr, string.Empty);
    }

    private static string TrimLastCharIf(string s, char c) =>
        s is null or "" ? s ?? "" : s[^1] == c ? s[..^1] : s;

    /// <summary>
    /// Maps common Unicode dash/minus glyphs to the ASCII hyphen '-' and
    /// replaces control characters (framing bytes like STX/ETX, stray CR, …)
    /// with spaces so tokenising sees clean, whitespace-separated fields.
    /// </summary>
    private static string Normalize(string s)
    {
        Span<char> buf = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            buf[i] = c switch
            {
                '−' or '‐' or '‑' or '–' or '—' => '-',
                _ when char.IsControl(c)         => ' ',
                _                                => c,
            };
        }
        return new string(buf);
    }
}
