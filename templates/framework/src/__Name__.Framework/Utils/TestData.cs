using System.Text.Json;

namespace {{Name}}.Framework.Utils;

/// <summary>
/// Loads a test class's paired data file, <c>resources/testData/&lt;Feature&gt;TestData.json</c>.
/// The test project copies those files next to its binaries, under <c>testData/</c>.
/// </summary>
public static class TestData
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static T Load<T>(string feature)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "testData", feature + "TestData.json");
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, Options)
            ?? throw new InvalidOperationException($"{path} contains no data.");
    }
}
