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
    [InlineData("dotnet test --project tests/A/A.csproj --results-directory TestResults/verify-2")]
    [InlineData("dotnet test --project tests/A/A.csproj --results-directory \"Test Results/run 2\"")]
    [InlineData("dotnet test --project tests/A/A.csproj --results-directory=TestResults/run-3")]
    [InlineData("dotnet test --project tests/A/A.csproj --report-trx")]
    [InlineData("dotnet test --project tests/A/A.csproj --no-build --no-restore")]
    [InlineData("dotnet test --no-build --project tests/A/A.csproj --report-trx --results-directory TestResults/x")]
    public void OptionsThatDoNotChangeWhichTestsRun_KeepTheFingerprint(string command)
    {
        Assert.Equal(TestRuns.Fingerprint("dotnet test --project tests/A/A.csproj"), TestRuns.Fingerprint(command));
    }

    [Theory]
    [InlineData("$env:BROWSER='firefox'; dotnet test --project tests/A/A.csproj")]
    [InlineData("$env:HEADED = \"1\"; dotnet test --project tests/A/A.csproj")]
    [InlineData("BROWSER=webkit dotnet test --project tests/A/A.csproj")]
    public void ChangingTheBrowserOrHeadedMode_GivesADifferentFingerprint(string command)
    {
        Assert.NotEqual(TestRuns.Fingerprint("dotnet test --project tests/A/A.csproj"), TestRuns.Fingerprint(command));
    }

    [Fact]
    public void TheBrowserSetting_IsComparedByValue_InEitherShell()
    {
        Assert.Equal(
            TestRuns.Fingerprint("$env:BROWSER = 'firefox'; dotnet test --project tests/A/A.csproj"),
            TestRuns.Fingerprint("BROWSER=firefox dotnet test --project tests/A/A.csproj"));
    }

    [Theory]
    [InlineData("$env:ANYTHING='1'; dotnet test --project tests/A/A.csproj")]
    [InlineData("cd src; dotnet test --project tests/A/A.csproj")]
    public void OtherVariablesAndCommands_BeforeTheRun_KeepTheFingerprint(string command)
    {
        Assert.Equal(TestRuns.Fingerprint("dotnet test --project tests/A/A.csproj"), TestRuns.Fingerprint(command));
    }

    private const string Run = "dotnet test --project tests/A/A.csproj";
    private static readonly string ProjectDir = Path.Combine(Path.GetTempPath(), "harness-fp-project");
    private static readonly string ScratchDir = Path.Combine(Path.GetTempPath(), "harness-fp-scratch");

    [Theory]
    [InlineData(null)]
    [InlineData("harness-fp-project")]
    [InlineData("harness-fp-project/tests")]
    public void ARunInsideTheProject_OrInAnUnknownFolder_IsFingerprinted(string? relativeFolder)
    {
        var folder = relativeFolder is null ? null : Path.Combine(Path.GetTempPath(), relativeFolder);

        Assert.Equal([TestRuns.Fingerprint(Run)!], TestRuns.Fingerprints(Run, folder, ProjectDir));
    }

    [Theory]
    [InlineData("harness-fp-scratch")]
    [InlineData("harness-fp-project-copy")]
    [InlineData("")]
    public void ARunInAFolderOutsideTheProject_HasNoFingerprint(string siblingFolder)
    {
        Assert.Empty(TestRuns.Fingerprints(Run, Path.Combine(Path.GetTempPath(), siblingFolder), ProjectDir));
    }

    [Fact]
    public void ACdOutOfTheProject_BeforeTheRun_MeansNoFingerprint()
    {
        Assert.Empty(TestRuns.Fingerprints($"cd \"{ScratchDir}\"; dotnet build; {Run}", ProjectDir, ProjectDir));
        Assert.Empty(TestRuns.Fingerprints($"Set-Location -Path '{ScratchDir}'; {Run}", ProjectDir, ProjectDir));
        Assert.Empty(TestRuns.Fingerprints($"cd ..; {Run}", ProjectDir, ProjectDir));
    }

    [Fact]
    public void ACdBackIntoTheProject_BeforeTheRun_MeansTheRunCounts()
    {
        var command = $"cd \"{ScratchDir}\"; dotnet build; cd \"{ProjectDir}\"; {Run}";

        Assert.Equal([TestRuns.Fingerprint(Run)!], TestRuns.Fingerprints(command, ProjectDir, ProjectDir));
    }

    [Fact]
    public void OnlyTheRunsInsideTheProject_OfAMixedChain_AreFingerprinted()
    {
        var command = $"{Run} --filter A; cd \"{ScratchDir}\"; {Run} --filter B";

        Assert.Equal([TestRuns.Fingerprint(Run + " --filter A")!], TestRuns.Fingerprints(command, ProjectDir, ProjectDir));
    }

    [Fact]
    public void EveryTestCommandInAChain_HasAFingerprint()
    {
        var all = TestRuns.Fingerprints("dotnet build; dotnet test --project tests/B/B.csproj && dotnet test --project tests/A/A.csproj | Select-Object -Last 5");

        Assert.Equal(
            [TestRuns.Fingerprint("dotnet test --project tests/B/B.csproj")!, TestRuns.Fingerprint("dotnet test --project tests/A/A.csproj")!],
            all);
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
