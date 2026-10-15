using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Changes;

/// <summary>What kind of locally stored data just changed (Phase 19A local propagation).</summary>
public enum DataChangeKind
{
    /// <summary>An Associate draft was saved (projected attendance, pre-solds, gate overrides, notes, products…).</summary>
    AssociateDraft,

    /// <summary>A monthly Early Ride Time configuration or a date override was saved or removed.</summary>
    EarlyRideTime,

    /// <summary>A persistent Operational Note was created, edited, resolved, or deleted.</summary>
    OperationalNotes,

    /// <summary>Personal Sync imported or resolved another device's version into the local database.</summary>
    PersonalSync,

    /// <summary>An online refresh (automatic, Check Online, or Deep Refresh) finished writing cached official data.</summary>
    OfficialData,

    /// <summary>The Eastern operating day rolled over while the app stayed open, so "today" now means a different date.</summary>
    OperatingDay,

    /// <summary>A passdown was finalized into History by the app's startup catch-up.</summary>
    FinalizedHistory,
}

/// <summary>
/// One local data change. <paramref name="OperatingDate"/> is the single date it affects when that is
/// known (an Associate draft save, an ERT date override, a date-specific online check), or
/// <see langword="null"/> when it may affect any date (a monthly ERT save, a note, a sync import, a
/// comprehensive refresh).
/// </summary>
public sealed record DataChange(DataChangeKind Kind, DateOnly? OperatingDate = null)
{
    /// <summary>True when this change could affect what is shown for <paramref name="date"/>.</summary>
    public bool MayAffect(DateOnly date) => OperatingDate is not { } changed || changed == date;
}

/// <summary>
/// Phase 19A: the app's one local invalidation signal. Published only after the change is already
/// committed to the local database, so a subscriber can simply re-read local state. Purely local —
/// nothing here, and nothing a subscriber is expected to do in response, ever reaches the network:
/// a local edit invalidates local projections, it never starts an online check. Handlers may run on
/// a background thread and must marshal to the UI thread themselves.
/// </summary>
public interface IDataChangeNotifier
{
    event EventHandler<DataChange>? Changed;

    void Publish(DataChange change);
}

/// <summary>
/// <see cref="IDataChangeNotifier"/> that also forwards Personal Sync's own
/// <see cref="ISyncLocalChangeNotifier.LocalDataChanged"/> as a <see cref="DataChangeKind.PersonalSync"/>
/// change, so a synced change reaches every subscriber through this same pathway rather than a
/// second, screen-specific one. A subscriber's exception never prevents other subscribers from
/// being notified, and never propagates back into the code that saved the data.
/// </summary>
public sealed class DataChangeNotifier : IDataChangeNotifier
{
    public DataChangeNotifier(ISyncLocalChangeNotifier? syncLocalChangeNotifier = null)
    {
        if (syncLocalChangeNotifier is not null)
        {
            syncLocalChangeNotifier.LocalDataChanged += (_, _) => Publish(new DataChange(DataChangeKind.PersonalSync));
        }
    }

    public event EventHandler<DataChange>? Changed;

    public void Publish(DataChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<DataChange>>())
        {
            try
            {
                handler(this, change);
            }
            catch (Exception)
            {
                // A notification is a convenience: the data is already saved. A broken subscriber must
                // never turn a successful save into a reported failure.
            }
        }
    }
}
