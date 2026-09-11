using FocusKey.Foundation.History;

namespace FocusKey.Foundation.Tests.History;

public sealed class WebsiteHistoryCsvTests
{
    [Fact]
    public void Parse_ExactWebsiteHoursFormat_SucceedsWithAccurateValues()
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
        Assert.Equal(CsvHistorySchema.Hours, result.DetectedSchema);
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
    public void Parse_ExactWebsiteMinutesFormat_SucceedsWithExactValues()
    {
        string csv =
            "date\tproject\tminutes\r\n" +
            "20260905\t\"\"\t271\r\n" +
            "20260906\t\"\"\t690\r\n" +
            "20260907\t\"\"\t507\r\n" +
            "20260908\t\"\"\t300\r\n" +
            "20260909\t\"\"\t503\r\n" +
            "20260910\t\"\"\t664\r\n" +
            "20260911\t\"\"\t217\r\n";

        var result = WebsiteHistoryCsv.Parse(csv);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(CsvHistorySchema.Minutes, result.DetectedSchema);
        Assert.Equal(7, result.TotalRowsFound);
        Assert.Equal(0, result.InvalidRows);
        Assert.Equal(7, result.ValidEntries.Count);

        // 20260905: 271 minutes = 16260 seconds
        Assert.Equal(new DateOnly(2026, 9, 5), result.ValidEntries[0].Date);
        Assert.Equal(string.Empty, result.ValidEntries[0].Project);
        Assert.Equal(TimeSpan.FromSeconds(271 * 60), result.ValidEntries[0].Duration);
        Assert.Equal(16260, result.ValidEntries[0].Duration.TotalSeconds);

        // 20260906: 690 minutes = 41400 seconds
        Assert.Equal(new DateOnly(2026, 9, 6), result.ValidEntries[1].Date);
        Assert.Equal(TimeSpan.FromSeconds(41400), result.ValidEntries[1].Duration);
        Assert.Equal(41400, result.ValidEntries[1].Duration.TotalSeconds);
        Assert.Equal(690.0, result.ValidEntries[1].Duration.TotalMinutes);

        // 20260907: 507 minutes = 30420 seconds
        Assert.Equal(new DateOnly(2026, 9, 7), result.ValidEntries[2].Date);
        Assert.Equal(TimeSpan.FromSeconds(507 * 60), result.ValidEntries[2].Duration);

        // 20260908: 300 minutes = 18000 seconds
        Assert.Equal(new DateOnly(2026, 9, 8), result.ValidEntries[3].Date);
        Assert.Equal(TimeSpan.FromSeconds(300 * 60), result.ValidEntries[3].Duration);

        // 20260909: 503 minutes = 30180 seconds
        Assert.Equal(new DateOnly(2026, 9, 9), result.ValidEntries[4].Date);
        Assert.Equal(TimeSpan.FromSeconds(503 * 60), result.ValidEntries[4].Duration);

        // 20260910: 664 minutes = 39840 seconds
        Assert.Equal(new DateOnly(2026, 9, 10), result.ValidEntries[5].Date);
        Assert.Equal(TimeSpan.FromSeconds(664 * 60), result.ValidEntries[5].Duration);

        // 20260911: 217 minutes = 13020 seconds
        Assert.Equal(new DateOnly(2026, 9, 11), result.ValidEntries[6].Date);
        Assert.Equal(TimeSpan.FromSeconds(217 * 60), result.ValidEntries[6].Duration);
    }

    [Fact]
    public void Parse_WithBomAndBlankLines_ParsesCorrectly()
    {
        string csv =
            "\uFEFF\r\n\r\n" +
            "date\tproject\tminutes\r\n" +
            "\r\n" +
            "20260901\t\"Client Work\"\t210\r\n" +
            "\r\n" +
            "20260902\t\t240\r\n" +
            "\r\n";

        var result = WebsiteHistoryCsv.Parse(csv);

        Assert.True(result.Success);
        Assert.Equal(CsvHistorySchema.Minutes, result.DetectedSchema);
        Assert.Equal(2, result.TotalRowsFound);
        Assert.Equal(0, result.InvalidRows);
        Assert.Equal(2, result.ValidEntries.Count);
        Assert.Equal("Client Work", result.ValidEntries[0].Project);
        Assert.Equal(TimeSpan.FromMinutes(210), result.ValidEntries[0].Duration);
        Assert.Equal(string.Empty, result.ValidEntries[1].Project);
        Assert.Equal(TimeSpan.FromMinutes(240), result.ValidEntries[1].Duration);
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
    [InlineData("date\tproject\tseconds\r\n20260901\t\"\"\t3600")] // Unsupported unit
    [InlineData("date\tproject\thours\tminutes\r\n20260901\t\"\"\t1\t60")] // Ambiguous
    public void Parse_InvalidHeader_ReturnsFailureExplainingBothFormats(string invalidCsv)
    {
        var result = WebsiteHistoryCsv.Parse(invalidCsv);
        Assert.False(result.Success);
        Assert.Contains("'date\tproject\thours'", result.ErrorMessage);
        Assert.Contains("'date\tproject\tminutes'", result.ErrorMessage);
    }

    [Fact]
    public void Parse_MalformedRowsInMinutes_AreSkippedAndCounted()
    {
        string csv =
            "date\tproject\tminutes\r\n" +
            "20260901\t\"\"\t210\r\n" +
            "invalid_date\t\"\"\t240\r\n" +      // Bad date
            "20260902\t\"\"\t-150\r\n" +          // Negative minutes
            "20260903\t\"\"\t1500\r\n" +          // Exceeds 24 hours (1440 min)
            "20260904\t\"\"\tnot_a_number\r\n" +  // Non-numeric
            "20260905\t\"\"\r\n" +                // Missing column
            "20260906\t\"\"\t435\r\n";

        var result = WebsiteHistoryCsv.Parse(csv);

        Assert.True(result.Success);
        Assert.Equal(7, result.TotalRowsFound);
        Assert.Equal(5, result.InvalidRows);
        Assert.Equal(2, result.ValidEntries.Count);
        Assert.Equal(new DateOnly(2026, 9, 1), result.ValidEntries[0].Date);
        Assert.Equal(TimeSpan.FromMinutes(210), result.ValidEntries[0].Duration);
        Assert.Equal(new DateOnly(2026, 9, 6), result.ValidEntries[1].Date);
        Assert.Equal(TimeSpan.FromMinutes(435), result.ValidEntries[1].Duration);
    }

    [Fact]
    public void Parse_IntraFileDuplicates_ConsolidatesMinutesUpTo24Hours()
    {
        string csv =
            "date\tproject\tminutes\r\n" +
            "20260901\t\"Focus\"\t180\r\n" +
            "20260901\t\"Focus\"\t270\r\n" +
            "20260901\t\"Other\"\t120\r\n";

        var result = WebsiteHistoryCsv.Parse(csv);

        Assert.True(result.Success);
        Assert.Equal(3, result.TotalRowsFound);
        Assert.Equal(0, result.InvalidRows);
        Assert.Equal(2, result.ValidEntries.Count);

        var focus = result.ValidEntries.First(e => e.Project == "Focus");
        Assert.Equal(TimeSpan.FromMinutes(450), focus.Duration);

        var other = result.ValidEntries.First(e => e.Project == "Other");
        Assert.Equal(TimeSpan.FromMinutes(120), other.Duration);
    }

    [Fact]
    public void Export_FormatsCorrectTabDelimitedOutputInCanonicalMinutes()
    {
        var records = new List<DailyFocusExportRecord>
        {
            new(new DateOnly(2026, 9, 5), "", TimeSpan.FromMinutes(271)),
            new(new DateOnly(2026, 9, 4), "Alpha", TimeSpan.FromMinutes(359)),
            new(new DateOnly(2026, 9, 6), "", TimeSpan.Zero), // Zero should be excluded
        };

        string exported = WebsiteHistoryCsv.Export(records);

        string expected =
            "date\tproject\tminutes\r\n" +
            "20260904\t\"Alpha\"\t359\r\n" +
            "20260905\t\"\"\t271\r\n";

        Assert.Equal(expected, exported);
    }

    [Fact]
    public void RoundTrip_ExportThenParse_PreservesData()
    {
        var records = new List<DailyFocusExportRecord>
        {
            new(new DateOnly(2026, 9, 1), "", TimeSpan.FromMinutes(375)),
            new(new DateOnly(2026, 9, 2), "Work", TimeSpan.FromMinutes(480)),
            new(new DateOnly(2026, 9, 3), "Study", TimeSpan.FromMinutes(285)),
        };

        string exported = WebsiteHistoryCsv.Export(records);
        var parsed = WebsiteHistoryCsv.Parse(exported);

        Assert.True(parsed.Success);
        Assert.Equal(CsvHistorySchema.Minutes, parsed.DetectedSchema);
        Assert.Equal(3, parsed.ValidEntries.Count);

        Assert.Equal(records[0].Date, parsed.ValidEntries[0].Date);
        Assert.Equal(records[0].Project, parsed.ValidEntries[0].Project);
        Assert.Equal(records[0].Duration, parsed.ValidEntries[0].Duration);

        Assert.Equal(records[1].Date, parsed.ValidEntries[1].Date);
        Assert.Equal(records[1].Project, parsed.ValidEntries[1].Project);
        Assert.Equal(records[1].Duration, parsed.ValidEntries[1].Duration);

        Assert.Equal(records[2].Date, parsed.ValidEntries[2].Date);
        Assert.Equal(records[2].Project, parsed.ValidEntries[2].Project);
        Assert.Equal(records[2].Duration, parsed.ValidEntries[2].Duration);
    }
}
