using System.Text.RegularExpressions;
using Harness.Tools.Hooks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Harness.Tools.Conventions.SourceFile;

namespace Harness.Tools.Conventions;

/// <summary>
/// The convention rules for a generated project. Every rule works on syntax only, so it is fast and
/// needs no build, but it also cannot see types: a rule matches names as they are written.
/// </summary>
internal static partial class ConventionRules
{
    public static readonly IReadOnlyList<string> DefaultCategories = ["smoke", "regression"];

    private static readonly Layer[] AnyLayer =
        [Layer.Tests, Layer.Setup, Layer.Pages, Layer.Apis, Layer.Dbs, Layer.Helpers, Layer.Utils, Layer.Config, Layer.Framework];

    private static readonly Layer[] FrameworkLayers =
        [Layer.Pages, Layer.Apis, Layer.Dbs, Layer.Helpers, Layer.Utils, Layer.Config, Layer.Framework];

    private static readonly Layer[] TestLayers = [Layer.Tests, Layer.Setup];
    private static readonly Layer[] TestsOnly = [Layer.Tests];
    private static readonly Layer[] PagesOnly = [Layer.Pages];
    private static readonly Layer[] Business = [Layer.Pages, Layer.Apis, Layer.Dbs, Layer.Helpers];

    private static readonly string[] SkipCalls = ["Ignore", "Inconclusive", "Skip", "SkipWhen", "SkipUnless"];
    private static readonly string[] AssertTypes = ["Assert", "ClassicAssert", "StringAssert", "CollectionAssert"];
    private static readonly string[] HttpTypes = ["HttpClient", "HttpRequestMessage", "APIRequest", "IAPIRequestContext"];
    private static readonly string[] OutputTypes = ["Console", "Debug", "Trace"];
    private static readonly string[] InternalNamespaces = ["Microsoft.Playwright.Core", "Microsoft.Playwright.Transport", "Microsoft.Playwright.Helpers"];

    private static readonly string[] TestNamespaces =
    [
        "NUnit", "Xunit", "Microsoft.VisualStudio.TestTools", "Microsoft.Playwright.NUnit", "Microsoft.Playwright.Xunit", "Microsoft.Playwright.MSTest",
    ];

    private static readonly string[] TestPackages =
    [
        "NUnit", "xunit", "MSTest", "Microsoft.NET.Test.Sdk", "Microsoft.Playwright.NUnit", "Microsoft.Playwright.Xunit", "Microsoft.Playwright.MSTest",
    ];

    private static readonly string[] BusinessNamespaceSegments = ["Pages", "Apis", "Dbs", "Helpers"];

    private static readonly string[] LocatorSuffixes =
    [
        "Button", "Input", "Link", "Header", "Heading", "Text", "Image", "Icon", "Checkbox", "Radio", "Dropdown", "List",
        "Table", "Row", "Cell", "Form", "Message", "Tab", "Card", "Modal", "Section",
    ];

    private static readonly string[] ActionCalls =
    [
        "ClickAsync", "DblClickAsync", "FillAsync", "TypeAsync", "PressAsync", "PressSequentiallyAsync", "CheckAsync", "UncheckAsync",
        "SetCheckedAsync", "SelectOptionAsync", "SetInputFilesAsync", "HoverAsync", "TapAsync", "DragToAsync", "ClearAsync", "GotoAsync",
    ];

    private static readonly Dictionary<string, string> FrameworkAttributes = new(StringComparer.Ordinal)
    {
        ["Test"] = "nunit", ["TestCase"] = "nunit", ["TestCaseSource"] = "nunit", ["TestFixture"] = "nunit", ["Category"] = "nunit",
        ["Explicit"] = "nunit", ["SetUp"] = "nunit", ["TearDown"] = "nunit", ["OneTimeSetUp"] = "nunit", ["OneTimeTearDown"] = "nunit",
        ["Parallelizable"] = "nunit",
        ["Fact"] = "xunit", ["Trait"] = "xunit", ["InlineData"] = "xunit", ["MemberData"] = "xunit", ["ClassData"] = "xunit",
        ["TestMethod"] = "mstest", ["DataTestMethod"] = "mstest", ["DataRow"] = "mstest", ["TestClass"] = "mstest",
        ["TestCategory"] = "mstest", ["TestInitialize"] = "mstest", ["TestCleanup"] = "mstest", ["ClassInitialize"] = "mstest",
        ["Parallelize"] = "mstest",
    };

    public static IReadOnlyList<ConventionRule> All { get; } =
    [
        new("no-skipped-test", Severity.Fail,
            "A skipped test hides a gap. Fix it, delete it, or track the defect with a bug: category instead of skipping.",
            TestLayers, NoSkippedTest),
        new("no-hard-wait", Severity.Fail,
            "Fixed sleeps make tests slow and flaky. Wait for a condition with Expect or a locator wait; a retry helper in Utils may sleep.",
            [Layer.Tests, Layer.Setup, .. Business], NoHardWait),
        new("no-try-outside-utils", Severity.Fail,
            "try/catch in tests and business classes swallows failures. Put deliberate error handling in a Utils helper.",
            [Layer.Tests, .. Business], NoTry),
        new("playwright-internal-api", Severity.Fail,
            "Playwright's internal namespaces are not a public API and break on upgrade.",
            AnyLayer, PlaywrightInternal, Exemptable: false, Baselinable: false),
        new("no-assertion-in-test", Severity.Fail,
            "Tests read as steps; checks live in page, API or DB classes as Verify* methods.",
            TestsOnly, NoAssertionInTest),
        new("no-locator-in-test", Severity.Fail,
            "Locators belong to page classes, so a UI change is fixed in one place.",
            TestsOnly, NoLocatorInTest),
        new("no-navigation-in-test", Severity.Warn,
            "Navigation belongs to the page class: call its NavigateAsync.",
            TestsOnly, (f, _) => Calls(f, c => Name(c) == "GotoAsync")),
        new("page-goto-owner", Severity.Warn,
            "A page navigates to itself only in NavigateAsync; going elsewhere belongs to the page that is opened.",
            PagesOnly, PageGotoOwner),
        new("no-http-in-test", Severity.Fail,
            "HTTP calls belong to API classes in the framework project.",
            TestsOnly, NoHttpInTest),
        new("no-id-engine-prefix", Severity.Fail,
            "Use GetByRole, GetByLabel or GetByTestId, or a #id CSS selector, instead of the id= engine prefix.",
            PagesOnly, NoIdEnginePrefix),
        new("no-console-output", Severity.Fail,
            "Console output is lost in parallel runs. Use the test framework's output or a logger in Utils.",
            [Layer.Tests, Layer.Setup, Layer.Pages, Layer.Apis, Layer.Dbs], NoConsoleOutput),
        new("no-persistent-dialog-handler", Severity.Fail,
            "A lambda added to Page.Dialog is never removed and handles every later dialog. Add a named handler, run the action, then remove it with -=.",
            PagesOnly, NoPersistentDialogHandler),
        new("locator-in-method-body", Severity.Warn,
            "Declare each locator once as a private ILocator property (or a method returning ILocator) and use it from actions.",
            PagesOnly, LocatorInMethodBody),
        new("timeout-literal", Severity.Warn,
            "A timeout number hides why it is needed. Name it in a Timeouts constant in Config.",
            Business, TimeoutLiteral),
        new("secret-literal", Severity.Fail,
            "Secrets come from environment variables or user-secrets, never from source.",
            AnyLayer, SecretLiteral, Exemptable: false, Baselinable: false),
        new("test-data-pairing", Severity.Fail,
            "Each test class has its data in resources/testData/<Feature>TestData.json.",
            TestsOnly, TestDataPairing),
        new("test-data-source", Severity.Fail,
            "A test class loads only its own data file, through TestData.Load(\"<Feature>\").",
            TestsOnly, TestDataSource),
        new("no-test-data-in-business-layer", Severity.Fail,
            "Page, API and DB classes take data as parameters; only tests load test data.",
            Business, NoTestDataInBusinessLayer),
        new("test-class-naming", Severity.Warn,
            "A test class is named <Feature>Tests and lives in <Feature>Tests.cs, so its data file can be found.",
            TestsOnly, TestClassNaming),
        new("category-vocabulary", Severity.Warn,
            "Categories drive the CI filters; use the project vocabulary (.harness/project.json) or bug:<id>.",
            TestLayers, CategoryVocabulary),
        new("locator-naming", Severity.Fail,
            "Locator members are PascalCase and end in their element kind (EmailInput, SaveButton), so actions read clearly.",
            PagesOnly, LocatorNaming),
        new("locator-visibility", Severity.Fail,
            "Locators are private; tests use the page's actions and Verify* methods.",
            PagesOnly, LocatorVisibility),
        new("anatomy-spacing", Severity.Fail,
            "Members are separated by one blank line.",
            Business, AnatomySpacing),
        new("layer-boundary", Severity.Fail,
            "The framework project must not depend on a test framework or on the tests, and Utils/Config must not depend on business classes.",
            FrameworkLayers, LayerBoundary, TestPackagesInFrameworkProjects),
        new("no-base-page", Severity.Fail,
            "Pages compose shared parts instead of inheriting from a base page; implementing an interface is fine.",
            PagesOnly, NoBasePage),
        new("test-logic", Severity.Fail,
            "A test is a straight line of steps. Branches and loops belong in data (one row per case) or in helpers.",
            TestsOnly, TestLogic),
        new("async-discipline", Severity.Fail,
            "Blocking on async code (.Result, .Wait(), GetResult()) or async void deadlocks and loses exceptions.",
            AnyLayer, AsyncDiscipline),
        new("async-naming", Severity.Fail,
            "A method returning Task or ValueTask ends in Async.",
            AnyLayer, AsyncNaming),
        new("action-verify-separation", Severity.Warn,
            "Verify* methods only check, and actions never assert, so a test shows what it does and what it checks.",
            [Layer.Pages, Layer.Apis, Layer.Dbs], ActionVerifySeparation),
        new("mixed-test-frameworks", Severity.Fail,
            "A project uses one test framework, the one recorded in .harness/project.json.",
            TestLayers, MixedTestFrameworks),
        new("test-data-folder", Severity.Fail,
            "Test data lives in resources/testData (camelCase); a resources/test-data folder is not copied to the output.",
            [], File: null, Repository: TestDataFolder),
    ];

    public static ConventionRule ById(string id) => All.First(r => r.Id == id);

    // ── file checks ──────────────────────────────────────────────────────

    private static IEnumerable<RuleHit> NoSkippedTest(SourceFile f, RuleContext _)
    {
        foreach (var attribute in f.Nodes<AttributeSyntax>())
        {
            var skipArgument = attribute.ArgumentList?.Arguments.Any(a => a.NameEquals?.Name.Identifier.Text is "Skip" or "SkipWhen" or "SkipUnless") == true;
            if (AttributeName(attribute) is "Ignore" or "Explicit" || skipArgument)
            {
                yield return Hit(attribute);
            }
        }

        foreach (var hit in Calls(f, c => ReceiverIs(c, "Assert") && SkipCalls.Contains(Name(c))))
        {
            yield return hit;
        }
    }

    private static IEnumerable<RuleHit> NoHardWait(SourceFile f, RuleContext _) => Calls(f, c =>
        Name(c) == "WaitForTimeoutAsync"
        || (Name(c) == "Sleep" && ReceiverIs(c, "Thread"))
        || (Name(c) == "Delay" && ReceiverIs(c, "Task")));

    private static IEnumerable<RuleHit> NoTry(SourceFile f, RuleContext _) =>
        f.Nodes<TryStatementSyntax>().Select(t => new RuleHit(LineOf(t.TryKeyword)));

    private static IEnumerable<RuleHit> PlaywrightInternal(SourceFile f, RuleContext _) =>
        f.Nodes<QualifiedNameSyntax>()
            .Where(n => n.Parent is not QualifiedNameSyntax)
            .Where(n => InternalNamespaces.Any(ns => IsNamespaceOrChild(Unglobal(n.ToString()), ns)))
            .Select(Hit);

    private static IEnumerable<RuleHit> NoAssertionInTest(SourceFile f, RuleContext _) => Calls(f, c =>
        IsExpect(c)
        || Name(c) == "Should"
        || (AssertTypes.Any(t => ReceiverIs(c, t)) && !(ReceiverIs(c, "Assert") && SkipCalls.Contains(Name(c)))));

    private static IEnumerable<RuleHit> NoLocatorInTest(SourceFile f, RuleContext _) => Calls(f, IsLocatorCall);

    private static IEnumerable<RuleHit> PageGotoOwner(SourceFile f, RuleContext _) => Calls(f, c =>
        Name(c) == "GotoAsync" && EnclosingMember(c) is not MethodDeclarationSyntax { Identifier.Text: "NavigateAsync" });

    private static IEnumerable<RuleHit> NoHttpInTest(SourceFile f, RuleContext _) =>
        f.Nodes<IdentifierNameSyntax>().Where(i => HttpTypes.Contains(i.Identifier.Text)).Select(Hit)
            .Concat(Calls(f, c => Name(c) == "FetchAsync"));

    private static IEnumerable<RuleHit> NoIdEnginePrefix(SourceFile f, RuleContext _) => Calls(f, c =>
        Name(c) == "Locator"
        && c.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
        && literal.IsKind(SyntaxKind.StringLiteralExpression)
        && literal.Token.ValueText.TrimStart().StartsWith("id=", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<RuleHit> NoConsoleOutput(SourceFile f, RuleContext _) => Calls(f, c =>
        Name(c).StartsWith("Write", StringComparison.Ordinal) && OutputTypes.Any(t => ReceiverIs(c, t)));

    private static IEnumerable<RuleHit> NoPersistentDialogHandler(SourceFile f, RuleContext _) =>
        f.Nodes<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(SyntaxKind.AddAssignmentExpression)
                && a.Right is AnonymousFunctionExpressionSyntax
                && a.Left switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.Text == "Dialog",
                    IdentifierNameSyntax name => name.Identifier.Text == "Dialog",
                    _ => false,
                })
            .Select(Hit);

    private static IEnumerable<RuleHit> LocatorInMethodBody(SourceFile f, RuleContext _) => Calls(f, c =>
        IsLocatorCall(c) && EnclosingMember(c) switch
        {
            ConstructorDeclarationSyntax => true,
            MethodDeclarationSyntax method => !IsLocatorType(method.ReturnType),
            _ => false,
        });

    private static IEnumerable<RuleHit> TimeoutLiteral(SourceFile f, RuleContext _) =>
        f.Nodes<AssignmentExpressionSyntax>()
            .Where(a => a.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && a.Left is IdentifierNameSyntax { Identifier.Text: "Timeout" }
                && a.Right is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.NumericLiteralExpression))
            .Select(Hit);

    private static IEnumerable<RuleHit> SecretLiteral(SourceFile f, RuleContext _) =>
        SecretScanner.Scan(f.Text).Select(s => new RuleHit(s.Line, "[redacted]", s.HighConfidence ? Severity.Fail : Severity.Warn));

    private static IEnumerable<RuleHit> TestDataPairing(SourceFile f, RuleContext context)
    {
        var testClass = f.TestClasses.FirstOrDefault();
        if (testClass is null || System.IO.File.Exists(Path.Combine(context.Root, "resources", "testData", f.Feature + "TestData.json")))
        {
            return [];
        }

        return [new RuleHit(LineOf(testClass.Identifier))];
    }

    private static IEnumerable<RuleHit> TestDataSource(SourceFile f, RuleContext _) => Calls(f, c =>
        (Name(c) == "Load" && ReceiverIs(c, "TestData") && !IsStringLiteral(c.ArgumentList.Arguments.FirstOrDefault()?.Expression, f.Feature))
        || ReceiverIs(c, "File"));

    private static IEnumerable<RuleHit> NoTestDataInBusinessLayer(SourceFile f, RuleContext _) =>
        Calls(f, c => Name(c) == "Load" && ReceiverIs(c, "TestData"))
            .Concat(f.Nodes<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression) && l.Token.ValueText.Contains("testData", StringComparison.OrdinalIgnoreCase))
                .Select(Hit));

    private static IEnumerable<RuleHit> TestClassNaming(SourceFile f, RuleContext _)
    {
        var fileName = Path.GetFileNameWithoutExtension(f.RelativePath);
        return f.TestClasses
            .Where(c => !c.Identifier.Text.EndsWith("Tests", StringComparison.Ordinal) || c.Identifier.Text != fileName)
            .Select(c => new RuleHit(LineOf(c.Identifier)));
    }

    private static IEnumerable<RuleHit> CategoryVocabulary(SourceFile f, RuleContext context)
    {
        foreach (var attribute in f.Nodes<AttributeSyntax>())
        {
            var category = AttributeName(attribute) switch
            {
                "Category" or "TestCategory" => StringArgument(attribute, 0),
                "Trait" when StringArgument(attribute, 0) == "Category" => StringArgument(attribute, 1),
                _ => null,
            };

            if (category is not null && !context.Categories.Contains(category, StringComparer.Ordinal) && !BugCategory().IsMatch(category))
            {
                yield return Hit(attribute);
            }
        }
    }

    private static IEnumerable<RuleHit> LocatorNaming(SourceFile f, RuleContext _) =>
        LocatorMembers(f)
            .Where(m => !PascalCase().IsMatch(m.Name.Text) || !LocatorSuffixes.Any(s => m.Name.Text.EndsWith(s, StringComparison.Ordinal)))
            .Select(m => new RuleHit(LineOf(m.Name)));

    private static IEnumerable<RuleHit> LocatorVisibility(SourceFile f, RuleContext _) =>
        LocatorMembers(f)
            .Where(m => m.Modifiers.Any(t => t.Kind() is SyntaxKind.PublicKeyword or SyntaxKind.InternalKeyword or SyntaxKind.ProtectedKeyword))
            .Select(m => new RuleHit(LineOf(m.Name)));

    private static IEnumerable<RuleHit> AnatomySpacing(SourceFile f, RuleContext _)
    {
        foreach (var type in f.Nodes<BaseTypeDeclarationSyntax>())
        {
            if (type.OpenBraceToken.IsMissing || type.OpenBraceToken.IsKind(SyntaxKind.None))
            {
                continue;
            }

            var open = LineOf(type.OpenBraceToken);
            var close = LineOf(type.CloseBraceToken);
            for (var line = open + 2; line < close; line++)
            {
                if (string.IsNullOrWhiteSpace(f.Lines[line - 1]) && string.IsNullOrWhiteSpace(f.Lines[line - 2]))
                {
                    yield return new RuleHit(line);
                }
            }
        }
    }

    private static IEnumerable<RuleHit> LayerBoundary(SourceFile f, RuleContext _)
    {
        foreach (var directive in f.Nodes<UsingDirectiveSyntax>())
        {
            var name = Unglobal(directive.NamespaceOrType.ToString());
            var segments = name.Split('.');
            var testDependency = TestNamespaces.Any(ns => IsNamespaceOrChild(name, ns)) || segments.Contains("Tests");
            var businessDependency = f.Layer is Layer.Utils or Layer.Config && segments.Skip(1).Any(BusinessNamespaceSegments.Contains);
            if (testDependency || businessDependency)
            {
                yield return Hit(directive);
            }
        }
    }

    private static IEnumerable<RuleHit> NoBasePage(SourceFile f, RuleContext _)
    {
        foreach (var type in f.Nodes<ClassDeclarationSyntax>())
        {
            var name = type.Identifier.Text;
            var abstractOrNamed = type.Modifiers.Any(SyntaxKind.AbstractKeyword)
                || name.Contains("BasePage", StringComparison.Ordinal)
                || name.Contains("PageBase", StringComparison.Ordinal);
            var inherits = type.BaseList?.Types.Any(t => !InterfaceName().IsMatch(SimpleTypeName(t.Type))) == true;
            if (abstractOrNamed || inherits)
            {
                yield return new RuleHit(LineOf(type.Identifier));
            }
        }
    }

    private static IEnumerable<RuleHit> TestLogic(SourceFile f, RuleContext _) =>
        f.Root.DescendantNodes()
            .Where(n => n is IfStatementSyntax or SwitchStatementSyntax or ForStatementSyntax or CommonForEachStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax or ConditionalExpressionSyntax or SwitchExpressionSyntax)
            .Select(Hit);

    private static IEnumerable<RuleHit> AsyncDiscipline(SourceFile f, RuleContext _)
    {
        foreach (var access in f.Nodes<MemberAccessExpressionSyntax>())
        {
            if (access.Name.Identifier.Text == "Result" && access.Expression is InvocationExpressionSyntax call
                && Name(call).EndsWith("Async", StringComparison.Ordinal))
            {
                yield return Hit(access);
            }
        }

        foreach (var call in f.Invocations)
        {
            var blocksOnWait = Name(call) == "Wait" && call.Expression is MemberAccessExpressionSyntax && call.ArgumentList.Arguments.Count == 0;
            var blocksOnResult = Name(call) == "GetResult"
                && call.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax awaiter }
                && Name(awaiter) == "GetAwaiter";
            if (blocksOnWait || blocksOnResult)
            {
                yield return Hit(call);
            }
        }

        foreach (var method in f.Nodes<MethodDeclarationSyntax>())
        {
            if (method.Modifiers.Any(SyntaxKind.AsyncKeyword) && method.ReturnType is PredefinedTypeSyntax predefined
                && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
            {
                yield return new RuleHit(LineOf(method.Identifier));
            }
        }
    }

    private static IEnumerable<RuleHit> AsyncNaming(SourceFile f, RuleContext _) =>
        f.Nodes<MethodDeclarationSyntax>()
            .Where(m => !m.Modifiers.Any(SyntaxKind.OverrideKeyword)
                && SimpleTypeName(m.ReturnType) is "Task" or "ValueTask"
                && !m.Identifier.Text.EndsWith("Async", StringComparison.Ordinal))
            .Select(m => new RuleHit(LineOf(m.Identifier)));

    private static IEnumerable<RuleHit> ActionVerifySeparation(SourceFile f, RuleContext _)
    {
        foreach (var method in f.Nodes<MethodDeclarationSyntax>())
        {
            var verifies = method.Identifier.Text.StartsWith("Verify", StringComparison.Ordinal);
            foreach (var call in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (verifies ? ActionCalls.Contains(Name(call)) : IsExpect(call))
                {
                    yield return Hit(call);
                }
            }
        }
    }

    private static IEnumerable<RuleHit> MixedTestFrameworks(SourceFile f, RuleContext context)
    {
        string? first = null;
        var reportedMix = false;
        var reportedMismatch = false;
        foreach (var attribute in f.Nodes<AttributeSyntax>())
        {
            if (!FrameworkAttributes.TryGetValue(AttributeName(attribute), out var framework))
            {
                continue;
            }

            if (!reportedMismatch && context.Framework is { } expected && !string.Equals(framework, expected, StringComparison.OrdinalIgnoreCase))
            {
                reportedMismatch = true;
                yield return Hit(attribute);
            }

            first ??= framework;
            if (!reportedMix && framework != first)
            {
                reportedMix = true;
                yield return Hit(attribute);
            }
        }
    }

    // ── repository checks ────────────────────────────────────────────────

    private static IEnumerable<Finding> TestDataFolder(ConventionRule rule, RuleContext context)
    {
        if (Directory.Exists(Path.Combine(context.Root, "resources", "test-data")))
        {
            yield return new Finding(rule.Id, rule.Severity, "resources/test-data", 1, rule.Why, "resources/test-data/");
        }
    }

    private static IEnumerable<Finding> TestPackagesInFrameworkProjects(ConventionRule rule, RuleContext context)
    {
        var src = Path.Combine(context.Root, "src");
        if (!Directory.Exists(src))
        {
            yield break;
        }

        foreach (var project in Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(context.Root, project).Replace('\\', '/');
            if (LayerMap.IsBuildOutput(relative))
            {
                continue;
            }

            var lines = System.IO.File.ReadAllLines(project);
            for (var i = 0; i < lines.Length; i++)
            {
                var reference = PackageReference().Match(lines[i]);
                if (reference.Success && TestPackages.Any(p => IsNamespaceOrChild(reference.Groups[1].Value, p, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return new Finding(rule.Id, rule.Severity, relative, i + 1, rule.Why, lines[i].Trim());
                }
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static RuleHit Hit(SyntaxNode node) => new(LineOf(node));

    private static IEnumerable<RuleHit> Calls(SourceFile f, Func<InvocationExpressionSyntax, bool> predicate) =>
        f.Invocations.Where(predicate).Select(Hit);

    private static bool IsExpect(InvocationExpressionSyntax call) =>
        Name(call) == "Expect" && (call.Expression is IdentifierNameSyntax || ReceiverIs(call, "Assertions"));

    private static bool IsLocatorCall(InvocationExpressionSyntax call)
    {
        var name = Name(call);
        return name is "Locator" or "FrameLocator" or "QuerySelectorAsync" or "QuerySelectorAllAsync"
            || name.StartsWith("GetBy", StringComparison.Ordinal);
    }

    private static bool IsLocatorType(TypeSyntax type) => SimpleTypeName(type) == "ILocator";

    private static bool IsStringLiteral(ExpressionSyntax? expression, string value) =>
        expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression) && literal.Token.ValueText == value;

    private static bool IsNamespaceOrChild(string name, string root, StringComparison comparison = StringComparison.Ordinal) =>
        name.Equals(root, comparison) || name.StartsWith(root + ".", comparison);

    private static string Unglobal(string name) => name.StartsWith("global::", StringComparison.Ordinal) ? name["global::".Length..] : name;

    private static IEnumerable<(SyntaxToken Name, SyntaxTokenList Modifiers)> LocatorMembers(SourceFile f)
    {
        foreach (var member in f.Nodes<MemberDeclarationSyntax>())
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property when IsLocatorType(property.Type):
                    yield return (property.Identifier, property.Modifiers);
                    break;
                case MethodDeclarationSyntax method when IsLocatorType(method.ReturnType):
                    yield return (method.Identifier, method.Modifiers);
                    break;
                case FieldDeclarationSyntax field when IsLocatorType(field.Declaration.Type):
                    foreach (var variable in field.Declaration.Variables)
                    {
                        yield return (variable.Identifier, field.Modifiers);
                    }

                    break;
            }
        }
    }

    [GeneratedRegex(@"^bug:[A-Za-z0-9-]+$")]
    private static partial Regex BugCategory();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PascalCase();

    [GeneratedRegex(@"^I[A-Z]")]
    private static partial Regex InterfaceName();

    [GeneratedRegex(@"<PackageReference\b[^>]*\bInclude\s*=\s*""([^""]+)""")]
    private static partial Regex PackageReference();
}
