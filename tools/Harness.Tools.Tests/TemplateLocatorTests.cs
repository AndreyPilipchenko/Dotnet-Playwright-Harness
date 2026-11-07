namespace Harness.Tools.Tests;

public class TemplateLocatorTests
{
    [Fact]
    public void FindsTemplatesInAnAncestorFolder()
    {
        using var dir = new TempDir();
        dir.Write("templates/build/global.json", "{}");
        var deep = dir.Combine("tools", "Harness.Tools", "bin", "Debug");
        Directory.CreateDirectory(deep);

        Assert.Equal(dir.Combine("templates"), TemplateLocator.Find(deep));
    }

    [Fact]
    public void IgnoresATemplatesFolderWithoutBuildLayer()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("a", "templates"));
        var start = dir.Combine("a", "b");
        Directory.CreateDirectory(start);

        var found = TemplateLocator.Find(start);

        Assert.NotEqual(dir.Combine("a", "templates"), found);
    }

    [Fact]
    public void FindsThisRepositorysTemplates()
    {
        Assert.True(Directory.Exists(Path.Combine(RepoPaths.Templates, "build")));
    }
}
