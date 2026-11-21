using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace {{Name}}.Framework.Utils;

/// <summary>
/// What the browser said during one test: console errors and warnings, uncaught page errors,
/// failed requests and HTTP error responses, from every page of the context. UiTest writes the
/// <see cref="Report"/> to the test output when the test failed, so it lands in the TRX. One
/// instance per test; lines are masked by <see cref="Redaction"/> and capped.
/// </summary>
public sealed partial class BrowserDiagnostics
{
    private const int MaxLines = 50;
    private const int MaxLength = 300;
    private const int MaxStored = 4000;

    private readonly List<string> _lines = [];
    private readonly Lock _gate = new();
    private int _dropped;

    private BrowserDiagnostics()
    {
    }

    public static BrowserDiagnostics Attach(IBrowserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var diagnostics = new BrowserDiagnostics();
        context.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning")
            {
                diagnostics.Add($"console.{message.Type}: {message.Text}");
            }
        };
        context.WebError += (_, error) => diagnostics.Add($"pageerror: {error.Error}");
        context.RequestFailed += (_, request) => diagnostics.Add($"requestfailed: {request.Method} {request.Url} ({request.Failure})");
        context.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                diagnostics.Add($"response {response.Status}: {response.Request.Method} {response.Url}");
            }
        };
        return diagnostics;
    }

    /// <summary>The lines in the order they happened, masked, each at most 300 characters, at most 50 of them.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                // Masked when read, so a secret remembered after the event is still hidden; cut after masking.
                var lines = _lines.Select(l => Cut(Redaction.Mask(l))).ToList();
                if (_dropped > 0)
                {
                    lines.Add($"… and {_dropped} more");
                }

                return lines;
            }
        }
    }

    /// <summary>The block for the test output: a heading and the lines, or nothing when the browser said nothing.</summary>
    public IReadOnlyList<string> Report()
    {
        var lines = Lines;
        return lines.Count == 0 ? [] : [$"Browser diagnostics ({lines.Count}):", .. lines.Select(l => "  " + l)];
    }

    private void Add(string line)
    {
        var oneLine = Whitespace().Replace(line, " ").Trim();
        lock (_gate)
        {
            if (_lines.Count < MaxLines)
            {
                _lines.Add(oneLine.Length > MaxStored ? oneLine[..MaxStored] : oneLine);
            }
            else
            {
                _dropped++;
            }
        }
    }

    private static string Cut(string line) => line.Length <= MaxLength ? line : line[..(MaxLength - 1)] + "…";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
