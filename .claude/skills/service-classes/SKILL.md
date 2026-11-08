---
name: service-classes
description: Use when creating, extending or reviewing an Api or Db class (<Domain>Api, <Domain>Db) in a Playwright C# framework built with this harness - API requests through IAPIRequestContext, endpoints, database queries and parameters, seeding test data through the API or database, cleaning up created records in teardown, API or DB checks, or connecting with credentials from the environment.
---

# Service classes (Apis and Dbs)

A service class owns one business domain on one channel: `OrdersApi` over HTTP, `OrdersDb` over
SQL. Tests use them to arrange state quickly, to check what the system stored, and to clean up.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §1, §3
(anatomy), §5 (seeding and cleanup), §8 (extension helpers).

## 1. One class per domain, per channel

- `Apis/<Domain>Api.cs` and `Dbs/<Domain>Db.cs`: `OrdersApi`, `UsersApi`, `OrdersDb`. Not `ApiClient`, not `Requests`.
- **Prefer the API for arranging state,** the DB when no API exists, and the GUI only for the behaviour under test (iron law 9).
- Same anatomy as a page: constants, fields, constructor or factory, `// Actions`, `// Validations`. `[anatomy-spacing]`

## 2. Api classes

```csharp
public sealed class OrdersApi : IAsyncDisposable
{
    private const string OrdersEndpoint = "/api/orders";

    private readonly IAPIRequestContext _request;

    private readonly List<string> _created = [];

    private OrdersApi(IAPIRequestContext request) => _request = request;

    public static async Task<OrdersApi> CreateAsync(IPlaywright playwright) =>
        new(await ApiContext.CreateAsync(playwright, "X-Api-Key", "SHOP_API_KEY"));

    // Actions

    public async Task<string> CreateOrderAsync(string product, int quantity)
    {
        var response = await _request.PostAsync(OrdersEndpoint, new() { DataObject = new { product, quantity } });
        var id = await response.ReadIdAsync($"POST {OrdersEndpoint}");
        _created.Add(id);
        return id;
    }

    public async Task DeleteCreatedOrdersAsync()
    {
        foreach (var id in _created)
        {
            await _request.DeleteAsync($"{OrdersEndpoint}/{id}");
        }

        _created.Clear();
    }

    public ValueTask DisposeAsync() => _request.DisposeAsync();

    // Validations

    public async Task VerifyOrderExistsAsync(string id) =>
        await Expect(await _request.GetAsync($"{OrdersEndpoint}/{id}")).ToBeOKAsync();
}
```

- **Endpoints are constants.** Paths relative to `BASE_URL`, which `ApiContext` sets.
- **The class owns its request context,** created by a static `CreateAsync` factory from the adapter's `Playwright`. Connection details and keys come from the environment through `Utils/ApiContext` and `Utils/Secrets`, so a test only ever holds the business class.
- **Inputs are parameters.** The payload is built from them, never hard-coded.
- **Return what the test uses,** such as the new id, and nothing else. Return business values (an id, a status), never the raw `IAPIResponse`.
- **Fail loudly on arrangement:** a failed create throws with the status (here through the `ReadIdAsync` extension). It never asserts and never retries.

## 3. Db classes

```csharp
public sealed class OrdersDb : IAsyncDisposable
{
    private const string CountByProductQuery = "SELECT COUNT(*) FROM orders WHERE product = @product";

    private readonly DbConnection _connection;

    public OrdersDb(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    // Actions

    public async Task<long> CountOrdersAsync(string product)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = CountByProductQuery;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@product";
        parameter.Value = product;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    // Validations

    public async Task VerifyOrderCountAsync(string product, long expected) =>
        Checks.Equal(expected, await CountOrdersAsync(product), $"orders for {product}");
}
```

- **Queries are constants with parameters.** Never interpolate a value into SQL.
- The class takes an open `DbConnection` (`System.Data.Common`, part of .NET). The database provider package (Npgsql, Microsoft.Data.SqlClient and so on) is **not** on the approved list. Ask the user before adding one (csharp-standards).
- Opening the connection from an environment variable is a Utils job, like `ApiContext`.
- Prefer read-only queries. A query that writes is for seeding and cleanup only, and touches only rows the test created.
- Database checks use `Checks.Equal`, because there is no web-first assertion for a value (validation-methods).

## 4. Clean up what you created

- The class records each id it creates and deletes exactly those in a cleanup action (`DeleteCreatedOrdersAsync`). It never deletes by pattern or wipes a table.
- The test class calls cleanup, then `DisposeAsync`, in its per-test teardown (test-classes). Teardown runs after a failure too.
- Cleanup is the one place where a loop is fine: over the ids this class owns.

## 5. What a service class never does

- Reference a test framework or `Assert`. `[layer-boundary]`
- Load test data. Values come in as parameters. `[no-test-data-in-business-layer]`
- `try`/`catch`, retries or sleeps. Those belong in Utils, if they are truly needed. `[no-try-outside-utils]` `[no-hard-wait]`
- Hold a secret in source. `[secret-literal]`
- Branch on responses to "handle" errors. Fail loudly and let the test report it.

## Boundaries

- `ApiContext`, `Secrets`, connection helpers → utility-classes
- Response helpers such as `ReadIdAsync` → extension-classes
- The validation side in detail → validation-methods
- Where the test sets up and tears down the service → test-classes

## Review checklist

- [ ] One domain per class, `<Domain>Api` or `<Domain>Db`, with the standard anatomy.
- [ ] Endpoints and queries are constants; queries are parameterized.
- [ ] Inputs are parameters; returns are business values used by tests.
- [ ] Arrangement failures throw with the status; no assertions in actions, no `try`, retries or sleeps.
- [ ] Every created record is tracked and deleted by the cleanup action.
- [ ] No new provider package without approval; credentials come from the environment.
