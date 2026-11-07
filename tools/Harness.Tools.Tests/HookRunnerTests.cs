using Harness.Tools.Hooks;

namespace Harness.Tools.Tests;

public class HookRunnerTests
{
    private const string ShellInput = """{"tool_name":"Bash","tool_input":{"command":"dotnet test"}}""";

    public static TheoryData<string> Modes => [.. HookRunner.Modes];

    [Theory]
    [MemberData(nameof(Modes))]
    public void EveryMode_AllowsAHarmlessEvent(string mode)
    {
        using var host = new Hooks.HookTestHost();

        var result = host.Run(mode, """{"session_id":"s","tool_name":"Read","tool_input":{"file_path":"a.cs"}}""");

        Assert.Equal(0, result.Code);
    }

    [Fact]
    public void ModesCoverThePlannedHooks()
    {
        Assert.Equal(
            ["post-edit", "post-shell", "pre-compact", "pre-shell", "pre-write", "session-end", "session-start"],
            HookRunner.Modes.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-mode")]
    public void UnknownOrMissingMode_FailsOpen(string? mode)
    {
        var io = Console3.WithInput(ShellInput);

        var code = HookRunner.Run(mode, io.In, io.Out, io.Err);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, io.Out.ToString());
        Assert.Contains("harness hook", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void MalformedInput_FailsOpen(string stdin)
    {
        var io = Console3.WithInput(stdin);

        var code = HookRunner.Run("pre-shell", io.In, io.Out, io.Err);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, io.Out.ToString());
    }

    [Fact]
    public void HandlerThatThrows_FailsOpen_AndDiscardsItsStdout()
    {
        var io = Console3.WithInput(ShellInput);
        var handlers = new Dictionary<string, HookHandler>
        {
            ["pre-shell"] = input =>
            {
                input.Out.Write("partial output");
                throw new InvalidOperationException("boom");
            },
        };

        var code = HookRunner.Run("pre-shell", io.In, io.Out, io.Err, handlers: handlers);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, io.Out.ToString());
        Assert.Contains("boom", io.Err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HandlerThatBlocks_ReturnsExitCode2_WithItsReasonOnStderr()
    {
        var io = Console3.WithInput(ShellInput);
        var handlers = new Dictionary<string, HookHandler>
        {
            ["pre-shell"] = input =>
            {
                input.Err.Write("blocked: reason");
                return HookDecision.Block;
            },
        };

        var code = HookRunner.Run("pre-shell", io.In, io.Out, io.Err, handlers: handlers);

        Assert.Equal(2, code);
        Assert.Equal("blocked: reason", io.Err.ToString());
    }

    [Fact]
    public void HandlerReceivesParsedInput()
    {
        var io = Console3.WithInput(ShellInput);
        string? seenTool = null;
        var handlers = new Dictionary<string, HookHandler>
        {
            ["pre-shell"] = input =>
            {
                seenTool = input.Payload.GetProperty("tool_name").GetString();
                return HookDecision.Allow;
            },
        };

        HookRunner.Run("pre-shell", io.In, io.Out, io.Err, handlers: handlers);

        Assert.Equal("Bash", seenTool);
    }

    [Fact]
    public void HandlerOutputIsPassedThrough_WhenItSucceeds()
    {
        var io = Console3.WithInput(ShellInput);
        var handlers = new Dictionary<string, HookHandler>
        {
            ["session-start"] = input =>
            {
                input.Out.Write("reminder");
                return HookDecision.Allow;
            },
        };

        var code = HookRunner.Run("session-start", io.In, io.Out, io.Err, handlers: handlers);

        Assert.Equal(0, code);
        Assert.Equal("reminder", io.Out.ToString());
    }
}
