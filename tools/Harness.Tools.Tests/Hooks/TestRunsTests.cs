using Harness.Tools.Hooks;

namespace Harness.Tools.Tests.Hooks;

public class TestRunsTests
{
    [Theory]
    [InlineData("dotnet test --project tests/A/A.csproj")]
    [InlineData("cd repo && dotnet test --project tests/A/A.csproj")]
    [InlineData("dotnet  test   --project tests/A/A.csproj")]
    [InlineData("dotnet test --project tests/A/A.csproj 2>&1 | Select-Object -Last 40")]
    public void SameTestCommand_HasTheSameFingerprint(string command)
    {
        Assert.Equal(TestRuns.Fingerprint("dotnet test --project tests/A/A.csproj"), TestRuns.Fingerprint(command));
    }

    [Theory]
    [InlineData("dotnet test --project tests/A/A.csproj --filter TestCategory=smoke")]
    [InlineData("dotnet test --project tests/B/B.csproj")]
    public void DifferentArguments_GiveADifferentFingerprint(string command)
    {
        Assert.NotEqual(TestRuns.Fingerprint("dotnet test --project tests/A/A.csproj"), TestRuns.Fingerprint(command));
    }

    [Theory]
    [InlineData("dotnet build")]
    [InlineData("echo dotnet testing")]
    [InlineData("")]
    public void NonTestCommand_HasNoFingerprint(string command)
    {
        Assert.Null(TestRuns.Fingerprint(command));
    }

    [Theory]
    [InlineData("Test run summary: Failed!\n  total: 2\n  failed: 1\n  succeeded: 1\n  skipped: 0")]
    [InlineData("Test run summary: Aborted!")]
    [InlineData("  failed: 3")]
    [InlineData("Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2")]
    [InlineData("Build FAILED.")]
    [InlineData("C:\\x\\Foo.cs(3,1): error CS1002: ; expected")]
    [InlineData("error NU1101: Unable to find package X")]
    public void RecognisesFailedRuns(string output)
    {
        Assert.True(TestRuns.LooksFailed(output));
    }

    [Theory]
    [InlineData("Test run summary: Passed!\n  total: 2\n  failed: 0\n  succeeded: 2")]
    [InlineData("Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2")]
    [InlineData("passed ShowsFailedLoginMessageAsync (1s)")]
    [InlineData("Build succeeded.\n    0 Error(s)")]
    [InlineData("")]
    public void RecognisesPassingRuns(string output)
    {
        Assert.False(TestRuns.LooksFailed(output));
    }
}
