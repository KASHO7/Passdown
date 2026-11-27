namespace Carowinds.Passdown.Domain.Weather;

/// <summary>
/// Pure rules for official NWS alerts (spec §6.6): which messages are real, when one is active, how Updates
/// and Cancels replace earlier messages, and the display order. Applicability is NWS's own: the alerts come
/// from point queries, and nothing here widens them to a state or county.
/// </summary>
public static class WeatherAlertRules
{
    /// <summary>When the alert stops applying: <c>ends</c> (the hazard's end) or, without it, <c>expires</c> (the message's end).</summary>
    public static DateTimeOffset? EndsAt(WeatherAlert alert) => alert.Ends ?? alert.Expires;

    /// <summary>
    /// True only for a real (<c>Actual</c>) Alert or Update that has started and has not ended. A message with
    /// neither <c>ends</c> nor <c>expires</c> is never active, because the application cannot know when it ends.
    /// </summary>
    public static bool IsActiveAt(WeatherAlert alert, DateTimeOffset now)
    {
        if (!string.Equals(alert.Status, "Actual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(alert.MessageType, "Cancel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var endsAt = EndsAt(alert);
        if (endsAt is null || now >= endsAt.Value)
        {
            return false;
        }

        var startsAt = alert.Effective ?? alert.Sent;
        return startsAt is null || startsAt.Value <= now;
    }

    /// <summary>
    /// The active alerts among <paramref name="alerts"/> (which may repeat across reference points): one per
    /// id, without any message an Update or Cancel references, sorted by severity, urgency and onset.
    /// </summary>
    public static IReadOnlyList<WeatherAlert> ResolveActive(IEnumerable<WeatherAlert> alerts, DateTimeOffset now)
    {
        var unique = new Dictionary<string, WeatherAlert>(StringComparer.Ordinal);
        foreach (var alert in alerts)
        {
            unique.TryAdd(alert.Id, alert);
        }

        var replaced = new HashSet<string>(unique.Values.SelectMany(alert => alert.ReferenceIds), StringComparer.Ordinal);

        return unique.Values
            .Where(alert => !replaced.Contains(alert.Id) && IsActiveAt(alert, now))
            .OrderBy(alert => SeverityRank(alert.Severity))
            .ThenBy(alert => UrgencyRank(alert.Urgency))
            .ThenBy(alert => alert.Onset ?? alert.Effective ?? alert.Sent ?? DateTimeOffset.MaxValue)
            .ThenBy(alert => alert.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Extreme first, Unknown and unrecognized values last.</summary>
    public static int SeverityRank(string? severity) => severity?.ToUpperInvariant() switch
    {
        "EXTREME" => 0,
        "SEVERE" => 1,
        "MODERATE" => 2,
        "MINOR" => 3,
        _ => 4,
    };

    /// <summary>Immediate first, Unknown and unrecognized values last.</summary>
    public static int UrgencyRank(string? urgency) => urgency?.ToUpperInvariant() switch
    {
        "IMMEDIATE" => 0,
        "EXPECTED" => 1,
        "FUTURE" => 2,
        "PAST" => 3,
        _ => 4,
    };
}
