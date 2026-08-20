namespace Carowinds.Passdown.Domain.Retrieval;

/// <summary>
/// How a source result was obtained, per spec §5.3 / §6.4's retrieval-strategy preference order.
/// </summary>
public enum RetrievalStrategy
{
    DirectApi,
    Html,
    Browser,
}
