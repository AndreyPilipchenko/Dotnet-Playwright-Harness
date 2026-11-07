using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Harness.Tools.Conventions;

/// <summary>A parsed C# file plus the small syntax helpers the rules share.</summary>
internal sealed partial class SourceFile
{
    private static readonly string[] TestMethodAttributes = ["Test", "TestCase", "TestCaseSource", "Theory", "Fact", "TestMethod", "DataTestMethod"];

    public SourceFile(string relativePath, Layer layer, string text)
    {
        RelativePath = relativePath;
        Layer = layer;
        Text = text;
        Lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Root = CSharpSyntaxTree.ParseText(text).GetRoot();
    }

    public string RelativePath { get; }

    public Layer Layer { get; }

    public string Text { get; }

    public string[] Lines { get; }

    public SyntaxNode Root { get; }

    /// <summary>The file name without extension, minus a trailing <c>Tests</c>: the test feature name.</summary>
    public string Feature
    {
        get
        {
            var name = Path.GetFileNameWithoutExtension(RelativePath);
            return name.EndsWith("Tests", StringComparison.Ordinal) && name.Length > 5 ? name[..^5] : name;
        }
    }

    public IEnumerable<T> Nodes<T>()
        where T : SyntaxNode => Root.DescendantNodes().OfType<T>();

    public IEnumerable<InvocationExpressionSyntax> Invocations => Nodes<InvocationExpressionSyntax>();

    public IEnumerable<MethodDeclarationSyntax> TestMethods => Nodes<MethodDeclarationSyntax>().Where(IsTestMethod);

    /// <summary>Classes that declare at least one test method.</summary>
    public IEnumerable<ClassDeclarationSyntax> TestClasses =>
        Nodes<ClassDeclarationSyntax>().Where(c => c.Members.OfType<MethodDeclarationSyntax>().Any(IsTestMethod));

    public static bool IsTestMethod(MethodDeclarationSyntax method) =>
        method.AttributeLists.SelectMany(a => a.Attributes).Any(a => TestMethodAttributes.Contains(AttributeName(a)));

    /// <summary>The rightmost simple name of a type: <c>Task</c> for <c>System.Threading.Tasks.Task&lt;int&gt;</c>.</summary>
    public static string SimpleTypeName(TypeSyntax type) => type switch
    {
        NullableTypeSyntax nullable => SimpleTypeName(nullable.ElementType),
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => type.ToString(),
    };

    public static int LineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    public static int LineOf(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>The called name: <c>Click</c> for <c>a.b.Click()</c>, <c>Load</c> for <c>TestData.Load&lt;T&gt;()</c>.</summary>
    public static string Name(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => string.Empty,
    };

    /// <summary>The receiver as written: <c>Assert</c> for <c>Assert.That()</c>; empty for a bare call.</summary>
    public static string Receiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax member ? member.Expression.ToString() : string.Empty;

    /// <summary>True when the receiver is <paramref name="type"/>, plain or namespace-qualified.</summary>
    public static bool ReceiverIs(InvocationExpressionSyntax invocation, string type)
    {
        var receiver = Receiver(invocation);
        return receiver == type || receiver.EndsWith("." + type, StringComparison.Ordinal);
    }

    public static string AttributeName(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => attribute.Name.ToString(),
        };
        return name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;
    }

    public static string? StringArgument(AttributeSyntax attribute, int index)
    {
        var args = attribute.ArgumentList?.Arguments;
        return args is { } list && list.Count > index && list[index].Expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;
    }

    /// <summary>The member a node sits in (method, constructor, property, field), if any.</summary>
    public static MemberDeclarationSyntax? EnclosingMember(SyntaxNode node) =>
        node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault(m => m is not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax);

    /// <summary>
    /// A line is exempt when it, or a comment-only line directly above it, carries
    /// <c>// conventions-ok: &lt;reason&gt;</c>. The reason is required.
    /// </summary>
    public bool IsExempt(int line)
    {
        if (line < 1 || line > Lines.Length)
        {
            return false;
        }

        if (Exemption().IsMatch(Lines[line - 1]))
        {
            return true;
        }

        return line >= 2 && Lines[line - 2].TrimStart().StartsWith("//", StringComparison.Ordinal) && Exemption().IsMatch(Lines[line - 2]);
    }

    public string ExcerptAt(int line)
    {
        var text = line >= 1 && line <= Lines.Length ? Lines[line - 1].Trim() : string.Empty;
        return text.Length > 90 ? text[..90] : text;
    }

    [GeneratedRegex(@"//\s*conventions-ok:\s*\S.{2,}")]
    private static partial Regex Exemption();
}
