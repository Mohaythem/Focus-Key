using FocusKey.Foundation.History;

namespace FocusKey.Foundation.Tests.History;

public sealed class WebsiteHistoryCsvTests
{
    [Fact]
    public void Parse_ExactWebsiteFormat_SucceedsWithAccurateValues()
    {
        string csv =
            "date\tproject\thours\r\n" +
            "20260904\t\"\"\t5.98\r\n" +
            "20260905\t\"\"\t4.51\r\n" +
            "20260906\t\"\"\t11.49\r\n" +
            "20260907\t\"\"\t8.45\r\n" +
            "20260908\t\"\"\t5\r\n" +
            "20260909\t\"\"\t8.37\r\n" +
            "20260910\t\"\"\t11.06\r\n";

        var result = WebsiteHistoryCsv.Parse(csv);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(7, result.TotalRowsFound);
        Assert.Equal(0, result.InvalidRows);
        Assert.Equal(7, result.ValidEntries.Count);

        Assert.Equal(new DateOnly(2026, 9, 4), result.ValidEntries[0].Date);
        Assert.Equal(string.Empty, result.ValidEntries[0].Project);
        Assert.Equal(5.98, result.ValidEntries[0].Hours);

        Assert.Equal(new DateOnly(2026, 9, 8), result.ValidEntries[4].Date);
        Assert.Equal(string.Empty, result.ValidEntries[4].Project);
        Assert.Equal(5.0, result.ValidEntries[4].Hours);

        Assert.Equal(new DateOnly(2026, 9, 10), result.ValidEntries[6].Date);
        Assert.Equal(11.06, result.ValidEntries[6].Hours);
    }

    [Fact]
    public void Parse_WithBomAndBlankLines_ParsesCorrectly()
    {
        string csv =
            "\uFEFF\r\n\r\n" +
            "date\tproject\thours\r\n" +
            "\r\n" +
            "20260901\t\"Client Work\"\t3.5\r\n" +
            "\r\n" +
            "20260902\t\t4.0\r\n" +
            "\r\n";

        var result = WebsiteHistoryCsv.Parse(csv);

        Assert.True(result.Success);
        Assert.Equal(2, result.TotalRowsFound);
        Assert.Equal(0, result.InvalidRows);
        Assert.Equal(2, result.ValidEntries.Count);
        Assert.Equal("Client Work", result.ValidEntries[0].Project);
        Assert.Equal(3.5, result.ValidEntries[0].Hours);
        Assert.Equal(string.Empty, result.ValidEntries[1].Project);
        Assert.Equal(4.0, result.ValidEntries[1].Hours);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    public void Parse_EmptyFile_ReturnsFailure(string emptyCsv)
    {
        var result = WebsiteHistoryCsv.Parse(emptyCsv);
        Assert.False(result.Success);
        Assert.Contains("empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("date,project,hours\r\n20260901,,3.5")] // Comma delimited
    [InlineData("day\tproject\thours\r\n20260901\t\"\"\t3.5")] // Wrong header
    [InlineData("date\thours\r\n20260901\t3.5")] // Missing column
    public void Parse_InvalidHeader_ReturnsFailure(string invalidCsv)
    {
        var result = WebsiteHistoryCsv.Parse(invalidCsv);
        Assert.False(result.Success);
        Assert.Contains("header", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MalformedRows_AreSkippedAndCounted()
    {
        string csv =
            "date\tproject\thours\r\n" +
            "20260901\t\"\"\t3.5\r\n" +
            "invalid_date\t\"\"\t4.0\r\n" +      // Bad date
            "20260902\t\"\"\t-2.5\r\n" +          // Negative hours
            "20260903\t\"\"\t25.0\r\n" +          // Exceeds 24 hours
            "20260904\t\"\"\tnot_a_number\r\n" +  // Non-numeric
            "20260905\t\"\"\r\n" +                // Missing column
            "20260906\t\"\"\t7.25\r\n";

        var result = WebsiteHistoryCsv.Parse(csv);

        Assert.True(result.Success);
        Assert.Equal(7, result.TotalRowsFound);
        Assert.Equal(5, result.InvalidRows);
        Assert.Equal(2, result.ValidEntries.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), result.ValidEntries[0].Date);
        Assert.Equal(3.5, result.ValidEntries[0].Hours);
        Assert.Equal(new DateOnly(2026, 9, 6), result.ValidEntries[1].Date);
        Assert.Equal(7.25, result.ValidEntries[1].Hours);
    }

    [Fact]
    public void Parse_IntraFileDuplicates_ConsolidatesHoursUpTo24()
    {
        string csv =
            "date\tproject\thours\r\n" +
            "20260901\t\"Focus\"\t3.0\r\n" +
            "20260901\t\"Focus\"\t4.5\r\n" +
            "20260901\t\"Other\"\t2.0\r\n";

        var result = WebsiteHistoryCsv.Parse(csv);

        Assert.True(result.Success);
        Assert.Equal(3, result.TotalRowsFound);
        Assert.Equal(0, result.InvalidRows);
        Assert.Equal(2, result.ValidEntries.Count);

        var focus = result.ValidEntries.First(e => e.Project == "Focus");
        Assert.Equal(7.5, focus.Hours);

        var other = result.ValidEntries.First(e => e.Project == "Other");
        Assert.Equal(2.0, other.Hours);
    }

    [Fact]
    public void Export_FormatsCorrectTabDelimitedOutput()
    {
        var records = new List<DailyFocusExportRecord>
        {
            new(new DateOnly(2026, 9, 5), "", 4.51),
            new(new DateOnly(2026, 9, 4), "Alpha", 5.98),
            new(new DateOnly(2026, 9, 6), "", 0.0), // Zero should be excluded
        };

        string exported = WebsiteHistoryCsv.Export(records);

        string expected =
            "date\tproject\thours\r\n" +
            "20260904\t\"Alpha\"\t5.98\r\n" +
            "20260905\t\"\"\t4.51\r\n";

        Assert.Equal(expected, exported);
    }

    [Fact]
    public void RoundTrip_ExportThenParse_PreservesData()
    {
        var records = new List<DailyFocusExportRecord>
        {
            new(new DateOnly(2026, 9, 1), "", 6.25),
            new(new DateOnly(2026, 9, 2), "Work", 8.0),
            new(new DateOnly(2026, 9, 3), "Study", 4.75),
        };

        string exported = WebsiteHistoryCsv.Export(records);
        var parsed = WebsiteHistoryCsv.Parse(exported);

        Assert.True(parsed.Success);
        Assert.Equal(3, parsed.ValidEntries.Count);
        Assert.Equal(records[0].Date, parsed.ValidEntries[0].Date);
        Assert.Equal(records[0].Project, parsed.ValidEntries[0].Project);
        Assert.Equal(records[0].Hours, parsed.ValidEntries[0].Hours);

        Assert.Equal(records[1].Date, parsed.ValidEntries[1].Date);
        Assert.Equal(records[1].Project, parsed.ValidEntries[1].Project);
        Assert.Equal(records[1].Hours, parsed.ValidEntries[1].Hours);

        Assert.Equal(records[2].Date, parsed.ValidEntries[2].Date);
        Assert.Equal(records[2].Project, parsed.ValidEntries[2].Project);
        Assert.Equal(records[2].Hours, parsed.ValidEntries[2].Hours);
    }
}
