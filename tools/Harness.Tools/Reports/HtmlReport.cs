using System.Net;
using System.Text;

namespace Harness.Tools.Reports;

/// <summary>The shared shape of the static HTML reports: no scripts, and a policy that forbids them.</summary>
internal static class HtmlReport
{
    private const string Style = """
          :root { color-scheme: light dark; --surface: #fcfcfb; --tile: #f0efec; --primary: #0b0b0b; --secondary: #52514e; --rule: #e0dfd8; --link: #256abf; --passed: #2a78d6; --failed: #eb6834; }
          @media (prefers-color-scheme: dark) { :root { --surface: #1a1a19; --tile: #2a2a28; --primary: #f0efec; --secondary: #c3c2b7; --rule: #383835; --link: #86b6ef; --passed: #3987e5; --failed: #d95926; } }
          * { box-sizing: border-box; }
          body { margin: 0 auto; max-width: 62rem; padding: 2rem 1.25rem 4rem; background: var(--surface); color: var(--primary); font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
          h1 { font-size: 1.6rem; margin: 0 0 .25rem; }
          h2 { font-size: 1.15rem; margin: 2.25rem 0 .5rem; }
          h3 { font-size: 1rem; margin: 1.25rem 0 .25rem; }
          p { margin: .5rem 0; }
          a { color: var(--link); }
          .muted { color: var(--secondary); font-size: .9rem; }
          .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(10.5rem, 1fr)); gap: .75rem; margin: 1.25rem 0; }
          .tile { background: var(--tile); border-radius: 8px; padding: .75rem 1rem; }
          .tile b { display: block; font-size: 1.9rem; line-height: 1.2; font-variant-numeric: tabular-nums; }
          .tile span { color: var(--secondary); font-size: .9rem; }
          .legend { display: flex; gap: 1.25rem; margin: .5rem 0; color: var(--secondary); font-size: .9rem; }
          .key { display: inline-block; width: .75rem; height: .75rem; border-radius: 3px; margin-right: .4rem; vertical-align: -1px; }
          .row { display: grid; grid-template-columns: 9.5rem 1fr 10.5rem; gap: .75rem; align-items: center; padding: .25rem 0; }
          .name, .counts { font-size: .9rem; }
          .counts { color: var(--secondary); font-variant-numeric: tabular-nums; }
          .track { display: flex; gap: 2px; height: .9rem; }
          .bar { display: block; min-width: 3px; }
          .bar:first-child { border-radius: 4px 0 0 4px; }
          .bar:last-child { border-radius: 0 4px 4px 0; }
          .bar:only-child { border-radius: 4px; }
          .passed { background: var(--passed); }
          .failed { background: var(--failed); }
          .scroll { overflow-x: auto; }
          table { width: 100%; border-collapse: collapse; font-size: .9rem; margin: .5rem 0 1rem; }
          th, td { text-align: left; vertical-align: top; padding: .45rem .75rem .45rem 0; border-bottom: 1px solid var(--rule); }
          th { color: var(--secondary); font-weight: 600; }
          th.num, td.num { text-align: right; font-variant-numeric: tabular-nums; }
          .layer { display: flex; justify-content: space-between; gap: .75rem; font-weight: 600; font-size: .95rem; margin-top: 1rem; padding-bottom: .25rem; border-bottom: 1px solid var(--rule); }
          details { border-bottom: 1px solid var(--rule); padding: .5rem 0; }
          summary { cursor: pointer; }
          @media (max-width: 40rem) { .row { grid-template-columns: 1fr; gap: .1rem; padding: .5rem 0; } }
        """;

    public static string E(string text) => WebUtility.HtmlEncode(text);

    public static string Tile(string value, string label) =>
        $"<div class=\"tile\"><b>{E(value)}</b><span>{E(label)}</span></div>";

    /// <summary>A table. Headers and cells are already HTML; a header starting with <c>#</c> is a number column.</summary>
    public static string Table(string[] headers, IEnumerable<string> rows)
    {
        var head = string.Concat(headers.Select(h => h.StartsWith('#') ? $"<th class=\"num\">{h[1..]}</th>" : $"<th>{h}</th>"));
        return $"<div class=\"scroll\"><table><thead><tr>{head}</tr></thead><tbody>{string.Concat(rows)}</tbody></table></div>";
    }

    public static string Page(string title, string body)
    {
        var page = new StringBuilder();
        page.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        page.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<meta name=\"robots\" content=\"noindex\">\n");
        page.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">\n");
        page.Append("<title>").Append(E(title)).Append("</title>\n<style>\n").Append(Style).Append("\n</style>\n</head>\n<body>\n");
        page.Append(body).Append("\n</body>\n</html>\n");
        return page.ToString();
    }

    public static void Write(string path, string html)
    {
        CiFiles.EnsureFolder(path);
        File.WriteAllText(path, html);
    }
}
