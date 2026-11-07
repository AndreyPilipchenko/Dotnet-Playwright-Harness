namespace Harness.Tools;

/// <summary>
/// Exit codes for commands other than <c>hook</c>. Hooks use Claude Code's own codes:
/// 0 allows, 2 blocks (see <see cref="Hooks.HookRunner"/>).
/// </summary>
internal static class ExitCodes
{
    public const int Ok = 0;

    /// <summary>The command ran and found problems, or could not do its job.</summary>
    public const int Failed = 1;

    /// <summary>The command line was wrong (EX_USAGE).</summary>
    public const int Usage = 64;
}
