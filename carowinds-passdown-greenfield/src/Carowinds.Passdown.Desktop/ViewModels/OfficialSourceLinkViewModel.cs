using Carowinds.Passdown.Application.Links;
using Carowinds.Passdown.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// Phase 26: one "View Official … Page" action for an official pricing source. Built only from the
/// source URL the app actually verified (the product's last official evidence), and only when that
/// address passes <see cref="OfficialSourceLinkPolicy"/>. Opening it changes nothing — no draft, no
/// price observation, no retrieval, no Personal Sync — and a failure shows a plain-language message.
/// </summary>
public sealed partial class OfficialSourceLinkViewModel : ObservableObject
{
    public const string MembershipsAndSeasonPassesLabel = "View Official Memberships & Season Passes Page";
    public const string GenericLabel = "View Official Page";

    private readonly IExternalLinkLauncher _launcher;

    private OfficialSourceLinkViewModel(Uri address, string label, IExternalLinkLauncher launcher)
    {
        Address = address;
        Label = label;
        _launcher = launcher;
    }

    /// <summary>The validated official address (never shown or opened unless it passed the allowlist).</summary>
    public Uri Address { get; }

    public string Label { get; }

    public string AddressText => OfficialSourceLinkPolicy.DisplayAddress(Address);

    public string HelpText => $"Opens {AddressText} in your default web browser.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; private set; }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    /// <summary>A link for <paramref name="sourceUrl"/>, or <see langword="null"/> when there is none or it is not an allowed official page.</summary>
    public static OfficialSourceLinkViewModel? TryCreate(Uri? sourceUrl, IExternalLinkLauncher? launcher)
    {
        if (launcher is null || !OfficialSourceLinkPolicy.TryValidate(sourceUrl, out var safe))
        {
            return null;
        }

        var label = safe.AbsolutePath.Contains("memberships-and-season-passes", StringComparison.OrdinalIgnoreCase)
            ? MembershipsAndSeasonPassesLabel
            : GenericLabel;
        return new OfficialSourceLinkViewModel(safe, label, launcher);
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        bool opened;
        try
        {
            opened = await _launcher.OpenAsync(Address);
        }
        catch (Exception)
        {
            opened = false; // any launcher failure is reported the same plain way; nothing else is affected
        }

        Message = opened
            ? null
            : $"The official page couldn't be opened. Open your web browser and go to {AddressText}.";
    }
}
