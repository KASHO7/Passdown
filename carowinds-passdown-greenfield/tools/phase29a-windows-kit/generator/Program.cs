using System.Text;
using System.Text.RegularExpressions;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.MorningMessage;
using Carowinds.Passdown.Documents.Teams;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Ert;
using Carowinds.Passdown.Domain.Hours;

// Phase 29A: renders the SYNTHETIC Windows-kit message through the real Phase 29 composer and checks it.
// Inputs: ../synthetic-input (the same files the tester pastes on Windows). Outputs: ../expected.
var kitDir = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var outDir = Path.Combine(kitDir, "expected");
Directory.CreateDirectory(outDir);
string Input(string name) => File.ReadAllText(Path.Combine(kitDir, "synthetic-input", name)).Replace("\r\n", "\n").TrimEnd('\n');
var date = new DateOnly(2026, 10, 22);
var now = new DateTimeOffset(2026, 10, 22, 11, 0, 0, TimeSpan.Zero);
var verified = new AssociateVerificationInfo(AssociateVerificationState.Verified, now, null, null);
var stale = new AssociateVerificationInfo(AssociateVerificationState.Stale, new DateTimeOffset(2026, 10, 19, 14, 0, 0, TimeSpan.Zero), TimeSpan.FromDays(3), null);

AssociateProduct P(string name, SourceCategory c, decimal price, AssociateVerificationInfo v) => new(
    $"synthetic|{c}|{name}", name, c, IsCustom: false, price, null, null, null, true, date, v, true, null, PriceKind.ExactDated);

var failures = new List<string>();
void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {what}"); if (!ok) failures.Add(what); }

foreach (var (label, fastLane) in new[] { ("verified", verified), ("stale", stale) })
{
    var products = new List<AssociateProduct>
    {
        P("SYNTHETIC Single Day Ticket", SourceCategory.DatedAdmissionTickets, 11.11m, verified),
        P("SYNTHETIC Fast Lane", SourceCategory.DatedFastLane, 22.22m, fastLane),
        P("SYNTHETIC Promo Offer", SourceCategory.Promotions, 33.33m, verified),
    };
    var effective = ParkHoursCalculator.Calculate(false, new TimeOnly(10, 0), new TimeOnly(20, 0), false, null, null, false, null, null);
    var draft = AssociateDraftPayload.CreateDefault() with
    {
        ProjectedAttendance = 12345, PreSolds = 2345,
        DailyFeatureType = DailyFeatureType.QuestionOfTheDay,
        DailyFeatureText = Input("06-daily-feature.txt"),
        OperationalNotes = ["SYNTHETIC private passdown note (must not appear)."],
    };
    var ws = new AssociateWorkspace(date, draft,
        new AssociateParkHoursView(effective, verified, false, new TimeOnly(10, 0), new TimeOnly(20, 0), null, null),
        products, [], AssociateSaveStatus.NeverSaved,
        EarlyRideTime: new EffectiveErt(EffectiveErtKind.Configured, EffectiveErtSource.Monthly, new TimeOnly(9, 30), ["Fury 325"], null));

    var prefs = MorningMessagePreferences.Default with { Greeting = Input("01-greeting.txt") };
    var content = MorningMessageContent.Empty with
    {
        DapPlacement = Input("02-dap-placement.txt"),
        Reminders = Input("03-reminders.txt"),
        BreakPlan = Input("05-break-plan.txt"),
        OdNotes = Input("07-od-notes.txt"),
        PriceNote = Input("04-price-note.txt"),
    };

    var comp = MorningMessageComposer.Compose(ws, prefs, content, date, now);
    var plain = TeamsPlainTextRenderer.Render(comp.Message);
    var html = TeamsHtmlRenderer.Render(comp.Message);
    var cf = CfHtml.Build(html);
    var od = TeamsPlainTextRenderer.Render(comp.OdNotes);
    var odHtml = TeamsHtmlRenderer.Render(comp.OdNotes);

    File.WriteAllText(Path.Combine(outDir, $"expected-morning-message-{label}.txt"), plain, new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(outDir, $"expected-morning-message-{label}.fragment.html"), html, new UTF8Encoding(false));
    File.WriteAllBytes(Path.Combine(outDir, $"expected-morning-message-{label}.cfhtml.bin"), cf);
        if (label == "verified")
    {
        File.WriteAllText(Path.Combine(outDir, "expected-od-notes.txt"), od, new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(outDir, "expected-od-notes.cfhtml.bin"), CfHtml.Build(odHtml));
    }
    File.WriteAllText(Path.Combine(outDir, $"preview-notices-{label}.txt"),
        string.Join("\r\n", comp.Preview.SelectMany(s => s.Notices.Select(n => $"{s.Id}: {n}")).Concat(comp.AfterCopyReminders)), new UTF8Encoding(false));

    Console.WriteLine($"--- {label}");
    Check(plain.StartsWith("Gooooood Morning!", StringComparison.Ordinal), "greeting first");
    Check(plain.TrimEnd().EndsWith("why?", StringComparison.Ordinal), "Daily Feature last");
    Check(!plain.Contains("OD Notes") && !plain.Contains("SYNTHETIC OD note") && !html.Contains("SYNTHETIC OD note"), "OD Notes absent from main message");
    Check(od.StartsWith("OD Notes:\r\n", StringComparison.Ordinal) && !odHtml.Contains("<script"), "OD Notes separate message");
    Check(!plain.Contains("private passdown note"), "operational notes never auto-included");
    Check(!Regex.IsMatch(plain, "</?(p|br|b|i|strong|em|ul|li|a)[ >]") && !plain.Contains("&amp;") && !plain.Contains("&nbsp;"), "plain text has no generated markup or entities");
    Check(plain.Contains('—') && plain.Contains("Café"), "Unicode (em dash, é) kept in plain");
    var parsed = CfHtml.Parse(cf);
    Check(parsed.Fragment == html && parsed.Version == "0.9", "CF_HTML offsets delimit exactly the fragment");
    Check(Encoding.UTF8.GetString(cf).Contains("—") && cf.Length == parsed.EndHtml && !(cf.Length >= 3 && cf[0] == 0xEF), "CF_HTML UTF-8, no BOM, EndHTML = length");
    var tags = Regex.Matches(html, "</?([a-zA-Z]+)").Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();
    Check(tags.All(t => t is "p" or "br" or "b" or "i" or "ul" or "li" or "a"), "only p/br/b/i/ul/li/a: " + string.Join(",", tags));
    var styles = Regex.Matches(html, "style\\s*=\\s*\"[^\"]*\"").Select(m => m.Value).Distinct().ToList();
    Check(styles.All(v => v is "style=\"font-weight:bold\"" or "style=\"font-style:italic\""), "only fixed bold/italic styles: " + string.Join(",", styles));
    Check(!html.Contains("javascript:", StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(html, "href=\"javascript", RegexOptions.IgnoreCase), "javascript: never a link");
    Check(html.Contains("href=\"https://www.carowinds.com/\""), "https link is a link");
    Check(html.Contains("&lt;before&gt;") && html.Contains("&amp;"), "user text HTML-encoded");
    Check(html.Contains("<ul>") && html.Contains("<b style=\"font-weight:bold\">Today&#39;s Operations:</b>"), "bullets and explicitly bold headings present");
    Check(plain.Contains("\r\nToday's Operations:\r\n") && !plain.Contains("Operations for") && !html.Contains("Operations for"), "undated Today's Operations heading");
    Check(!html.Contains("<at") && plain.Contains("@Jordan Testperson"), "@Name stays plain text");
    Check(!html.Contains("<img") && !plain.Contains("data:image"), "no image in text");
    var order = new[] { "Gooooood Morning!", "Operations", "DAP / placement", "Reminders:", "Ticket Prices:", "Tentative Break Plan:", "Question of the Day:" }
        .Select(s => plain.IndexOf(s, StringComparison.Ordinal)).ToList();
    Check(order.All(i => i >= 0) && order.SequenceEqual(order.Order()), "default section order: " + string.Join(",", order));
    Check(plain.Contains("$11.11") && plain.Contains("$22.22") && plain.Contains("$33.33"), "synthetic prices present");
    var staleLine = plain.Contains("Some prices could not be re-verified today");
    Check(label == "stale" ? staleLine : !staleLine, "stale notice only when a price is stale");
}
Console.WriteLine(failures.Count == 0 ? "ALL CHECKS PASSED" : $"{failures.Count} FAILED");
return failures.Count == 0 ? 0 : 1;
