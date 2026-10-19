using Carowinds.Passdown.Application.Preferences;
using Carowinds.Passdown.Desktop.Services;

namespace Carowinds.Passdown.Ui.Tests.TestSupport;

/// <summary>Phase 23C: in-memory appearance preference — never the real profile's appearance.json.</summary>
public sealed class FakeAppearancePreferenceStore : IAppearancePreferenceStore
{
    public AppearancePreference Stored { get; set; } = AppearancePreference.System;

    public int LoadCount { get; private set; }

    public List<AppearancePreference> Saves { get; } = [];

    /// <summary>When set, <see cref="SaveAsync"/> throws it (a read-only or full disk).</summary>
    public Exception? SaveException { get; set; }

    /// <summary>When set, each save waits for it — lets a test hold saves in flight to prove ordering.</summary>
    public TaskCompletionSource<bool>? SaveGate { get; set; }

    public AppearancePreference Load()
    {
        LoadCount++;
        return Stored;
    }

    public async Task SaveAsync(AppearancePreference preference, CancellationToken cancellationToken = default)
    {
        if (SaveGate is { } gate)
        {
            await gate.Task.ConfigureAwait(true);
        }

        if (SaveException is not null)
        {
            throw SaveException;
        }

        Saves.Add(preference);
        Stored = preference;
    }
}

/// <summary>Phase 23C: a simulated operating-system light/dark setting, so System mode is deterministic on any test machine.</summary>
public sealed class FakeSystemAppearanceSource(EffectiveAppearance initial = EffectiveAppearance.Light) : ISystemAppearanceSource
{
    public EffectiveAppearance Current { get; private set; } = initial;

    public event EventHandler? Changed;

    public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>Simulates the user switching the operating system's appearance while the app is open.</summary>
    public void Switch(EffectiveAppearance appearance)
    {
        Current = appearance;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Phase 23C: records every palette the controller asked to show, without touching the running application.</summary>
public sealed class RecordingThemeVariantTarget : IThemeVariantTarget
{
    public List<EffectiveAppearance> Applied { get; } = [];

    public EffectiveAppearance? Last => Applied.Count == 0 ? null : Applied[^1];

    public void Apply(EffectiveAppearance appearance) => Applied.Add(appearance);
}
