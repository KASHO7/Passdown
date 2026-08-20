namespace Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// Exact identity for a single dated retrieval result — a source/category checked
/// for one specific date, per spec §6.2's dated-results store. Equality is exact:
/// no two different dates or categories are ever considered the same result, so a
/// value for one date can never be silently substituted for another (spec §5.2/§5.4).
/// </summary>
public readonly record struct DatedResultKey(SourceCategory Category, DateOnly Date)
{
    public override string ToString() => $"{Category}:{Date:yyyy-MM-dd}";
}
