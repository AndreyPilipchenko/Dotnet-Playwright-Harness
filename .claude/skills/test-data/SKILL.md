---
name: test-data
description: Use when adding, shaping, loading or reviewing test data in a Playwright C# framework built with this harness - the paired FeatureTestData.json file, its C# records, data for data-driven cases, enums, unique per-run values, credentials and secrets, uploaded files, or removing hard-coded inputs and expected values from tests.
---

# Test data

Every input and expected value a test uses comes from its own JSON file, except credentials,
which come from the environment, and values that must be unique per run, which are generated.

Read [design-conventions](../pom-architecture/references/design-conventions.md) first: §2
(file naming), §4 laws 5 and 9.

## 1. One file per test class, paired by name

`tests/<Name>.Tests/Ui/OrdersTests.cs` loads `resources/testData/OrdersTestData.json`, and nothing
else. `[test-data-pairing]` `[test-data-source]`

```csharp
private static readonly OrdersTestData Data = TestData.Load<OrdersTestData>("Orders");
```

- The folder is exactly `resources/testData/` (camelCase). The test csproj copies it next to the binaries. `[test-data-folder]`
- Load once per class, in a `static readonly` field. The file is read-only input: never write to it during a run.
- Business classes take data as parameters and never load it. `[no-test-data-in-business-layer]`

## 2. Shape it as records next to the test

```csharp
public sealed record OrdersTestData(UserData User, IReadOnlyDictionary<string, OrderCase> Orders);

public sealed record UserData(string Email);

public sealed record OrderCase(string Product, int Quantity, string Status);
```

```json
{
  "user": { "email": "qa.orders@example.com" },
  "orders": {
    "standard": { "product": "Pliers", "quantity": 1, "status": "Placed" },
    "express": { "product": "Hammer", "quantity": 2, "status": "Placed, express" }
  }
}
```

- JSON properties are camelCase. `TestData` matches them to the PascalCase record properties without regard to case.
- Group related values into nested records (`User`, `Orders`), not long flat lists.
- **Data-driven cases:** key them in a dictionary, and let the test's data rows name the key: `[TestCase("standard")]` with `Data.Orders[shipping]`. The rows stay short and the values stay in JSON.
- **Fixed option lists** become an enum with `[JsonConverter(typeof(JsonStringEnumConverter<Shipping>))]` on the type. Then `"express"` or `"Express"` in the JSON reads as `Shipping.Express`.

## 3. Nothing hard-coded in code

- No input strings, numbers or expected texts in a test body, an action or a validation.
- **Structure is not data:** a page's `Url`, locator names and endpoint paths stay in code.
- Deliberately invalid values (a wrong password, an over-long name) are synthetic data and belong in the JSON too.

## 4. Three kinds of values

| Kind | Example | Lives in |
|---|---|---|
| Static | product names, expected messages | the JSON |
| Environment | `BASE_URL`, passwords, API keys | environment variables, read through `Utils/Secrets` |
| Runtime | a new user's email, a created order's id | generated per run (`UniqueData`) or returned by an Api action |

```csharp
await checkout.SignInAndOpenOrdersAsync(Data.User.Email, Secrets.Get("SHOP_PASSWORD"));
```

- **Never** put a real credential, token or key in JSON or source. `[secret-literal]` The account named in JSON must be a disposable test account.
- **Unique per test and per run:** anything a test creates gets a generated value, so parallel and repeated runs never collide.

```csharp
public static string Email(string prefix) =>
    string.Create(CultureInfo.InvariantCulture, $"{prefix}+{Guid.NewGuid():N}@example.com");
```

- **Seed, then clean up:** records a test needs are created through an Api or Db class and deleted in teardown (service-classes). Never depend on data that happens to exist.
- **Payment data** is always the provider's published test card numbers. It is synthetic and grouped in one record. Never use a real card.

## 5. Files for uploads

Put them in `resources/files/<Feature>/`, and add a copy rule to the test csproj (it needs the
user's approval, like any template change):

```xml
<None Include="..\..\resources\files\**\*" LinkBase="files" CopyToOutputDirectory="PreserveNewest" />
```

Store the file name in the JSON, and build the path from it in the test:
`Path.Combine(AppContext.BaseDirectory, "files", "Orders", Data.InvoiceFile)`.

## Boundaries

- Where the data is used in a test → test-methods
- Creating and deleting records → service-classes
- `Secrets` and `UniqueData` themselves → utility-classes

## Review checklist

- [ ] One paired JSON file in `resources/testData/`, loaded once, never written to.
- [ ] Records shaped by meaning; cases keyed; enums for fixed option lists.
- [ ] No inputs or expected values hard-coded in code.
- [ ] Credentials come from the environment; created values are unique per run.
- [ ] Seeded records are cleaned up by the test that created them.
