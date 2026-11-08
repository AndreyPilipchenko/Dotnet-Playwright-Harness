using Harness.Tools.Conventions;

namespace Harness.Tools.Tests.Conventions;

/// <summary>A temporary project root to run the conventions checker against.</summary>
internal sealed class ConventionHost : IDisposable
{
    public const string TestPath = "tests/Acme.Tests/Ui/LoginTests.cs";
    public const string SetupPath = "tests/Acme.Tests/Setup/UiTest.cs";
    public const string PagePath = "src/Acme.Framework/Pages/LoginPage.cs";
    public const string ApiPath = "src/Acme.Framework/Apis/UsersApi.cs";
    public const string UtilPath = "src/Acme.Framework/Utils/Waits.cs";
    public const string ExtensionPath = "src/Acme.Framework/Utils/Extensions/LocatorExtensions.cs";

    public TempDir Root { get; } = new();

    public ConventionHost Write(string relativePath, string content)
    {
        Root.Write(relativePath, content);
        return this;
    }

    public ConventionReport Check(params string[] files) =>
        ConventionChecker.Run(Root.Path, files.Length == 0 ? null : files);

    public IReadOnlyList<Finding> FindingsFor(string relativePath) =>
        Check(relativePath).Findings.Where(f => f.File == relativePath).ToList();

    public void Dispose() => Root.Dispose();
}
