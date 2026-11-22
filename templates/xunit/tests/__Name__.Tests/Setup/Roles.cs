using {{Name}}.Framework.Utils;

namespace {{Name}}.Tests.Setup;

/// <summary>
/// The roles UI tests can run as. Each role signs in once at the start of the run (SignInOnce), and a
/// test class picks one with <c>protected override string? Role => Roles.Customer;</c>. Choose each
/// role's sign-in when you design the automation: the API when the application has an API sign-in,
/// otherwise the login page. A role's sign-in that fails makes only that role's tests fail; it never
/// switches to the other way. Credentials come from <c>&lt;ROLE&gt;_EMAIL</c> and <c>&lt;ROLE&gt;_PASSWORD</c>.
/// </summary>
public static class Roles
{
    // Add a name and an entry per role, for example:
    //
    // public const string Customer = "customer";
    //
    // Role.ThroughApi(Customer, context => AuthApi.SignInAsync(context.APIRequest, Secrets.Get("CUSTOMER_EMAIL"), Secrets.Get("CUSTOMER_PASSWORD"))),
    //
    // ThroughApi posts on the context's own request object, so the session cookie lands in the browser
    // context. That needs the sign-in API on the same origin as the app (BASE_URL). AuthApi.SignInAsync
    // borrows the request object and never disposes it.
    //
    // An application without an API sign-in uses its login page instead:
    // Role.ThroughLoginPage(Customer, async page =>
    // {
    //     await new LoginPage(page).LoginAsync(Secrets.Get("CUSTOMER_EMAIL"), Secrets.Get("CUSTOMER_PASSWORD"));
    //     await new AccountPage(page).VerifyIsOpenAsync(); // the state is saved next, so wait until the session exists
    // }),
    public static IReadOnlyList<Role> All { get; } = [];
}
