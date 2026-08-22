using Carowinds.Passdown.Desktop.ScheduledRefresh;

namespace Carowinds.Passdown.Ui.Tests.ScheduledRefresh;

public class CommandLineArgumentsTests
{
    // 1. No argument selects normal desktop mode.
    [Fact]
    public void Parse_NoArguments_SelectsNormalMode()
    {
        var result = CommandLineArguments.Parse([]);

        Assert.Equal(StartupMode.Normal, result.Mode);
    }

    // 2. --scheduled-refresh selects scheduled mode.
    [Fact]
    public void Parse_ScheduledRefreshFlag_SelectsScheduledMode()
    {
        var result = CommandLineArguments.Parse(["--scheduled-refresh"]);

        Assert.Equal(StartupMode.ScheduledRefresh, result.Mode);
    }

    // 3. Unknown argument does not trigger scheduled refresh.
    [Fact]
    public void Parse_UnknownArgument_IsInvalid_NeverNormalOrScheduled()
    {
        var result = CommandLineArguments.Parse(["--some-other-flag"]);

        Assert.Equal(StartupMode.Invalid, result.Mode);
        Assert.NotNull(result.UsageError);
    }

    // 12. Invalid command usage returns usage exit code (parsing side of that: it must be classified Invalid).
    [Fact]
    public void Parse_ScheduledRefreshFlagWithExtraArgument_IsInvalid()
    {
        var result = CommandLineArguments.Parse(["--scheduled-refresh", "extra"]);

        Assert.Equal(StartupMode.Invalid, result.Mode);
    }

    [Fact]
    public void Parse_CaseSensitiveFlag_MismatchedCaseIsInvalid()
    {
        var result = CommandLineArguments.Parse(["--Scheduled-Refresh"]);

        Assert.Equal(StartupMode.Invalid, result.Mode);
    }
}
