using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.ProductDetails;
using Carowinds.Passdown.Desktop.Services;
using Carowinds.Passdown.Desktop.Presentation;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Carowinds.Passdown.Desktop.ViewModels;

/// <summary>
/// One editable official-product row (spec Phase 4D Parts C/D/E). Wraps an Application-composed
/// <see cref="AssociateProduct"/> with the two things an associate may change about it — whether it
/// prints on the PDF and an optional draft-local display-price override — without ever mutating the
/// official source-backed fields (<see cref="OfficialPriceText"/>, <see cref="ComparisonPriceText"/>,
/// <see cref="PriceQualifier"/>) it was built from.
///
/// Built fresh only when <see cref="AssociateViewModel"/> rebuilds the product list from a newly
/// (re)composed workspace (a load, date switch, or Check Online); every other user edit mutates this
/// same instance in place, mirroring how the parent's own scalar draft fields work, so an
/// in-progress edit or focused control is never reset out from under the associate.
/// </summary>
public partial class ProductRowViewModel : ObservableObject, IPriceEditTarget
{
    private readonly Action _notifyChanged;
    private readonly Action<string>? _openPriceHistory;
    private readonly Action<OfficialDetailsPresentation>? _openDetails;
    private readonly Action<IPriceEditTarget>? _openPriceEditor;
    private decimal? _displayPriceOverride;

    public string ProductIdentity { get; }

    public string DisplayName { get; }

    /// <summary>
    /// Phase 22E: "$59.00" for an exact/catalog price, "From $40.00" for an advertised starting price —
    /// the same <see cref="AdvertisedPriceText"/> rule the PDFs and Teams Copy print, decided by the
    /// row's <see cref="PriceKind"/> evidence only (never by the mere presence of an amount).
    /// </summary>
    public string OfficialPriceText { get; }

    /// <summary>Phase 22E: the official evidence for this row's amount (exact/dated, starting-from, catalog, sale, bundle).</summary>
    public PriceKind PriceKind { get; }

    /// <summary>Phase 22E: the official amount is an advertised minimum ("From $x"), not an exact price for the date.</summary>
    public bool IsStartingPrice => PriceKind == PriceKind.StartingFrom;

    public string? ComparisonPriceText { get; }

    /// <summary>The "Compare at $x" line — every product except a pass offer (Phase 25), whose comparison reads as <see cref="ReferencePriceNoteText"/>.</summary>
    public bool HasCompareAtLine => ComparisonPriceText is not null && ReferencePriceNoteText is null;

    /// <summary>
    /// Phase 25: a pass offer's crossed-out amount, labeled as a reference price ("Reference price
    /// $115.00") — never as a current sale price.
    /// </summary>
    public string? ReferencePriceNoteText { get; }

    public bool HasReferencePriceNote => ReferencePriceNoteText is not null;

    /// <summary>Phase 25: "Renewal offer $89.00" on a Season Pass row — a separate published price for renewing passholders.</summary>
    public string? RenewalNoteText { get; }

    public bool HasRenewalNote => RenewalNoteText is not null;

    /// <summary>Phase 25: "$20.00 initiation fee" on a Membership row — never added to the monthly rate.</summary>
    public string? InitiationFeeNoteText { get; }

    public bool HasInitiationFeeNote => InitiationFeeNoteText is not null;

    /// <summary>
    /// Phase 25: whether this row can print on the passdown (and so offers the PDF checkbox and a display
    /// price override). False for Memberships, unfolded renewal/initiation rows and unavailable options.
    /// </summary>
    public bool CanIncludeOnPdf { get; }

    /// <summary>
    /// Phase 25: the muted line a non-printable row shows instead of the PDF controls. Membership rows
    /// carry none — their whole group says it once (<see cref="Presentation.PassSourcePresentation"/>).
    /// </summary>
    public string? NotPrintedNoteText => CanIncludeOnPdf || _isMembership ? null : "Not printed on the passdown.";

    public bool HasNotPrintedNote => NotPrintedNoteText is not null;

    private readonly bool _isMembership;

    public string? PriceQualifier { get; }

    /// <summary>
    /// The qualifier line under the name. Phase 22E: a "Starting From" qualifier on a starting-price row is
    /// now carried by the price itself ("From $40.00", as on the PDFs), so it is not repeated; every
    /// other qualifier ("Valid ID required", "Sale Price", …) still shows.
    /// </summary>
    public bool HasPriceQualifier => !string.IsNullOrWhiteSpace(PriceQualifier)
        && !(IsStartingPrice && string.Equals(PriceQualifier.Trim(), StartingFromQualifier, StringComparison.OrdinalIgnoreCase));

    private const string StartingFromQualifier = "Starting From";

    /// <summary>Phase 13C Part W: a source-described bundle's per-person breakdown (e.g. "2 for $50.00 — $25.00 per person"), derived and clearly labeled — never replacing <see cref="OfficialPriceText"/> (the bundle total).</summary>
    public string? BundleNoteText { get; }

    public bool HasBundleNote => !string.IsNullOrWhiteSpace(BundleNoteText);

    /// <summary>Only set for the states worth flagging on a per-row basis (stale, error and, pre-RC.15, previously verified); every other state is already covered by the category-level status banner or is unremarkable (spec Part C).</summary>
    public string? VerificationNoteText { get; }

    public string OverridePlaceholderText { get; }

    /// <summary>Phase 20A: the catalog key whose season price history this row can show; null for non-tracked products.</summary>
    public string? PriceHistoryProductKey { get; }

    /// <summary>Only tracked Season Passes / Season Pass Add-Ons expose "Price history" (never an empty button on daily products).</summary>
    public bool HasPriceHistory => PriceHistoryProductKey is not null && _openPriceHistory is not null;

    [ObservableProperty]
    public partial bool IncludeOnPdf { get; set; }

    [ObservableProperty]
    public partial string? DisplayPriceOverrideText { get; set; }

    [ObservableProperty]
    public partial string? DisplayPriceOverrideError { get; set; }

    /// <summary>The last valid parsed override (spec Part E: invalid text never overwrites this).</summary>
    public decimal? DisplayPriceOverride => _displayPriceOverride;

    public bool HasOverride => _displayPriceOverride is not null;

    /// <summary>
    /// The price the passdown prints: the override when present, else the official price — with the same
    /// evidence label the PDF gives it (a starting-price row prints "From $x" even with an override,
    /// because the document model keeps the row's <see cref="PriceKind"/>).
    /// </summary>
    public string EffectivePriceText => AdvertisedPriceText.FormatOrNull(_displayPriceOverride, PriceKind, _priceUnit) ?? OfficialPriceText;

    /// <summary>Phase 25: how a pass option the source lists as unavailable reads — never "$0.00".</summary>
    public const string UnavailableText = "Not available";

    private readonly PassPriceUnit _priceUnit;

    /// <summary>Phase 22E: under an override on a starting-price row, says how it prints ("Prints as From $35.00"); null otherwise.</summary>
    public string? OverridePrintNoteText => IsStartingPrice && HasOverride ? $"Prints as {EffectivePriceText}" : null;

    public bool HasOverridePrintNote => OverridePrintNoteText is not null;

    /// <summary>The composed product this row was built from (read-only; never mutated).</summary>
    internal AssociateProduct Product { get; }

    /// <summary>
    /// Phase 31B: the row's expandable "Important Details &amp; Deadlines" (official terms, purchase and
    /// redemption deadlines, eligibility, fees), or null when no official details match this product.
    /// Display only — never printed, never part of the draft.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportantDetails))]
    public partial ProductImportantDetailsPresentation? ImportantDetails { get; private set; }

    public bool HasImportantDetails => ImportantDetails is not null;

    /// <summary>Phase 31B: the allowlisted official page the details were verified on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImportantDetailsSourceLink))]
    public partial OfficialSourceLinkViewModel? ImportantDetailsSourceLink { get; private set; }

    public bool HasImportantDetailsSourceLink => ImportantDetailsSourceLink is not null;

    /// <summary>Phase 31B: applies (or clears) the official details for this row; changes nothing else.</summary>
    internal void ApplyImportantDetails(ProductImportantDetails? details, IExternalLinkLauncher? linkLauncher)
    {
        ImportantDetails = details is null ? null : ProductImportantDetailsPresentation.Build(details);
        ImportantDetailsSourceLink = details is null ? null : OfficialSourceLinkViewModel.TryCreate(details.Source.PrimarySourceUrl, linkLauncher);
        OnPropertyChanged(nameof(CompactDeadlineText));
        OnPropertyChanged(nameof(HasCompactDeadline));
        OnPropertyChanged(nameof(HasViewDetails));
    }

    /// <summary>Phase 31C: the section heading this row is listed under (used as the dialog's subtitle).</summary>
    internal string SectionName { get; set; } = string.Empty;

    /// <summary>Phase 31C: a confirmed purchase deadline short enough for the row, or null.</summary>
    public string? CompactDeadlineText => ImportantDetails?.CompactDeadlineText;

    public bool HasCompactDeadline => CompactDeadlineText is not null;

    /// <summary>
    /// Phase 31C: the small "View details" link — on every row with official Important Details, and on
    /// the pass, membership, military/first responder, promotional, seasonal and add-on rows (which then
    /// say plainly when no further terms are recorded). Dated admission and Fast Lane rows have none.
    /// </summary>
    public bool HasViewDetails => _openDetails is not null && (HasImportantDetails || IsDetailsCategory(Product));

    public string ViewDetailsAutomationName => $"View details for {DisplayName}";

    private static bool IsDetailsCategory(AssociateProduct product) => !product.IsCustom && product.Category is
        SourceCategory.SeasonPasses or SourceCategory.AddOns or SourceCategory.Promotions or SourceCategory.HauntedAttractions or
        SourceCategory.MilitaryFirstResponder or SourceCategory.DatedMilitaryFirstResponder or
        SourceCategory.BringAFriendDuo or SourceCategory.DatedBringAFriend;

    /// <summary>Phase 31C: what the View details dialog shows for this row (built from this row's own values only).</summary>
    public OfficialDetailsPresentation BuildDetails()
    {
        var notes = new[]
        {
            HasCompareAtLine ? $"Compare at {ComparisonPriceText}" : null,
            ReferencePriceNoteText,
            RenewalNoteText,
            InitiationFeeNoteText,
            HasPriceQualifier ? PriceQualifier : null,
            BundleNoteText,
        }.OfType<string>().ToList();

        var verifiedAt = Product.Verification.LastVerifiedAt;
        var verified = verifiedAt is { } at
            ? $"Official price last verified {EasternTimestampFormat.DateYearAndTime(at)}"
            : "Official price not verified yet";

        return OfficialDetailsPresentation.ForProduct(
            DisplayName,
            SectionName,
            OfficialPriceText,
            notes,
            VerificationNoteText,
            verified,
            ImportantDetails,
            Product.OfficialSourceUrl);
    }

    [RelayCommand]
    private void ViewDetails() => _openDetails?.Invoke(BuildDetails());

    public ProductRowViewModel(AssociateProduct product, Action notifyChanged, Action<string>? openPriceHistory = null, Action<OfficialDetailsPresentation>? openDetails = null, Action<IPriceEditTarget>? openPriceEditor = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(notifyChanged);
        Product = product;
        _notifyChanged = notifyChanged;
        _openPriceHistory = openPriceHistory;
        _openDetails = openDetails;
        _openPriceEditor = openPriceEditor;
        PriceHistoryProductKey = product.PriceHistoryProductKey;

        ProductIdentity = product.ProductIdentity;
        DisplayName = product.DisplayName;
        PriceKind = product.PriceKind;
        _priceUnit = product.PriceUnit;
        OfficialPriceText = product.IsUnavailablePassOption
            ? UnavailableText
            : AdvertisedPriceText.FormatOrNull(product.OfficialPriceAmount, product.PriceKind, _priceUnit) ?? product.OfficialDisplayPrice ?? "Not priced";
        ComparisonPriceText = PriceFormatting.FormatOrNull(product.ComparisonPriceAmount);
        ReferencePriceNoteText = ComparisonPriceText is not null && product.PassOffer is not null ? $"Reference price {ComparisonPriceText}" : null;
        RenewalNoteText = product.RenewalPriceAmount is { } renewal ? $"Renewal offer {PriceFormatting.FormatOrNull(renewal)}" : null;
        InitiationFeeNoteText = product.InitiationFeeAmount is { } fee ? $"{PriceFormatting.FormatOrNull(fee)} initiation fee" : null;
        CanIncludeOnPdf = product.IsPassdownExportable && !product.IsUnavailablePassOption;
        _isMembership = product.PassOffer?.Product == PassProductType.Membership;
        PriceQualifier = product.PriceQualifier;
        BundleNoteText = product is { BundleQuantity: { } quantity, PerPersonPriceAmount: { } perPerson }
            ? $"{quantity} for {PriceFormatting.FormatOrNull(product.BundleTotalPriceAmount)} — {PriceFormatting.FormatOrNull(perPerson)} per person"
            : null;
        VerificationNoteText = BuildVerificationNote(product.Verification);
        OverridePlaceholderText = $"Use official ({OfficialPriceText})";

        _displayPriceOverride = product.DisplayPriceOverride;
        IncludeOnPdf = product.IncludeOnPdf;
        DisplayPriceOverrideText = PriceFormatting.FormatOrNull(product.DisplayPriceOverride);
    }

    partial void OnIncludeOnPdfChanged(bool value) => _notifyChanged();

    // ------------------------------------------------------------------ Phase 35C: display price as text + popover

    /// <summary>Phase 35C: whether the row matches the Products search (view state only; true when no search).</summary>
    [ObservableProperty]
    public partial bool IsSearchMatch { get; set; } = true;

    /// <summary>Phase 35C: case-insensitive match on the row's visible words (name, notes, prices, section).</summary>
    internal bool MatchesSearch(string query, string sectionName)
    {
        var needle = query.Trim();
        return new[]
        {
            DisplayName, sectionName, OfficialPriceText, EffectivePriceText, PriceQualifier, ComparisonPriceText,
            ReferencePriceNoteText, RenewalNoteText, InitiationFeeNoteText, BundleNoteText,
        }.Any(text => text?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>Phase 35C: the subdued label under a display price that differs from the official source.</summary>
    public const string ManualOverrideText = "Manual override";

    /// <summary>
    /// Phase 35C: no official value is available for this row on this date (an unavailable pass option, or no
    /// published amount or wording) — the official column reads muted rather than like a price.
    /// </summary>
    public bool HasNoOfficialValue => Product.IsUnavailablePassOption
        || (Product.OfficialPriceAmount is null && string.IsNullOrWhiteSpace(Product.OfficialDisplayPrice));

    /// <summary>Phase 35C: the Edit control's accessible name (the visible control shows the price and a pencil).</summary>
    public string EditPriceAutomationName => $"Edit display price for {DisplayName}, currently {EffectivePriceText}";

    /// <summary>Phase 35C: opens the shared anchored price editor on this row (printable rows only).</summary>
    public bool CanEditPrice => CanIncludeOnPdf && _openPriceEditor is not null;

    [RelayCommand]
    private void EditPrice()
    {
        if (CanEditPrice)
        {
            _openPriceEditor!(this);
        }
    }

    string IPriceEditTarget.PriceEditorName => DisplayName;

    string IPriceEditTarget.PriceEditorInitialText =>
        PriceFormatting.FormatOrNull(_displayPriceOverride ?? Product.OfficialPriceAmount) ?? string.Empty;

    string IPriceEditTarget.PriceEditorOfficialLine => $"Official price: {OfficialPriceText}";

    string? IPriceEditTarget.PriceEditorPrintNote => IsStartingPrice
        ? "Starting price — prints as \u201CFrom\u201D followed by the amount."
        : null;

    bool IPriceEditTarget.PriceEditorCanUseOfficial => HasOverride;

    bool IPriceEditTarget.PriceEditorAllowsBlank => false;

    bool IPriceEditTarget.TryApplyPriceEdit(string? text, out string? error)
    {
        var candidate = text?.Trim();
        if (IsStartingPrice && candidate is not null && candidate.StartsWith("From", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[4..]; // "From $39" on a starting-price row means the amount; "From" is printed anyway.
        }

        if (!PriceFormatting.TryParse(candidate, out var parsed, out error))
        {
            return false; // nothing applied: the row keeps its last valid override (spec Part E).
        }

        if (parsed is null)
        {
            error = PriceEditorViewModel.BlankValueMessage;
            return false;
        }

        if (parsed == (_displayPriceOverride ?? Product.OfficialPriceAmount))
        {
            return true; // unchanged: no override is created, no autosave is scheduled.
        }

        DisplayPriceOverrideText = PriceFormatting.Format(parsed.Value); // the existing override path.
        return true;
    }

    void IPriceEditTarget.UsePriceEditorOfficial() => ResetToOfficialCommand.Execute(null);

    partial void OnDisplayPriceOverrideTextChanged(string? value)
    {
        if (!PriceFormatting.TryParse(value, out var parsed, out var error))
        {
            DisplayPriceOverrideError = error;
            return; // the last valid override is left untouched (spec Part E).
        }

        DisplayPriceOverrideError = null;
        _displayPriceOverride = parsed;
        OnPropertyChanged(nameof(EffectivePriceText));
        OnPropertyChanged(nameof(HasOverride));
        OnPropertyChanged(nameof(OverridePrintNoteText));
        OnPropertyChanged(nameof(HasOverridePrintNote));
        OnPropertyChanged(nameof(EditPriceAutomationName));
        _notifyChanged();
    }

    /// <summary>Clears the override back to "use official price" (spec Part C/E's obvious reset action).</summary>
    [RelayCommand]
    private void ResetToOfficial() => DisplayPriceOverrideText = null;

    /// <summary>Phase 20A: opens the read-only Price History screen for this product. Never edits anything.</summary>
    [RelayCommand]
    private void ViewPriceHistory()
    {
        if (PriceHistoryProductKey is { } key)
        {
            _openPriceHistory?.Invoke(key);
        }
    }

    private static string? BuildVerificationNote(AssociateVerificationInfo verification) => verification.State switch
    {
        AssociateVerificationState.Stale => "Stale — showing the last verified price.",
        // Pre-RC.15: the official source no longer lists this date (its sales window ended or it was withdrawn).
        AssociateVerificationState.PreviouslyVerified => verification.LastVerifiedAt is { } at
            ? $"Last verified {EasternTimestampFormat.DateAndTime(at)} — sales availability not confirmed."
            : "Previously verified — sales availability not confirmed.",
        AssociateVerificationState.Error => "Could not verify current pricing.",
        _ => null,
    };
}
