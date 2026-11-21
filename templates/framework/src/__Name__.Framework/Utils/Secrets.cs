namespace {{Name}}.Framework.Utils;

/// <summary>
/// Credentials come from the environment, never from code, data files or Config. Every value handed
/// out is remembered by <see cref="Redaction"/>, so it is masked in browser diagnostics and traces.
/// </summary>
public static class Secrets
{
    /// <summary>Returns the variable's value, or throws naming the variable (never a value) when it is missing.</summary>
    public static string Get(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Set the {name} environment variable.");
        }

        Redaction.Remember(value);
        return value;
    }
}
