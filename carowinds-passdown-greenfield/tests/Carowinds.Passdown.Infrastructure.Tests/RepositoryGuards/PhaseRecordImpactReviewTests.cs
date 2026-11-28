using System.Text.RegularExpressions;
using Carowinds.Passdown.Infrastructure.Tests.TestSupport;

namespace Carowinds.Passdown.Infrastructure.Tests.RepositoryGuards;

/// <summary>
/// Repository guard: every phase record written after the 2026-11-26 documentation maintenance
/// carries both mandatory maintenance statuses (.claude/rules/documentation.md "Mandatory Product
/// Specification Maintenance"; template docs/templates/phase-record-template.md). The check is
/// mechanical — it confirms the review was recorded, not that it was correct.
/// Records that predate the rule are listed below by exact name (paths relative to <c>docs/</c>) and are
/// never edited to satisfy it (historical evidence is preserved); do not add new names to this list.
/// Every other <c>phase*.md</c> anywhere under <c>docs/</c> — including one named like a checklist or
/// placed in a subfolder — must state each status exactly once with a permitted value.
/// </summary>
public partial class PhaseRecordImpactReviewTests
{
    private static readonly HashSet<string> RecordsPredatingTheRule = new(StringComparer.Ordinal)
    {
        "phase1-discovery.md",
        "phase9-finalization.md",
        "phase10-operations-dashboard.md",
        "phase11-operational-notes.md",
        "phase12-operations-calendar.md",
        "phase13-hardening.md",
        "phase18-comprehensive-audit.md",
        "phase18f-teams-copy.md",
        "phase19a-automatic-refresh.md",
        "phase19b-seasonal-catalog.md",
        "phase20a-spellcheck-price-history.md",
        "phase20c-historical-backfill.md",
        "phase21a-pricing-historical-integrity.md",
        "phase21b-associate-navigation-toolbar.md",
        "phase21c-official-show-schedules.md",
        "phase21d-daily-feature-math.md",
        "phase22a-design-foundation.md",
        "phase22b-fury-banner-navigation.md",
        "phase22c-dashboard-navigation-pdf-order.md",
        "phase22d-associate-workspace-number-formatting.md",
        "phase22e-calendar-notes-history.md",
        "phase22e1-published-park-schedule-coverage.md",
        "phase22f-settings-accessibility-final-qa.md",
        "phase23a-save-failure-freeze-fix.md",
        "phase23b-dashboard-shows-branding.md",
        "phase23c-appearance-preferences.md",
        "phase23d-github-updater.md",
        "phase25-memberships-season-passes.md",
        "phase26-official-price-history-sync.md",
        "phase29-30-windows-validation-kit.md",
        "phase29-morning-message-teams-copy.md",
        "phase29a-windows-teams-validation.md",
        "phase29b-morning-message-heading-rich-formatting.md",
        "phase29c-windows-clipboard-interop.md",
        "phase30-optional-morning-message-sync.md",
        "phase31-dynamic-seasons-and-deals.md",
        "phase31b-product-details-price-history-ert-sync.md",
        "phase32-monthly-pass-perks-calendar.md",
        "phase33-live-pdf-preview-printing.md",
        "phase34a-repository-performance-audit.md",
        "phase34b1-preview-responsiveness.md",
        "phase34b2-general-performance-optimization.md",
        "phase34c-repository-cleanup.md",
        "phase35-unified-ui-modernization.md",
        "phase35b-passdown-workspace-redesign.md",
        "phase35c-products-pricing-redesign.md",
        "phase35d-shows-notes-shared-polish.md",
        "phase35e-dashboard-calendar-redesign.md",
        "phase35f-operational-notes-history-settings.md",
        "phase35g-final-uiux-polish-readiness.md",
        // Windows validation checklists that predate the rule (new checklists live in docs/releases/).
        "phase-7e-windows-validation-checklist.md",
        "phase18e-rc1-windows-checklist.md",
        "phase18g-rc2-windows-checklist.md",
        "phase19c-rc3-windows-checklist.md",
        "phase20b-rc4-windows-checklist.md",
        "phase20d-rc5-windows-checklist.md",
        "phase21e-rc6-windows-checklist.md",
        "phase22-windows-validation-checklist.md",
        "phase31b-windows-gui-checklist.md",
        "phase31c-windows-gui-checklist.md",
        "phase31d-windows-gui-checklist.md",
        "phase34a-windows-performance-checklist.md",
    };

    [Fact]
    public void GrandfatheredRecords_StillExist()
    {
        var missing = RecordsPredatingTheRule.Where(name => !File.Exists(RepositoryFiles.PathOf($"docs/{name}"))).ToArray();

        Assert.Equal(62, RecordsPredatingTheRule.Count);

        Assert.True(missing.Length == 0, "Historical phase records must not be deleted or renamed: " + string.Join(", ", missing));
    }

    [Fact]
    public void Template_DeclaresBothStatusLines()
    {
        var template = RepositoryFiles.ReadText("docs/templates/phase-record-template.md");

        Assert.Single(ProductSpecificationStatus().Matches(template));
        Assert.Single(ArchitectureDocumentationStatus().Matches(template));
    }

    [Fact]
    public void NewPhaseRecords_RecordProductSpecificationAndArchitectureStatus()
    {
        var failures = new List<string>();
        foreach (var path in Directory.GetFiles(RepositoryFiles.PathOf("docs"), "phase*.md", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(RepositoryFiles.PathOf("docs"), path).Replace(Path.DirectorySeparatorChar, '/');
            if (RecordsPredatingTheRule.Contains(name))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            if (ProductSpecificationStatus().Matches(text).Count != 1)
            {
                failures.Add($"{name}: needs exactly one \"**Product specification:** Updated | Reviewed — no changes required | Discrepancy requires approval\"");
            }

            if (ArchitectureDocumentationStatus().Matches(text).Count != 1)
            {
                failures.Add($"{name}: needs exactly one \"**Architecture documentation:** Updated | Reviewed — no changes required | Discrepancy requires investigation\"");
            }

            if (AnyStatusLine().Matches(text).Count != 2)
            {
                failures.Add($"{name}: a status line has an unpermitted value or is repeated");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [GeneratedRegex(@"\*\*(?:Product specification|Architecture documentation):\*\*")]
    private static partial Regex AnyStatusLine();

    [GeneratedRegex(@"\*\*Product specification:\*\*\s+(Updated|Reviewed — no changes required|Discrepancy requires approval)\b", RegexOptions.Multiline)]
    private static partial Regex ProductSpecificationStatus();

    [GeneratedRegex(@"\*\*Architecture documentation:\*\*\s+(Updated|Reviewed — no changes required|Discrepancy requires investigation)\b", RegexOptions.Multiline)]
    private static partial Regex ArchitectureDocumentationStatus();
}
