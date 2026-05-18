using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBuryProject.Tests.Architecture;

public sealed partial class HttpIntegrationCollectionConventionTests
{
    private const string RequiredCollectionName = "HttpIntegration";

    [Fact]
    public void HttpIntegrationTests_ConCustomWebApplicationFactory_DebenUsarCollectionHttpIntegration()
    {
        var offenders = FindHttpIntegrationTestClassesWithoutCollection();

        Assert.True(
            offenders.Count == 0,
            "Las siguientes clases usan CustomWebApplicationFactory/TestHost y deben tener " +
            "[Collection(\"HttpIntegration\")]: " + string.Join(", ", offenders.Order()));
    }

    private static IReadOnlyCollection<string> FindHttpIntegrationTestClassesWithoutCollection()
    {
        var assembly = typeof(CustomWebApplicationFactory).Assembly;
        var fixtureBasedTypes = assembly
            .GetTypes()
            .Where(IsConcreteTestClass)
            .Where(UsesCustomWebApplicationFactoryFixture)
            .Select(type => type.FullName ?? type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var sourceBasedTypes = FindSourceBasedHttpTestClasses();

        return fixtureBasedTypes
            .Concat(sourceBasedTypes)
            .Distinct(StringComparer.Ordinal)
            .Where(typeName =>
            {
                var type = assembly.GetType(typeName);
                return type == null || !HasHttpIntegrationCollection(type);
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsConcreteTestClass(Type type)
    {
        return type is { IsClass: true, IsAbstract: false }
               && type.GetMethods().Any(method => method.GetCustomAttributes()
                   .Any(attribute => attribute is FactAttribute || attribute is TheoryAttribute));
    }

    private static bool UsesCustomWebApplicationFactoryFixture(Type type)
    {
        return type.GetInterfaces().Any(interfaceType =>
            interfaceType.IsGenericType
            && interfaceType.GetGenericTypeDefinition() == typeof(IClassFixture<>)
            && typeof(WebApplicationFactory<Program>).IsAssignableFrom(interfaceType.GetGenericArguments()[0]));
    }

    private static bool HasHttpIntegrationCollection(Type type)
    {
        return CustomAttributeData.GetCustomAttributes(type)
            .Where(attribute => attribute.AttributeType == typeof(CollectionAttribute))
            .Any(attribute =>
                attribute.ConstructorArguments.Count == 1
                && string.Equals(
                    attribute.ConstructorArguments[0].Value as string,
                    RequiredCollectionName,
                    StringComparison.Ordinal));
    }

    private static IReadOnlyCollection<string> FindSourceBasedHttpTestClasses()
    {
        var testsRoot = FindTestsRoot();
        var classNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}Architecture{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var fileName = Path.GetFileName(file);
            if (fileName is "CustomWebApplicationFactory.cs" or "TestAuthHandler.cs")
                continue;

            var source = File.ReadAllText(file);
            var sourceForMatching = RemoveCommentsAndStrings(source);

            if (!sourceForMatching.Contains("[Fact]", StringComparison.Ordinal)
                && !sourceForMatching.Contains("[Theory]", StringComparison.Ordinal))
                continue;

            var namespaceName = NamespaceRegex().Match(sourceForMatching).Groups["name"].Value;

            foreach (Match match in ClassRegex().Matches(sourceForMatching))
            {
                var className = match.Groups["name"].Value;
                var classBodyStart = sourceForMatching.IndexOf('{', match.Index);
                if (classBodyStart < 0)
                    continue;

                var classBody = ExtractBlock(sourceForMatching, classBodyStart);
                if (ContainsTestHostPattern(classBody))
                    classNames.Add($"{namespaceName}.{className}");
            }
        }

        return classNames;
    }

    private static bool ContainsTestHostPattern(string classBody)
    {
        return classBody.Contains("CustomWebApplicationFactory", StringComparison.Ordinal)
               || classBody.Contains("WebApplicationFactory", StringComparison.Ordinal)
               || classBody.Contains("CreateClient(", StringComparison.Ordinal)
               || classBody.Contains("CreateAuthenticatedClient(", StringComparison.Ordinal)
               || classBody.Contains("CreateClientWithUserId(", StringComparison.Ordinal);
    }

    private static string ExtractBlock(string source, int openingBraceIndex)
    {
        var depth = 0;

        for (var i = openingBraceIndex; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
                depth--;

            if (depth == 0)
                return source.Substring(openingBraceIndex, i - openingBraceIndex + 1);
        }

        return source[openingBraceIndex..];
    }

    private static string RemoveCommentsAndStrings(string source)
    {
        return CommentsAndStringsRegex().Replace(source, string.Empty);
    }

    private static string FindTestsRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "TheBuryProyect.Tests");
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro el directorio TheBuryProyect.Tests.");
    }

    [GeneratedRegex(@"(?:public|internal)\s+(?:sealed\s+)?(?:partial\s+)?class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ClassRegex();

    [GeneratedRegex(@"namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*;")]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex("""
        //.*?$|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"
        """, RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex CommentsAndStringsRegex();
}
