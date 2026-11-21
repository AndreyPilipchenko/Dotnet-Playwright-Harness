using System.Text;
using Harness.Tools.Installing;

namespace Harness.Tools.Tests.Installing;

public class ClaudeMdTests
{
    private const string Line = "@.harness/templates/CLAUDE.harness.md";

    private static string PathIn(TempDir root) => root.Combine("CLAUDE.md");

    private static void WriteBytes(TempDir root, byte[] bytes) => File.WriteAllBytes(PathIn(root), bytes);

    [Fact]
    public void NoFile_IsCreated_WithTheImportLine()
    {
        using var root = new TempDir();

        var change = ClaudeMd.Ensure(PathIn(root));

        Assert.Equal(ClaudeMdChange.Created, change);
        var lines = File.ReadAllLines(PathIn(root));
        Assert.StartsWith("# ", lines[0], StringComparison.Ordinal);
        Assert.Equal(Line, lines[^1]);
    }

    [Fact]
    public void FileWithoutTheLine_GetsItAppended_AndKeepsItsText()
    {
        using var root = new TempDir();
        root.Write("CLAUDE.md", "# Team\n\nOur rules.\n");

        var change = ClaudeMd.Ensure(PathIn(root));

        Assert.Equal(ClaudeMdChange.Appended, change);
        Assert.Equal($"# Team\n\nOur rules.\n\n{Line}\n", File.ReadAllText(PathIn(root)));
    }

    [Fact]
    public void NoTrailingNewline_StartsTheImportOnItsOwnLine()
    {
        using var root = new TempDir();
        root.Write("CLAUDE.md", "Our rules.");

        ClaudeMd.Ensure(PathIn(root));

        Assert.Equal($"Our rules.\n\n{Line}\n", File.ReadAllText(PathIn(root)));
    }

    [Fact]
    public void CrlfFile_StaysCrlf()
    {
        using var root = new TempDir();
        root.Write("CLAUDE.md", "A\r\nB\r\n");

        ClaudeMd.Ensure(PathIn(root));

        Assert.Equal($"A\r\nB\r\n\r\n{Line}\r\n", File.ReadAllText(PathIn(root)));
    }

    [Fact]
    public void BomFile_KeepsItsBom()
    {
        using var root = new TempDir();
        WriteBytes(root, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# Team\n")]);

        ClaudeMd.Ensure(PathIn(root));

        var bytes = File.ReadAllBytes(PathIn(root));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal($"# Team\n\n{Line}\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void FileWithoutBom_GetsNone()
    {
        using var root = new TempDir();
        WriteBytes(root, Encoding.UTF8.GetBytes("# Team\n"));

        ClaudeMd.Ensure(PathIn(root));

        Assert.NotEqual(0xEF, File.ReadAllBytes(PathIn(root))[0]);
    }

    [Fact]
    public void LineAlreadyThere_EvenIndented_IsUnchanged()
    {
        using var root = new TempDir();
        var bytes = Encoding.UTF8.GetBytes($"x\n  {Line}  \n");
        WriteBytes(root, bytes);

        var change = ClaudeMd.Ensure(PathIn(root));

        Assert.Equal(ClaudeMdChange.Unchanged, change);
        Assert.Equal(bytes, File.ReadAllBytes(PathIn(root)));
    }

    [Fact]
    public void LineInsideASentence_DoesNotCount()
    {
        using var root = new TempDir();
        root.Write("CLAUDE.md", $"We import {Line} here.\n");

        Assert.Equal(ClaudeMdChange.Appended, ClaudeMd.Ensure(PathIn(root)));
    }

    [Fact]
    public void Rerun_IsByteIdentical()
    {
        using var root = new TempDir();
        root.Write("CLAUDE.md", "# Team\r\n");
        ClaudeMd.Ensure(PathIn(root));
        var first = File.ReadAllBytes(PathIn(root));

        var change = ClaudeMd.Ensure(PathIn(root));

        Assert.Equal(ClaudeMdChange.Unchanged, change);
        Assert.Equal(first, File.ReadAllBytes(PathIn(root)));
    }

    [Fact]
    public void Utf16File_IsLeftAlone_AndReported()
    {
        // Windows PowerShell 5.1 writes `"# rules" > CLAUDE.md` as UTF-16LE with a BOM.
        using var root = new TempDir();
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("# rules\r\n")).ToArray();
        WriteBytes(root, bytes);

        Assert.Equal(ClaudeMdChange.NotUtf8, ClaudeMd.Ensure(PathIn(root)));
        Assert.Equal(bytes, File.ReadAllBytes(PathIn(root)));
    }

    [Fact]
    public void InvalidUtf8_IsLeftAlone_AndReported()
    {
        // "café" in Windows-1252.
        using var root = new TempDir();
        byte[] bytes = [0x63, 0x61, 0x66, 0xE9, 0x0A];
        WriteBytes(root, bytes);

        Assert.Equal(ClaudeMdChange.NotUtf8, ClaudeMd.Ensure(PathIn(root)));
        Assert.Equal(bytes, File.ReadAllBytes(PathIn(root)));
    }

    [Fact]
    public void ReadOnlyFile_IsReported_AndInstallStillSucceeds()
    {
        using var root = new TempDir();
        root.Write("CLAUDE.md", "# Team\n");
        File.SetAttributes(PathIn(root), FileAttributes.ReadOnly);
        try
        {
            var result = Installer.Run(new InstallOptions(root.Path, "nunit", "Acme.Shop", null, Path.Combine(RepoPaths.Templates, "settings.harness.json")));

            Assert.Equal(ClaudeMdChange.WriteFailed, result.ClaudeMd);
            Assert.True(File.Exists(root.Combine(".claude", "settings.json")));
            Assert.Equal("# Team\n", File.ReadAllText(PathIn(root)));
        }
        finally
        {
            File.SetAttributes(PathIn(root), FileAttributes.Normal);
        }
    }

    [Fact]
    public void Install_ReportsTheClaudeMdChange()
    {
        using var root = new TempDir();

        var result = Installer.Run(new InstallOptions(root.Path, "nunit", "Acme.Shop", null, Path.Combine(RepoPaths.Templates, "settings.harness.json")));

        Assert.Equal(ClaudeMdChange.Created, result.ClaudeMd);
        Assert.True(File.Exists(PathIn(root)));
    }
}
