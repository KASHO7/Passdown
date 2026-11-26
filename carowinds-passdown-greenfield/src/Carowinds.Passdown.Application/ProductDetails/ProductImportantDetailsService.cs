using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Persistence;
using Carowinds.Passdown.Application.Seasons;
using Carowinds.Passdown.Application.Time;
using Carowinds.Passdown.Domain;
using Carowinds.Passdown.Domain.Catalog;
using Carowinds.Passdown.Domain.Promotions;

namespace Carowinds.Passdown.Application.ProductDetails;

/// <summary>Phase 31B: one official statement with how it stands today.</summary>
public sealed record ProductDetailLine(ProductDetailStatement Statement, ProductDetailAssessment Assessment)
{
    public ProductDetailKind Kind => Statement.Kind;

    public string Text => Statement.Text;
}

/// <summary>
/// Phase 31B: the Important Details &amp; Deadlines of one Products row — the official card it was read
/// from, its statements in display order (deadlines first), and the channel document's freshness.
/// </summary>
public sealed record ProductImportantDetails(
    OfficialProductDetails Source,
    IReadOnlyList<ProductDetailLine> Lines,
    OfficialDocumentStatus Status)
{
    public IReadOnlyList<ProductDetailLine> Deadlines => Lines
        .Where(l => l.Statement.PurchaseDeadline is not null || l.Statement.RedemptionDeadline is not null || l.Statement.ValidityWindow is not null)
        .ToList();
}

/// <summary>
/// Phase 31B: matches Products rows to official details. A Season Passes row (pass offer) matches the
/// official card of the same tier on the Season Passes page; every other row matches by its official
/// id/keyword within its own channel (Military &amp; First Responder, Promotions), falling back to an
/// exact official title. A row never borrows another channel's terms, so a perk printed for regular
/// passes is never shown on a military pass (or vice versa).
/// </summary>
public sealed class ProductImportantDetailsIndex
{
    private static readonly string MilitaryTicketsKeywordValue = CatalogProductKey.FromOfficialId(SourceCategory.MilitaryFirstResponder, "MilitaryWizard").Value;

    private readonly IReadOnlyDictionary<OfficialProductChannel, (OfficialProductDetailsDocument Document, OfficialDocumentStatus Status)> _channels;
    private readonly DateOnly _today;
    private readonly DateTimeOffset _now;

    public static ProductImportantDetailsIndex Empty { get; } = new(new Dictionary<OfficialProductChannel, (OfficialProductDetailsDocument, OfficialDocumentStatus)>(), default, default);

    public ProductImportantDetailsIndex(
        IReadOnlyDictionary<OfficialProductChannel, (OfficialProductDetailsDocument Document, OfficialDocumentStatus Status)> channels,
        DateOnly today,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(channels);
        _channels = channels;
        _today = today;
        _now = now;
    }

    public ProductImportantDetails? Find(AssociateProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product.IsCustom || product.Category is not { } category)
        {
            return null;
        }

        var channel = ChannelOf(category);
        if (channel is null || !_channels.TryGetValue(channel.Value, out var entry))
        {
            return null;
        }

        var match = FindCard(entry.Document, product, channel.Value);
        return match is null ? null : Build(match, entry.Status);
    }

    private static OfficialProductChannel? ChannelOf(SourceCategory category) => category switch
    {
        SourceCategory.SeasonPasses => OfficialProductChannel.SeasonPasses,
        SourceCategory.MilitaryFirstResponder or SourceCategory.DatedMilitaryFirstResponder => OfficialProductChannel.MilitaryFirstResponder,
        SourceCategory.Promotions => OfficialProductChannel.Promotions,
        _ => null,
    };

    private static OfficialProductDetails? FindCard(OfficialProductDetailsDocument document, AssociateProduct product, OfficialProductChannel channel)
    {
        if (channel == OfficialProductChannel.SeasonPasses)
        {
            var tier = product.PassOffer?.Tier ?? PassOffer.FromLegacyName(product.DisplayName)?.Tier;
            return tier is null
                ? null
                : document.Products.Where(d => string.Equals(d.TierName, tier, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(d => d.LastVerifiedAt).FirstOrDefault();
        }

        // The per-date Military/First Responder ticket rows come from the "Military & First Responder
        // Ticket" card (Buy Now keyword MilitaryWizard).
        var keyValues = product.Category == SourceCategory.DatedMilitaryFirstResponder
            ? new HashSet<string>(StringComparer.Ordinal) { MilitaryTicketsKeywordValue }
            : OfficialIdValuesOf(product);

        if (keyValues.Count > 0)
        {
            var byId = document.Products
                .Where(d => IdsOf(d, channel).Overlaps(keyValues))
                .OrderByDescending(d => d.LastVerifiedAt)
                .FirstOrDefault();
            if (byId is not null)
            {
                return byId;
            }
        }

        var title = Normalize(product.DisplayName);
        return document.Products
            .Where(d => Normalize(d.Title) == title)
            .OrderByDescending(d => d.LastVerifiedAt)
            .FirstOrDefault();
    }

    private static HashSet<string> OfficialIdValuesOf(AssociateProduct product)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        const string prefix = "catalog|";
        if (!product.ProductIdentity.StartsWith(prefix, StringComparison.Ordinal))
        {
            return values;
        }

        try
        {
            var key = CatalogProductKey.Parse(product.ProductIdentity[prefix.Length..]);
            if (key.Value.StartsWith("id:", StringComparison.Ordinal))
            {
                values.Add(key.Value);
            }
        }
        catch (ArgumentException)
        {
            // not a parseable catalog key — title fallback only
        }

        return values;
    }

    private static HashSet<string> IdsOf(OfficialProductDetails details, OfficialProductChannel channel)
    {
        var category = channel == OfficialProductChannel.MilitaryFirstResponder ? SourceCategory.MilitaryFirstResponder : SourceCategory.Promotions;
        return new[] { details.OfficialId, details.Keyword }
            .Concat(details.RelatedKeywords)
            .OfType<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => CatalogProductKey.FromOfficialId(category, id).Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private ProductImportantDetails Build(OfficialProductDetails details, OfficialDocumentStatus status)
    {
        var lines = details.Statements
            .Select(s => new ProductDetailLine(s, ProductDetailStatusEvaluator.Evaluate(details, s, _today, status.LastSuccessAt, _now)))
            .OrderBy(l => l.Statement.PurchaseDeadline is null && l.Statement.RedemptionDeadline is null && l.Statement.ValidityWindow is null ? 1 : 0)
            .ThenBy(l => KindOrder(l.Kind))
            .ToList();
        return new ProductImportantDetails(details, lines, status);
    }

    private static int KindOrder(ProductDetailKind kind) => kind switch
    {
        ProductDetailKind.BringAFriend => 0,
        ProductDetailKind.Incentive => 1,
        ProductDetailKind.Eligibility => 2,
        ProductDetailKind.VisitValidity => 3,
        ProductDetailKind.Purchase => 4,
        ProductDetailKind.Restriction => 5,
        ProductDetailKind.Fee => 6,
        _ => 7,
    };

    private static string Normalize(string? text) =>
        string.Join(' ', (text ?? string.Empty).Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public interface IProductImportantDetailsService
{
    /// <summary>Reads the cached official product details only — never retrieves online.</summary>
    Task<ProductImportantDetailsIndex> GetIndexAsync(CancellationToken cancellationToken = default);
}

/// <summary>Phase 31B: builds the details index from the three channels' last-known-good documents.</summary>
public sealed class ProductImportantDetailsService(IOfficialReferenceDocumentRepository documents, IClock clock) : IProductImportantDetailsService
{
    public async Task<ProductImportantDetailsIndex> GetIndexAsync(CancellationToken cancellationToken = default)
    {
        var channels = new Dictionary<OfficialProductChannel, (OfficialProductDetailsDocument, OfficialDocumentStatus)>();
        foreach (var channel in OfficialProductDetailsDocumentIds.AllChannels)
        {
            var record = await documents.GetAsync(OfficialProductDetailsDocumentIds.For(channel), cancellationToken).ConfigureAwait(false);
            var document = OfficialSeasonJson.TryDeserialize<OfficialProductDetailsDocument>(record?.SuccessPayloadJson);
            if (document is not null)
            {
                channels[channel] = (document, OfficialDocumentStatus.From(record));
            }
        }

        var now = EasternTimeZone.ConvertFromUtc(clock.UtcNow);
        return new ProductImportantDetailsIndex(channels, DateOnly.FromDateTime(now.DateTime), now);
    }
}
