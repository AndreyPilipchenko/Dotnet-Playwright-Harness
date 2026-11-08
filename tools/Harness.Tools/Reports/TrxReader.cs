using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Harness.Tools.Reports;

internal enum TrxOutcome
{
    Passed,
    Failed,
    Skipped,
}

internal sealed record TrxTest(
    string Name,
    TrxOutcome Outcome,
    string? Message,
    IReadOnlyList<string> Bugs,
    IReadOnlyList<string> Cases,
    string? ClassName);

/// <summary>The tests of one or more TRX files. <see cref="RunProblem"/> is set when the run itself reported a failure.</summary>
internal sealed record TrxRun(IReadOnlyList<TrxTest> Tests, TimeSpan Duration, string? RunProblem)
{
    public IEnumerable<TrxTest> With(TrxOutcome outcome) => Tests.Where(t => t.Outcome == outcome);
}

/// <summary>
/// Reads TRX as NUnit, xUnit v3 and MSTest v4 write it under Microsoft Testing Platform. A skip is
/// <c>NotExecuted</c> in all three; only xUnit puts the class in the test name.
/// </summary>
internal static class TrxReader
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private static readonly string[] FailingOutcomes = ["Failed", "Error", "Timeout", "Aborted"];

    /// <summary>The TRX files at a path: the file itself, or every <c>*.trx</c> below a folder.</summary>
    public static string[] Find(string path) =>
        File.Exists(path) ? [path]
        : Directory.Exists(path) ? [.. Directory.GetFiles(path, "*.trx", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
        : [];

    /// <summary>Reads the TRX at a path. Null when there is none or it can't be read.</summary>
    public static TrxRun? TryRead(string path)
    {
        var files = Find(path);
        if (files.Length == 0)
        {
            return null;
        }

        try
        {
            return Read(files);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <exception cref="InvalidDataException">A file is not TRX.</exception>
    /// <exception cref="XmlException">A file is not well-formed XML, or holds a DTD.</exception>
    public static TrxRun Read(IReadOnlyList<string> files)
    {
        var tests = new List<TrxTest>();
        DateTimeOffset? start = null;
        DateTimeOffset? finish = null;
        string? problem = null;
        var problemFound = false;

        foreach (var file in files)
        {
            var root = Load(file);
            tests.AddRange(ReadTests(root));

            var times = root.Element(Ns + "Times");
            start = Earliest(start, Time(times, "start"));
            finish = Latest(finish, Time(times, "finish"));

            var summary = root.Element(Ns + "ResultSummary");
            if (FailingOutcomes.Contains((string?)summary?.Attribute("outcome"), StringComparer.OrdinalIgnoreCase))
            {
                problemFound = true;
                problem ??= summary?.Descendants(Ns + "RunInfo")
                    .FirstOrDefault(i => string.Equals((string?)i.Attribute("outcome"), "Error", StringComparison.OrdinalIgnoreCase))
                    ?.Element(Ns + "Text")?.Value;
            }
        }

        var duration = start is { } s && finish is { } f && f >= s ? f - s : TimeSpan.Zero;
        return new TrxRun(tests, duration, problemFound ? problem ?? string.Empty : null);
    }

    private static XElement Load(string file)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(file, settings);
        var root = XDocument.Load(reader).Root;
        return root?.Name == Ns + "TestRun" ? root : throw new InvalidDataException("Not a TRX file.");
    }

    private static IEnumerable<TrxTest> ReadTests(XElement root)
    {
        var definitions = root.Element(Ns + "TestDefinitions")?.Elements(Ns + "UnitTest")
            .Where(d => d.Attribute("id") is not null)
            .GroupBy(d => (string)d.Attribute("id")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase) ?? [];

        foreach (var result in root.Element(Ns + "Results")?.Elements(Ns + "UnitTestResult") ?? [])
        {
            definitions.TryGetValue((string?)result.Attribute("testId") ?? string.Empty, out var definition);
            var message = result.Element(Ns + "Output")?.Element(Ns + "ErrorInfo")?.Element(Ns + "Message")?.Value;
            var categories = definition?.Descendants(Ns + "TestCategoryItem")
                .Select(c => (string?)c.Attribute("TestCategory") ?? string.Empty)
                .ToList() ?? [];
            var className = (string?)definition?.Element(Ns + "TestMethod")?.Attribute("className");

            yield return new TrxTest(
                FullName((string?)result.Attribute("testName") ?? "(unnamed)", className),
                OutcomeOf((string?)result.Attribute("outcome")),
                string.IsNullOrWhiteSpace(message) ? null : message,
                Linked(categories, "bug:"),
                Linked(categories, "tc:"),
                className);
        }
    }

    private static List<string> Linked(List<string> categories, string prefix) =>
        [.. categories.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(c => c[prefix.Length..])];

    private static string FullName(string testName, string? className) =>
        string.IsNullOrEmpty(className) || testName.StartsWith(className + ".", StringComparison.Ordinal)
            ? testName
            : $"{className}.{testName}";

    private static TrxOutcome OutcomeOf(string? outcome) =>
        string.Equals(outcome, "Passed", StringComparison.OrdinalIgnoreCase) ? TrxOutcome.Passed
        : FailingOutcomes.Contains(outcome, StringComparer.OrdinalIgnoreCase) ? TrxOutcome.Failed
        : TrxOutcome.Skipped;

    private static DateTimeOffset? Time(XElement? times, string attribute) =>
        DateTimeOffset.TryParse((string?)times?.Attribute(attribute), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;

    private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null || a < b ? a : b;

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null || a > b ? a : b;
}
