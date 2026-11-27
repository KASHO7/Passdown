using System.Globalization;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Documents.Associate;
using Carowinds.Passdown.Documents.Associate.Templates;
using Carowinds.Passdown.Documents.Tests.Associate.TestSupport;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Carowinds.Passdown.Documents.Tests.Associate.Weather;

/// <summary>
/// Phase 37F: the Weather Outlook rendered by the real QuestPDF pipeline in Classic, Modern and Compact. Compares each
/// weather-enabled PDF with the same document without weather: nothing else moves vertically, no page is added, no
/// text collides, the outlook stays in its column, it uses no images or new fonts, and output is deterministic.
/// </summary>
public class Phase37FPdfWeatherLayoutTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 8, 21, 11, 0, 0, TimeSpan.Zero);
    private static readonly AssociatePassdownPdfGenerator Generator = new();
    private const double PageWidth = 612;

    public static TheoryData<AssociatePassdownTemplate, string, string> Cases()
    {
        var data = new TheoryData<AssociatePassdownTemplate, string, string>();
        foreach (var template in Enum.GetValues<AssociatePassdownTemplate>())
        {
            foreach (var day in new[] { "normal", "sparse", "buyout", "dense", "excessive", "minimal" })
            {
                foreach (var weather in new[] { "normal", "rain", "long", "notemp", "norain" })
                {
                    data.Add(template, day, weather);
                }
            }
        }

        return data;
    }

    internal static AssociateWorkspace Day(string name) => name switch
    {
        "normal" => AssociateWorkspaceFixtures.NormalDay(),
        "sparse" => AssociateWorkspaceFixtures.SparseDay(),
        "buyout" => AssociateWorkspaceFixtures.PrivateBuyoutDay(),
        "dense" => AssociateWorkspaceFixtures.DenseDay(),
        "excessive" => AssociateWorkspaceFixtures.ExcessiveDay(),
        "minimal" => AssociateWorkspaceFixtures.MinimalValid(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    internal static PdfWeatherOutlook Outlook(string name, DateOnly date) => name switch
    {
        "normal" => PdfWeatherFixtures.Normal(date),
        "rain" => PdfWeatherFixtures.RainLater(date),
        "long" => PdfWeatherFixtures.LongCondition(date),
        "notemp" => PdfWeatherFixtures.NoTemperature(date),
        "norain" => PdfWeatherFixtures.NoRain(date),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    internal static AssociatePassdownPdfResult Render(AssociateWorkspace workspace, AssociatePassdownTemplate template, PdfWeatherOutlook? weather) =>
        Generator.Generate(AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt, weather), template);

    private static string Key(Word word) => word.Text + "@" + Math.Round(word.BoundingBox.Bottom, 2).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>One glyph and its baseline height. Glyphs, not words: PdfPig may split letter-spaced labels into words differently between two renders.</summary>
    private static string Key(Letter letter) => letter.Value + "@" + Math.Round(letter.StartBaseLine.Y, 2).ToString("0.00", CultureInfo.InvariantCulture);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Weather_MovesNothingVertically_AddsNoPage_AndCollidesWithNothing(AssociatePassdownTemplate template, string day, string weather)
    {
        var workspace = Day(day);
        var without = Render(workspace, template, null);
        var with = Render(workspace, template, Outlook(weather, workspace.OperatingDate));

        Assert.Equal(AssociatePassdownPdfStatus.Generated, with.Status);
        Assert.Equal(without.PageCount, with.PageCount);
        Assert.Equal(PdfInspector.CountPages(without.PdfBytes!), PdfInspector.CountPages(with.PdfBytes!));

        using var plain = PdfDocument.Open(without.PdfBytes!);
        using var weathered = PdfDocument.Open(with.PdfBytes!);
        for (var page = 1; page <= plain.NumberOfPages; page++)
        {
            // Every glyph of the weather-free document is still there at exactly the same height (zero displacement).
            var remaining = weathered.GetPage(page).Letters.Select(Key).GroupBy(key => key).ToDictionary(group => group.Key, group => group.Count());
            foreach (var key in plain.GetPage(page).Letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)).Select(Key))
            {
                Assert.True(remaining.TryGetValue(key, out var count) && count > 0, $"{template}/{day}/{weather} page {page}: '{key}' moved or disappeared.");
                remaining[key] = count - 1;
            }
        }

        var words = weathered.GetPage(1).GetWords().ToList();
        for (var i = 0; i < words.Count; i++)
        {
            for (var j = i + 1; j < words.Count; j++)
            {
                Assert.False(Overlap(words[i], words[j]), $"{template}/{day}/{weather}: '{words[i].Text}' overlaps '{words[j].Text}'.");
            }
        }
    }

    private static bool Overlap(Word a, Word b)
    {
        const double inset = 0.6; // glyph boxes of adjacent words may touch; they must not intersect
        return a.BoundingBox.Left + inset < b.BoundingBox.Right && b.BoundingBox.Left + inset < a.BoundingBox.Right
            && a.BoundingBox.Bottom + inset < b.BoundingBox.Top && b.BoundingBox.Bottom + inset < a.BoundingBox.Top;
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic, 34, 220)]
    [InlineData(AssociatePassdownTemplate.Modern, 36, 196)]
    [InlineData(AssociatePassdownTemplate.Compact, 24, 204)]
    public void Weather_StaysInsideItsRightHandColumn_AndThePrintableMargin(AssociatePassdownTemplate template, double margin, double width)
    {
        foreach (var name in new[] { "normal", "rain", "long", "notemp", "norain" })
        {
            var workspace = AssociateWorkspaceFixtures.NormalDay();
            var without = Letters(Render(workspace, template, null).PdfBytes!);
            var with = Letters(Render(workspace, template, Outlook(name, workspace.OperatingDate)).PdfBytes!);
            var added = AddedLetters(without, with);

            Assert.NotEmpty(added);
            Assert.All(added, letter =>
            {
                Assert.True(letter.BoundingBox.Left >= PageWidth - margin - width - 0.5, $"{template}/{name}: '{letter.Value}' starts left of the weather column.");
                Assert.True(letter.BoundingBox.Right <= PageWidth - margin + 0.5, $"{template}/{name}: '{letter.Value}' crosses the right margin.");
            });
        }
    }

    private static List<Letter> Letters(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPage(1).Letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)).ToList();
    }

    /// <summary>
    /// Glyphs in <paramref name="with"/> beyond those the weather-free page has on the same baseline, matched from the
    /// left (Modern's metadata columns narrow horizontally beside the outlook, so their glyphs keep their baseline but
    /// not their x; the outlook is right-aligned, so the unmatched glyphs on a shared baseline are its own).
    /// </summary>
    private static List<Letter> AddedLetters(List<Letter> without, List<Letter> with)
    {
        var remaining = without.Select(Key).GroupBy(key => key).ToDictionary(group => group.Key, group => group.Count());
        var added = new List<Letter>();
        foreach (var letter in with.OrderBy(letter => letter.StartBaseLine.X))
        {
            if (remaining.TryGetValue(Key(letter), out var count) && count > 0)
            {
                remaining[Key(letter)] = count - 1;
            }
            else
            {
                added.Add(letter);
            }
        }

        return added;
    }

    private static List<Word> Words(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPage(1).GetWords().ToList();
    }

    /// <summary>Words in <paramref name="with"/> that the weather-free page does not have at the same height.</summary>
    private static List<Word> Added(List<Word> without, List<Word> with)
    {
        var remaining = without.Select(Key).GroupBy(key => key).ToDictionary(group => group.Key, group => group.Count());
        var added = new List<Word>();
        foreach (var word in with)
        {
            if (remaining.TryGetValue(Key(word), out var count) && count > 0)
            {
                remaining[Key(word)] = count - 1;
            }
            else
            {
                added.Add(word);
            }
        }

        return added;
    }

    [Fact]
    public void Classic_UsesTheFullSummary_WithLabelConditionRainAndSource()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var text = PdfInspector.ExtractPageText(Render(workspace, AssociatePassdownTemplate.Classic, PdfWeatherFixtures.Normal(workspace.OperatingDate)).PdfBytes!, 1);

        PdfTextAssertWeather.Contains("WEATHER", text);
        PdfTextAssertWeather.Contains("63–74°F", text);
        PdfTextAssertWeather.Contains("Partly cloudy · Rain chance up to 10%", text);
        PdfTextAssertWeather.Contains("NWS forecast 6:05 AM", text);
    }

    [Fact]
    public void Classic_RainLater_SplitsConditionAndTiming()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var text = PdfInspector.ExtractPageText(Render(workspace, AssociatePassdownTemplate.Classic, PdfWeatherFixtures.RainLater(workspace.OperatingDate)).PdfBytes!, 1);

        PdfTextAssertWeather.Contains("Chance showers and thunderstorms", text);
        PdfTextAssertWeather.Contains("Storms possible after 5 PM (up to 60%)", text);
    }

    [Fact]
    public void Modern_FitsTheCondensedSummaryOppositeTheHours()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var bytes = Render(workspace, AssociatePassdownTemplate.Modern, PdfWeatherFixtures.Normal(workspace.OperatingDate)).PdfBytes!;
        var text = PdfInspector.ExtractPageText(bytes, 1);

        PdfTextAssertWeather.Contains("WEATHER", text);
        PdfTextAssertWeather.Contains("Partly cloudy · Rain up to 10% · NWS 6:05 AM", text);

        // Top-aligned with the hours headline.
        var hours = PdfInspector.FindWordBox(bytes, 1, "9:00")!.Value;
        var label = PdfInspector.FindWordBox(bytes, 1, "WEATHER")!.Value;
        Assert.InRange(label.Top, hours.Top - 1, hours.Top + 4);
    }

    [Fact]
    public void Compact_FitsTwoLinesBesideTheStrip()
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var text = PdfInspector.ExtractPageText(Render(workspace, AssociatePassdownTemplate.Compact, PdfWeatherFixtures.RainLater(workspace.OperatingDate)).PdfBytes!, 1);

        PdfTextAssertWeather.Contains("WEATHER", text);
        PdfTextAssertWeather.Contains("61–77°F", text);
        PdfTextAssertWeather.Contains("Storms", text);
        PdfTextAssertWeather.Contains("Rain up to 60% · NWS 6:05 AM", text);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    public void LongCondition_FallsBackToTheShortForm_NeverWrapsOrShrinks(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var text = PdfInspector.ExtractPageText(Render(workspace, template, PdfWeatherFixtures.LongCondition(workspace.OperatingDate)).PdfBytes!, 1);

        Assert.DoesNotContain("blowing", text, StringComparison.Ordinal);
        PdfTextAssertWeather.Contains("Storms", text);
        PdfTextAssertWeather.Contains("NWS Dec 31, 11:59 PM", text);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    public void NothingFits_PrintsNoWeatherAndReservesNoSpace(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var unfittable = PdfWeatherFixtures.Normal(workspace.OperatingDate) with
        {
            AttributionText = string.Concat(Enumerable.Repeat("NWS forecast source text ", 20)),
            ShortAttributionText = string.Concat(Enumerable.Repeat("NWS source ", 30)),
        };

        var without = Words(Render(workspace, template, null).PdfBytes!);
        var with = Words(Render(workspace, template, unfittable).PdfBytes!);

        Assert.Empty(Added(without, with));
        Assert.Equal(without.Count, with.Count);
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    public void AnotherDatesOutlook_IsIgnored_AndTheBytesEqualTheWeatherFreeDocument(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var wrongDate = PdfWeatherFixtures.Normal(workspace.OperatingDate.AddDays(1));

        var model = AssociatePassdownDocumentModelBuilder.Build(workspace, GeneratedAt, wrongDate);
        Assert.Null(model.Weather);

        var without = Render(workspace, template, null).PdfBytes!;
        var with = Render(workspace, template, wrongDate).PdfBytes!;
        Assert.Equal(PdfInspector.WithoutGenerationTimestamps(without), PdfInspector.WithoutGenerationTimestamps(with));
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    public void SameOutlook_RendersIdenticalBytes(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var first = Render(workspace, template, PdfWeatherFixtures.RainLater(workspace.OperatingDate)).PdfBytes!;
        var second = Render(workspace, template, PdfWeatherFixtures.RainLater(workspace.OperatingDate)).PdfBytes!;

        Assert.Equal(PdfInspector.WithoutGenerationTimestamps(first), PdfInspector.WithoutGenerationTimestamps(second));
    }

    [Theory]
    [InlineData(AssociatePassdownTemplate.Classic)]
    [InlineData(AssociatePassdownTemplate.Modern)]
    [InlineData(AssociatePassdownTemplate.Compact)]
    public void Icons_AreVectors_NoImagesNoNewFonts_AndTheFileStaysSmall(AssociatePassdownTemplate template)
    {
        var workspace = AssociateWorkspaceFixtures.NormalDay();
        var without = Render(workspace, template, null).PdfBytes!;
        var with = Render(workspace, template, PdfWeatherFixtures.RainLater(workspace.OperatingDate)).PdfBytes!;

        using var plain = PdfDocument.Open(without);
        using var weathered = PdfDocument.Open(with);
        Assert.Empty(weathered.GetPage(1).GetImages());

        var plainFonts = plain.GetPage(1).Letters.Select(letter => letter.FontName).ToHashSet(StringComparer.Ordinal);
        var weatherFonts = weathered.GetPage(1).Letters.Select(letter => letter.FontName).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(plainFonts.Select(BaseFont).ToHashSet(), weatherFonts.Select(BaseFont).ToHashSet());

        Assert.True(with.Length - without.Length < 24 * 1024, $"{template}: the outlook added {with.Length - without.Length} bytes.");
    }

    /// <summary>"ABCDEF+Lato-Bold" → "Lato-Bold": subset prefixes differ when the subset's glyphs differ.</summary>
    private static string BaseFont(string? name) => name is null ? string.Empty : name.Contains('+', StringComparison.Ordinal) ? name[(name.IndexOf('+', StringComparison.Ordinal) + 1)..] : name;

    [Fact]
    public void IconSvgs_UseOnlyTheInkColourAndWhiteMasks_ForGrayscalePrinting()
    {
        foreach (var icon in Enum.GetValues<PdfWeatherIcon>().Where(icon => icon != PdfWeatherIcon.None))
        {
            var svg = PdfWeatherIcons.Svg(icon, "#111111");
            var colours = System.Text.RegularExpressions.Regex.Matches(svg, "#[0-9A-Fa-f]{6}").Select(match => match.Value.ToUpperInvariant()).Distinct().ToArray();

            Assert.All(colours, colour => Assert.Contains(colour, new[] { "#111111", "#FFFFFF" }));
            Assert.Contains("stroke-width=\"1.8\"", svg, StringComparison.Ordinal);
            Assert.DoesNotContain("<image", svg, StringComparison.Ordinal);
            Assert.DoesNotContain("<text", svg, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WeatherTextColours_AreNeutralGrays()
    {
        // The templates' own ink and muted colours: no hue, so the outlook reads the same printed in grayscale.
        foreach (var file in new[] { "Classic", "Modern", "Compact" })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Carowinds.Passdown.Documents", "Associate", "Templates", $"{file}AssociatePassdownTemplateRenderer.cs"));
            var style = source[source.IndexOf("new(LabelSize:", StringComparison.Ordinal)..];
            style = style[..style.IndexOf(';', StringComparison.Ordinal)];
            Assert.DoesNotContain("AccentColor", style, StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>Whitespace-insensitive containment for extracted PDF text (PdfPig drops or inserts spaces between spans).</summary>
internal static class PdfTextAssertWeather
{
    public static void Contains(string expected, string actual) =>
        Assert.True(Normalize(actual).Contains(Normalize(expected), StringComparison.Ordinal), $"Expected '{expected}' in the PDF text.");

    private static string Normalize(string value) => new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
}
