using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.RepositoryGuards;

/// <summary>
/// Repository guard: preserved historical documents stay byte-for-byte identical to the Git blobs they
/// were archived from (docs/archive/README.md, docs/architecture.md "original architecture history").
/// Historical evidence is never edited; later corrections or sign-off go in separate documents.
/// </summary>
public class HistoricalDocumentIntegrityTests
{
    [Theory]
    // Original greenfield build specification (commit 4364f95, later AGENT_BUILD_SPEC.md until 2026-11-26).
    [InlineData("docs/archive/original-greenfield-build-spec.md", "5a16e092a030a2b8ca1b1c891e935a6dc283c0ac")]
    // Staged-but-never-committed .claude/skills/pdf-design/SKILL.md (a legacy UI Polish text).
    [InlineData("docs/archive/claude-skills/ui-polish-legacy-staged-as-pdf-design.md", "83d7eaa1b9a14867a3547cf639405cfe02144025")]
    // docs/architecture.md as committed at 9351c64, before the current-state overview replaced it.
    [InlineData("docs/architecture/architecture-history.md", "33d7d88ff61c786f06437ba17fd96670b01155c2")]
    public void PreservedDocument_MatchesItsArchivedGitBlob(string relativePath, string blobId)
    {
        Assert.Equal(blobId, RepositoryFiles.GitBlobId(relativePath));
    }

    [Fact]
    public void ArchiveReadme_ListsEveryArchivedDocumentWithItsBlobPrefix()
    {
        var readme = RepositoryFiles.ReadText("docs/archive/README.md");

        Assert.Contains("`original-greenfield-build-spec.md`", readme, StringComparison.Ordinal);
        Assert.Contains("`5a16e09`", readme, StringComparison.Ordinal);
        Assert.Contains("`claude-skills/ui-polish-legacy-staged-as-pdf-design.md`", readme, StringComparison.Ordinal);
        Assert.Contains("`83d7eaa`", readme, StringComparison.Ordinal);
    }
}
