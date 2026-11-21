using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HarnessTemplates.Framework.Utils;

namespace Harness.Tools.Tests.Templates;

/// <summary>
/// Runs the skeleton's real Redaction.cs, which the csproj renders into this assembly.
/// Remembered values are global, so every test uses secrets of its own.
/// </summary>
public class RedactionTests
{
    [Fact]
    public void Mask_JsonFieldLevelZero_MasksTheValue() =>
        Assert.Equal("""{"password":"***"}""", Redaction.Mask("""{"password":"hunter2-a"}"""));

    [Fact]
    public void Mask_JsonFieldLevelOne_KeepsEscapedQuotesInside() =>
        Assert.Equal(
            """{"body":"{\"access_token\":\"***\"}"}""",
            Redaction.Mask("""{"body":"{\"access_token\":\"ab\\\"cd\"}"}"""));

    [Fact]
    public void Mask_JsonFieldLevelThree_MasksTheValue() =>
        Assert.Equal(@"\\\""password\\\"":\\\""***\\\""", Redaction.Mask(@"\\\""password\\\"":\\\""s3cret-b\\\"""));

    [Fact]
    public void Mask_RememberedValue_MasksWholeValuesOnly()
    {
        Redaction.Remember("Rem-1234-c");

        Assert.Equal("a *** b Rem-1234-cd", Redaction.Mask("a Rem-1234-c b Rem-1234-cd"));
    }

    [Fact]
    public void MaskZip_TypedPassword_IsMaskedInEveryTextEntry()
    {
        using var dir = new TempDir();
        var zip = Zip(
            dir.Path,
            ("trace.trace", Utf8("""{"type":"frame-snapshot","attrs":{"__playwright_value_":"Typed-Pw-91","type":"password"}}""")),
            ("trace.network", Utf8("""{"params":{"value":"Typed-Pw-91"}}""")));

        Redaction.MaskZip(zip);

        Assert.All(["trace.trace", "trace.network"], name =>
        {
            var text = Entry(zip, name);
            Assert.DoesNotContain("Typed-Pw-91", text, StringComparison.Ordinal);
            using var parsed = JsonDocument.Parse(text);
        });
    }

    [Fact]
    public void MaskZip_BinaryEntry_IsCopiedUnchanged()
    {
        using var dir = new TempDir();
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0x00, 0x10, 0x4A, 0x46];
        var zip = Zip(
            dir.Path,
            ("trace.trace", Utf8("""{"type":"frame-snapshot","attrs":{"__playwright_value_":"Typed-Pw-92","type":"password"}}""")),
            ("screencast/a.jpeg", jpeg));

        Redaction.MaskZip(zip);

        using var archive = ZipFile.OpenRead(zip);
        using var input = archive.GetEntry("screencast/a.jpeg")!.Open();
        using var copy = new MemoryStream();
        input.CopyTo(copy);
        Assert.Equal(jpeg, copy.ToArray());
    }

    [Fact]
    public void Mask_RawNewlineInValue_MasksToEndOfLine() =>
        Assert.Equal("{\"password\":\"***\ndef\"}", Redaction.Mask("{\"password\":\"abc-d\ndef\"}"));

    [Fact]
    public void Mask_CrLfInValue_KeepsTheLineEnding() =>
        Assert.Equal("{\"password\":\"***\r\nx", Redaction.Mask("{\"password\":\"abc-e\r\nx"));

    [Fact]
    public void Mask_UnclosedValueAtEnd_MasksToEnd() =>
        Assert.Equal("""log: "password":"***""", Redaction.Mask("""log: "password":"abc-f"""));

    [Fact]
    public void Mask_BareQuoteInsideLevelOne_MasksUpToIt() =>
        Assert.Equal("""\"password\":\"***"rest""", Redaction.Mask("""\"password\":\"abc-g"rest"""));

    [Fact]
    public void MaskZip_WellFormedTraceLines_StillParse()
    {
        using var dir = new TempDir();
        var zip = Zip(dir.Path, ("trace.trace", Utf8("{\"password\":\"p-1x\"}\n{\"m\":\"{\\\"token\\\":\\\"t-2x\\\"}\"}")));

        Redaction.MaskZip(zip);

        var lines = Entry(zip, "trace.trace").Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line =>
        {
            Assert.DoesNotContain("p-1x", line, StringComparison.Ordinal);
            Assert.DoesNotContain("t-2x", line, StringComparison.Ordinal);
            using var parsed = JsonDocument.Parse(line);
        });
    }

    [Fact]
    public void Mask_LongBackslashRun_IsLinear()
    {
        // The quote matters: each start inside the run walks to it and back.
        var text = new string('\\', 300_000) + "\"x";
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var masked = Redaction.Mask(text);

        clock.Stop();
        Assert.Equal(text, masked);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Mask took {clock.Elapsed}.");
    }

    [Fact]
    public void Mask_BackslashBeforeFieldName_StillMasks() =>
        Assert.Equal("""C:\"password":"***" """, Redaction.Mask("""C:\"password":"x-h" """));

    // Final review finding: a nested field after extra literal backslashes was left unmasked.
    [Fact]
    public void Mask_BackslashBeforeNestedFieldName_StillMasks() =>
        Assert.Equal("""{"text":"C:\\\"password\":\"***\""}""", Redaction.Mask("""{"text":"C:\\\"password\":\"hunter2-i\""}"""));

    [Fact]
    public void Mask_BackslashBeforeLevelThreeFieldName_StillMasks() =>
        Assert.Equal(@"\\\\\\\""password\\\"":\\\""***\\\""", Redaction.Mask(@"\\\\\\\""password\\\"":\\\""zz7-j\\\"""));

    // Final review finding: an even run of backslashes before a quote closes the outer string, so the value ends there.
    [Fact]
    public void MaskZip_ValueEndingInBackslashInsideString_StillParses()
    {
        using var dir = new TempDir();
        var zip = Zip(dir.Path, ("trace.trace", Utf8("""{"m":"\"password\":\"abc-k\\","n":"keep"}""")));

        Redaction.MaskZip(zip);

        var line = Entry(zip, "trace.trace");
        Assert.Equal("""{"m":"\"password\":\"***","n":"keep"}""", line);
        using var parsed = JsonDocument.Parse(line);
    }

    // Final review finding: a field that starts inside an earlier masked value still masks its own value.
    [Fact]
    public void Mask_FieldStraddlingAnEarlierMaskedValue_StillMasks() =>
        Assert.DoesNotContain("leak9-l", Redaction.Mask("\"token\":\"abc \\\"password\\\"\n:\\\"leak9-l\\\""), StringComparison.Ordinal);

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static string Zip(string dir, params (string Name, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(dir, "trace.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var output = archive.CreateEntry(name).Open();
            output.Write(bytes);
        }

        return path;
    }

    private static string Entry(string zipPath, string name)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
