namespace Harness.Tools.Conventions;

/// <summary>
/// Maps a project-relative path to its layer. The layout is the generated one:
/// <c>src/&lt;Name&gt;.Framework/{Pages,Apis,Dbs,Helpers,Utils,Config}/</c> and
/// <c>tests/&lt;Name&gt;.Tests/{Ui,Api,Setup}/</c>. Build output is never a layer.
/// </summary>
internal static class LayerMap
{
    private static readonly (string Folder, Layer Layer)[] FrameworkFolders =
    [
        ("Pages", Layer.Pages),
        ("Apis", Layer.Apis),
        ("Dbs", Layer.Dbs),
        ("Helpers", Layer.Helpers),
        ("Utils", Layer.Utils),
        ("Config", Layer.Config),
    ];

    public static Layer Of(string relativePath)
    {
        if (!relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return Layer.None;
        }

        if (IsBuildOutput(relativePath))
        {
            return Layer.None;
        }

        var parts = relativePath.Replace('\\', '/').Split('/');

        switch (parts[0])
        {
            case "tests":
                return parts.Contains("Setup") ? Layer.Setup : Layer.Tests;
            case "src":
                foreach (var part in parts)
                {
                    foreach (var (folder, layer) in FrameworkFolders)
                    {
                        if (part == folder)
                        {
                            return layer;
                        }
                    }
                }

                return Layer.Framework;
            default:
                return Layer.None;
        }
    }

    public static bool IsBuildOutput(string relativePath) =>
        relativePath.Replace('\\', '/').Split('/').Any(p => p is "bin" or "obj");

    public static bool IsFrameworkLayer(Layer layer) =>
        layer is Layer.Pages or Layer.Apis or Layer.Dbs or Layer.Helpers or Layer.Utils or Layer.Config or Layer.Framework;
}
