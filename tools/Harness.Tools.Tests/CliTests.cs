namespace Harness.Tools.Tests;

public class CliTests
{
    private static (int Code, Console3 Io) Run(params string[] args)
    {
        var io = new Console3();
        var code = Cli.Run(args, io.In, io.Out, io.Err);
        return (code, io);
    }

    [Fact]
    public void NoArguments_PrintsUsage_AndReturnsUsageCode()
    {
        var (code, io) = Run();

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("Usage:", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void Help_PrintsUsageToStdout_AndSucceeds(string flag)
    {
        var (code, io) = Run(flag);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Contains("Usage:", io.Out.ToString(), StringComparison.Ordinal);
        Assert.Contains("scaffold", io.Out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommand_ReturnsUsageCode_AndNamesTheCommand()
    {
        var (code, io) = Run("frobnicate");

        Assert.Equal(ExitCodes.Usage, code);
        Assert.Contains("frobnicate", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validate-skills")]
    public void PlannedCommand_IsRouted_ButReportsNotImplemented(string command)
    {
        var (code, io) = Run(command);

        Assert.Equal(ExitCodes.Failed, code);
        Assert.Contains("not implemented", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Hook_IsRoutedToHookRunner_AndFailsOpen()
    {
        var io = Console3.WithInput("{}");

        var code = Cli.Run(["hook", "pre-shell"], io.In, io.Out, io.Err);

        Assert.Equal(ExitCodes.Ok, code);
        Assert.Equal(string.Empty, io.Out.ToString());
    }
}
