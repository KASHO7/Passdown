using Carowinds.Passdown.Application.PersonalSync;
using Carowinds.Passdown.Desktop.Presentation;

namespace Carowinds.Passdown.Ui.Tests.PersonalSync;

/// <summary>Phase 16E §26/§30: the Sync Now status line distinguishes History imported/exported/conflicts/incomplete from drafts/notes, without any new UI surface.</summary>
public class PersonalSyncResultPresentationTests
{
    [Fact]
    public void Format_HistoryItemsSynced_AppendsCombinedImportedAndExportedCount()
    {
        var result = new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 0, 0, 0, 0, null,
            HistoryImportedCount: 2, HistoryExportedCount: 1);

        Assert.Contains("3 history items synced.", PersonalSyncResultPresentation.Format(result));
    }

    [Fact]
    public void Format_OneHistoryItemSynced_UsesSingularWording()
    {
        var result = new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 0, 0, 0, 0, null,
            HistoryImportedCount: 1);

        Assert.Contains("1 history item synced.", PersonalSyncResultPresentation.Format(result));
    }

    [Fact]
    public void Format_HistoryConflicts_AppendsManualReviewCallout()
    {
        var result = new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 0, 0, 0, 0, null,
            HistoryConflictCount: 1);

        Assert.Contains("1 history conflict — requires manual review.", PersonalSyncResultPresentation.Format(result));
    }

    [Fact]
    public void Format_HistoryIncomplete_AppendsWaitingForPdfCallout()
    {
        var result = new PersonalSyncResult(PersonalSyncOutcome.Completed, 0, 0, 0, 0, 0, 0, null,
            HistoryIncompleteCount: 2);

        Assert.Contains("2 history items waiting for their PDF.", PersonalSyncResultPresentation.Format(result));
    }

    [Fact]
    public void Format_NoHistoryActivity_OmitsEveryHistoryClause()
    {
        var result = new PersonalSyncResult(PersonalSyncOutcome.Completed, 1, 1, 1, 0, 0, 0, null);

        var text = PersonalSyncResultPresentation.Format(result);

        Assert.DoesNotContain("history", text);
    }
}
