using System.Globalization;

/// <summary>
/// Phase 36B (audit finding TD-03): keeps one harness process bounded. Every capture realizes a full app window and the
/// process retains roughly 20–25 MB per capture (Phase 35G: ≈5.8 GB after 223 captures; Phase 36A: 324 MB for 4
/// captures, 594 MB for 16). A run therefore refuses to start when it would take more captures than
/// <see cref="MaxCaptures"/>, and stops — writing the geometry report gathered so far — if the process working set
/// passes <see cref="MaxWorkingSetMegabytes"/> (the 3 GB test-process ceiling used by the build-and-test skill).
/// Large reviews run one process per scenario (README.md). Both limits can be changed for one run with environment
/// variables; nothing here affects the rendered images.
/// </summary>
internal sealed record CaptureBudget(int MaxCaptures, int MaxWorkingSetMegabytes)
{
    public const string MaxCapturesVariable = "CAROWINDS_VISUAL_FIXTURES_MAX_CAPTURES";
    public const string MaxWorkingSetVariable = "CAROWINDS_VISUAL_FIXTURES_MAX_WORKING_SET_MB";
    public const int DefaultMaxCaptures = 48;
    public const int DefaultMaxWorkingSetMegabytes = 3072;

    public static bool TryLoad(out CaptureBudget budget, out string? error)
    {
        budget = new CaptureBudget(DefaultMaxCaptures, DefaultMaxWorkingSetMegabytes);
        if (!TryRead(MaxCapturesVariable, DefaultMaxCaptures, out var captures, out error)
            || !TryRead(MaxWorkingSetVariable, DefaultMaxWorkingSetMegabytes, out var workingSet, out error))
        {
            return false;
        }

        budget = new CaptureBudget(captures, workingSet);
        return true;
    }

    public static long CurrentWorkingSetMegabytes() => Environment.WorkingSet / (1024 * 1024);

    public bool IsWorkingSetExceeded(out long workingSetMegabytes)
    {
        workingSetMegabytes = CurrentWorkingSetMegabytes();
        return workingSetMegabytes > MaxWorkingSetMegabytes;
    }

    public string TooManyCapturesMessage(int planned) =>
        $"visual-fixtures: this run would take {planned} captures in one process; the limit is {MaxCaptures} " +
        $"(each capture keeps about 20–25 MB until the process exits). Narrow the scenario filter or sizes, or run one " +
        $"process per scenario (README.md \"Memory\"). To accept the memory cost for one run, set {MaxCapturesVariable}.";

    public string WorkingSetExceededMessage(long workingSetMegabytes, int completed, int planned) =>
        $"visual-fixtures: stopped after {completed} of {planned} captures — working set {workingSetMegabytes} MB passed the " +
        $"{MaxWorkingSetMegabytes} MB limit ({MaxWorkingSetVariable}). The geometry report so far was written; continue " +
        "with one process per scenario.";

    private static bool TryRead(string variable, int fallback, out int value, out string? error)
    {
        error = null;
        var text = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(text))
        {
            value = fallback;
            return true;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0)
        {
            return true;
        }

        error = $"visual-fixtures: {variable} must be a positive whole number (got \"{text}\").";
        return false;
    }
}
