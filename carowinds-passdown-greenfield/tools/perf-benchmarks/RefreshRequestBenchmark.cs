using System.Net;
using System.Text.Json;
using Carowinds.Passdown.Application.Retrieval;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Infrastructure.Retrieval.Http;
using Carowinds.Passdown.Infrastructure.Retrieval.Sources;
using Carowinds.Passdown.Ui.Tests.TestSupport;

namespace PerfBenchmarks;

/// <summary>
/// Counts the official POSTs one 7-day rolling-window refresh sends through the admission and Fast Lane
/// sources, answered from the committed, sanitized fixtures (no network). Counts are deterministic.
/// </summary>
internal static class RefreshRequestBenchmark
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private sealed class CountingClient(Func<string, RetrievalHttpResponse> respond) : IRetrievalHttpClient
    {
        public int Posts;

        public Task<RetrievalHttpResponse> GetAsync(Uri requestUri, CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task<RetrievalHttpResponse> PostJsonAsync(Uri requestUri, string jsonBody, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Posts);
            return Task.FromResult(respond(jsonBody));
        }
    }

    public static async Task<int> RunAsync()
    {
        Console.WriteLine();
        Console.WriteLine("## refresh-requests — official POSTs for one 7-day rolling window (fixtures, no network)");
        var fixtures = FindFixtures();
        RetrievalHttpResponse Fixture(string path) => new(HttpStatusCode.OK, "application/json", File.ReadAllText(Path.Combine(fixtures, path)));
        var admission = Fixture("dated-admission/published-window.json");
        var noDates = Fixture("dated-fast-lane/no-dates-found.json");
        var horizon = Fixture("dated-fast-lane/horizon-window.json");
        RetrievalHttpResponse Respond(string body)
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(body)!;
            return fields["keyword"] == "SnapWizardDailyTickets" ? admission : fields["start_date"] == fields["end_date"] ? noDates : horizon;
        }

        IClock clock = new FakeClock(Now);
        var admissionDates = Enumerable.Range(0, 7).Select(i => new DateOnly(2026, 8, 20).AddDays(i)).ToArray();
        var gapDates = Enumerable.Range(22, 7).Select(day => new DateOnly(2026, 8, day)).ToArray(); // Fast Lane closed/gap dates

        foreach (var coalesce in new[] { false, true })
        {
            var inner = new CountingClient(Respond);
            IRetrievalHttpClient client = coalesce ? new CoalescingRetrievalHttpClient(inner, clock, AccessoKeywordCalendarCoalescing.KeyBody) : inner;
            var admissionSource = new DatedAdmissionSource(client, clock);
            var fastLane = new DatedFastLaneSource(client, clock);

            foreach (var date in admissionDates)
            {
                await admissionSource.RetrieveAsync(new RetrievalRequest(admissionSource.SourceId, SourceCategory.DatedAdmissionTickets, date), CancellationToken.None);
            }

            var afterAdmission = inner.Posts;
            foreach (var date in gapDates)
            {
                await fastLane.RetrieveAsync(new RetrievalRequest(fastLane.SourceId, SourceCategory.DatedFastLane, date), CancellationToken.None);
            }

            Console.WriteLine($"  {(coalesce ? "coalesced (34B-2)" : "direct (RC.12)  ")}  admission 7 dates: {afterAdmission} POSTs | Fast Lane 7 gap dates: {inner.Posts - afterAdmission} POSTs | total {inner.Posts}");
        }

        return 0;
    }

    private static string FindFixtures()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CarowindsPassdown.slnx")))
            {
                return Path.Combine(directory.FullName, "fixtures");
            }
        }

        throw new DirectoryNotFoundException("Run from inside the repository so the committed fixtures can be found.");
    }
}
