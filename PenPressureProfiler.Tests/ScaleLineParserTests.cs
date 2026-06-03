using PenPressureProfiler;

namespace PenPressureProfiler.Tests;

public class ScaleLineParserTests
{
    // ── Parse success ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_PlainNumber_ReturnsSuccess()
    {
        var result = ScaleLineParser.Parse("123.4");
        Assert.True(result.Parsed);
        Assert.Equal(123.4, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_NumberWithGramSuffix_StripsAndParses()
    {
        var result = ScaleLineParser.Parse("  56.78g  ");
        Assert.True(result.Parsed);
        Assert.Equal(56.78, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_NumberWithMgSuffix_StripsAndParses()
    {
        var result = ScaleLineParser.Parse("9999.0M");
        Assert.True(result.Parsed);
        Assert.Equal(9999.0, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_MultiTokenLine_UsesLastToken()
    {
        // Typical scale output: "ST,GS   50.00g"
        var result = ScaleLineParser.Parse("ST,GS   50.00g");
        Assert.True(result.Parsed);
        Assert.Equal(50.0, result.ScaleRecord!.ReadingAsDouble);
    }

    // ── Negative values ────────────────────────────────────────────────────

    [Fact]
    public void Parse_GluedLeadingMinus_IsNegative()
    {
        var result = ScaleLineParser.Parse("-50.00g");
        Assert.True(result.Parsed);
        Assert.Equal(-50.0, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_SeparateMinusToken_IsNegative()
    {
        // The case that used to drop the sign and read positive.
        var result = ScaleLineParser.Parse("ST,GS - 50.00g");
        Assert.True(result.Parsed);
        Assert.Equal(-50.0, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_MinusTokenWithWideGap_IsNegative()
    {
        // Reported real-world line: "-     2.1g"
        var result = ScaleLineParser.Parse("-     2.1g");
        Assert.True(result.Parsed);
        Assert.Equal(-2.1, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_StxFramedNegative_IsNegative()
    {
        // Real line captured from the scale: STX (0x02) framing byte, then the
        // sign glued to it, then the value: "\x02-     2.1g".
        var result = ScaleLineParser.Parse("-     2.1g");
        Assert.True(result.Parsed);
        Assert.Equal(-2.1, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_EnDashSignToken_IsNegative()
    {
        // Some scales emit an en dash (U+2013) rather than ASCII '-'.
        var result = ScaleLineParser.Parse("–     2.1g");
        Assert.True(result.Parsed);
        Assert.Equal(-2.1, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_TabSeparatedSignToken_IsNegative()
    {
        // Sign padded away from the digits with tabs rather than spaces.
        var result = ScaleLineParser.Parse("-\t\t2.1g");
        Assert.True(result.Parsed);
        Assert.Equal(-2.1, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_TrailingMinus_IsNegative()
    {
        var result = ScaleLineParser.Parse("50.00-g");
        Assert.True(result.Parsed);
        Assert.Equal(-50.0, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_UnicodeMinus_IsNegative()
    {
        var result = ScaleLineParser.Parse("−50.00g");
        Assert.True(result.Parsed);
        Assert.Equal(-50.0, result.ScaleRecord!.ReadingAsDouble);
    }

    [Fact]
    public void Parse_LeadingPlus_IsPositive()
    {
        var result = ScaleLineParser.Parse("+50.00g");
        Assert.True(result.Parsed);
        Assert.Equal(50.0, result.ScaleRecord!.ReadingAsDouble);
    }

    // ── Parse failures ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_NullLine_ReturnsFailed()
    {
        var result = ScaleLineParser.Parse(null);
        Assert.False(result.Parsed);
        Assert.Null(result.ScaleRecord);
    }

    [Fact]
    public void Parse_EmptyLine_ReturnsFailed()
    {
        var result = ScaleLineParser.Parse("   ");
        Assert.False(result.Parsed);
    }

    [Fact]
    public void Parse_NonNumericToken_ReturnsFailed()
    {
        var result = ScaleLineParser.Parse("OVER");
        Assert.False(result.Parsed);
        Assert.Contains("Failed to parse", result.Error);
    }

    // ── Input preservation ─────────────────────────────────────────────────

    [Fact]
    public void Parse_Success_PreservesOriginalLine()
    {
        const string line = "ST,GS  100.0g";
        var result = ScaleLineParser.Parse(line);
        Assert.True(result.Parsed);
        Assert.Equal(line, result.ScaleRecord!.Line);
    }
}
