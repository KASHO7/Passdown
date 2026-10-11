using Carowinds.Passdown.Application.Diagnostics;

namespace Carowinds.Passdown.Application.Tests.Diagnostics;

/// <summary>Phase 18A PRIV-001: pure redaction rules for text leaving the machine in a diagnostic ZIP.</summary>
public class DiagnosticPathRedactorTests
{
    [Fact]
    public void Redact_NestedKnownRoots_LongestRootWins()
    {
        var result = DiagnosticPathRedactor.Redact(
            @"path 'c:\users\JANE\appdata\local\x'",
            [new(@"C:\Users\Jane", "%USERPROFILE%"), new(@"C:\Users\Jane\AppData\Local\", "%LOCALAPPDATA%")]);

        Assert.Equal(@"path '%LOCALAPPDATA%\x'", result);
    }

    [Theory]
    [InlineData(@"at 'C:\Users\Mary Ann O.Brien\Desktop\f.pdf'", @"at '%USERPROFILE%\Desktop\f.pdf'")]
    [InlineData("open /Users/kashyap/Library/x.db failed", "open ~/Library/x.db failed")]
    [InlineData("open /home/alex/.local/x.db failed", "open ~/.local/x.db failed")]
    public void Redact_UnknownProfilePaths_AreStillRedacted(string input, string expected)
    {
        Assert.Equal(expected, DiagnosticPathRedactor.Redact(input, []));
    }

    [Fact]
    public void Redact_TextWithoutPaths_IsUnchanged()
    {
        const string line = "2026-08-22T10:00:00Z [Information] Refresh: 8 sources checked, 0 failed. https://www.sixflags.com/carowinds/park-hours";
        Assert.Equal(line, DiagnosticPathRedactor.Redact(line, [new(@"C:\Users\Jane", "%USERPROFILE%")]));
    }

    [Fact]
    public void Redact_TrivialRoots_AreIgnored_SoADriveRootCannotEraseEveryPath()
    {
        Assert.Equal(@"C:\Program Files\x", DiagnosticPathRedactor.Redact(@"C:\Program Files\x", [new(@"C:\", "%ROOT%"), new("", "%EMPTY%")]));
    }
}
