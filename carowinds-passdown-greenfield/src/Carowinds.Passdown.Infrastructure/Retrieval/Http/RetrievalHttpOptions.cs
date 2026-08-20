namespace Carowinds.Passdown.Infrastructure.Retrieval.Http;

public sealed class RetrievalHttpOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}
