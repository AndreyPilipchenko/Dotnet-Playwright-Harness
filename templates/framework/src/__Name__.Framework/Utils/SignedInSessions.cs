using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace {{Name}}.Framework.Utils;

/// <summary>
/// A role that UI tests can run as, and its one sign-in. The sign-in is chosen when the automation is
/// designed: <see cref="ThroughApi"/> when the application has an API sign-in, otherwise
/// <see cref="ThroughLoginPage"/>. There is no fallback from one to the other.
/// </summary>
public sealed record Role(string Name, Func<IBrowserContext, Task> SignIn)
{
    /// <summary>Signs in through the API on the role's context, for example a POST through <c>context.APIRequest</c>, which shares the context's cookies.</summary>
    public static Role ThroughApi(string name, Func<IBrowserContext, Task> signIn) => new(name, signIn);

    /// <summary>Signs in through the login page, for example <c>new LoginPage(page).LoginAsync(email, password)</c>.</summary>
    public static Role ThroughLoginPage(string name, Func<IPage, Task> signIn) => new(name, async context =>
    {
        var page = await context.NewPageAsync();
        await signIn(page);
    });
}

/// <summary>The test gets a fresh, signed-out browser context, even in a test class that has a role.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SignedOutAttribute : Attribute;

/// <summary>
/// Signs each role in once per run and keeps its Playwright storage state (cookies and local storage)
/// for the UI tests of that role. Storage state does not hold session storage: an application that
/// keeps its sign-in there needs the session fallback described in the utility-classes skill.
/// The files live in a folder of their own for this test process, under the output folder, so
/// parallel or repeated runs never share or delete each other's sessions. They hold tokens: never
/// copy them into results or reports.
/// </summary>
public static class SignedInSessions
{
    private static readonly string RunFolder =
        Path.Combine(AppContext.BaseDirectory, ".auth", Guid.NewGuid().ToString("N"));

    private static readonly ConcurrentDictionary<string, Exception?> Outcomes = new(StringComparer.Ordinal);

    /// <summary>
    /// Signs in every role, each in its own context, and saves its state. A role that fails is
    /// recorded, so only its own tests fail; the other roles still sign in. Two roles whose names
    /// would share a state file are refused before anyone signs in, so no role runs as another.
    /// </summary>
    public static async Task SaveAllAsync(IBrowser browser, BrowserNewContextOptions options, IReadOnlyList<Role> roles)
    {
        var clash = roles.GroupBy(r => FileName(r.Name), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new InvalidOperationException(
                $"Roles {string.Join(", ", clash.Select(r => $"'{r.Name}'"))} share the file name '{clash.Key}'. Give each role in Setup/Roles.cs a distinct name.");
        }

        Directory.CreateDirectory(RunFolder);
        foreach (var role in roles)
        {
            var context = await browser.NewContextAsync(options);
            try
            {
                await role.SignIn(context);
                await context.StorageStateAsync(new() { Path = FileFor(role.Name) });
                Outcomes[role.Name] = null;
            }
            catch (Exception ex)
            {
                Outcomes[role.Name] = ex;
            }
            finally
            {
                await context.CloseAsync();
            }
        }
    }

    /// <summary>The saved state of <paramref name="role"/>, for <c>StorageStatePath</c>. Throws when the role did not sign in.</summary>
    public static string PathFor(string role)
    {
        if (!Outcomes.TryGetValue(role, out var failure))
        {
            throw new InvalidOperationException($"Role '{role}' is not in Setup/Roles.cs, so it was never signed in.");
        }

        return failure is null
            ? FileFor(role)
            : throw new InvalidOperationException($"Role '{role}' could not sign in at the start of the run: {Reason(failure)}", failure);
    }

    // The first line of the message keeps the test report short; the whole exception is the inner exception.
    private static string Reason(Exception ex) =>
        ex.Message.Split('\n')[0].Trim() is { Length: > 0 } line ? line : ex.GetType().Name;

    /// <summary>Deletes this run's saved sessions. Other runs' folders are left alone.</summary>
    public static void DeleteAll()
    {
        if (Directory.Exists(RunFolder))
        {
            Directory.Delete(RunFolder, recursive: true);
        }
    }

    private static string FileFor(string role) => Path.Combine(RunFolder, FileName(role));

    private static string FileName(string role) =>
        string.Concat(role.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')) + ".json";
}
