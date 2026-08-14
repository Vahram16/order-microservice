using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Product.Api.Domain;

namespace Product.Api.Tests;

public sealed class ProductDomainBoundaryTests
{
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "MediatR",
        "FluentValidation",
        "Npgsql",
        "Microservices.Application",
        "Microservices.Security",
        "Microservices.ServiceDefaults",
        "Product.Api.Features",
        "Product.Api.Persistence",
        "Product.Api.Infrastructure"
    ];

    [Fact]
    public void DomainCompilesIntoProductApiAssembly()
    {
        Assert.Equal(typeof(Program).Assembly, typeof(Domain.Product).Assembly);
    }

    [Fact]
    public void DomainDoesNotSemanticallyReferenceFrameworkOrOuterLayers()
    {
        var productApiPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Services",
            "Product",
            "Product.Api");
        var domainPath = Path.Combine(productApiPath, "Domain");
        var sourceFiles = Directory
            .EnumerateFiles(domainPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Append(Path.Combine(productApiPath, "GlobalUsings.cs"))
            .ToArray();

        Assert.NotEmpty(sourceFiles);
        var syntaxTrees = sourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                path: path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "ProductDomainBoundaryAnalysis",
            syntaxTrees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var compilationErrors = compilation
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.True(
            compilationErrors.Length == 0,
            $"Product Domain semantic analysis could not compile the boundary sources:{Environment.NewLine}{string.Join(Environment.NewLine, compilationErrors)}");

        var violations = new List<string>();
        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(
                syntaxTree,
                ignoreAccessibility: true);
            foreach (var name in syntaxTree
                         .GetRoot()
                         .DescendantNodes()
                         .OfType<NameSyntax>())
            {
                foreach (var symbol in GetReferencedSymbols(semanticModel, name))
                {
                    if (!TryGetForbiddenNamespace(symbol, out var namespaceName))
                    {
                        continue;
                    }

                    var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add(
                        $"{Path.GetRelativePath(productApiPath, syntaxTree.FilePath)}:{line} references {symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} from forbidden namespace '{namespaceName}'.");
                }
            }
        }

        Assert.Empty(violations.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));
    }

    private static IEnumerable<MetadataReference> CreateMetadataReferences()
    {
        var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                referencePaths.Add(path);
            }
        }

        var productAssembly = typeof(Program).Assembly;
        referencePaths.Add(productAssembly.Location);
        foreach (var referencedAssembly in productAssembly.GetReferencedAssemblies())
        {
            var path = Path.Combine(
                AppContext.BaseDirectory,
                $"{referencedAssembly.Name}.dll");
            if (File.Exists(path))
            {
                referencePaths.Add(path);
            }
        }

        return referencePaths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static IEnumerable<ISymbol> GetReferencedSymbols(
        SemanticModel semanticModel,
        NameSyntax name)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(name);
        if (symbolInfo.Symbol is not null)
        {
            yield return UnwrapAlias(symbolInfo.Symbol);
        }

        foreach (var candidateSymbol in symbolInfo.CandidateSymbols)
        {
            yield return UnwrapAlias(candidateSymbol);
        }

        if (semanticModel.GetTypeInfo(name).Type is { } type)
        {
            yield return type;
        }
    }

    private static ISymbol UnwrapAlias(ISymbol symbol) =>
        symbol is IAliasSymbol alias ? alias.Target : symbol;

    private static bool TryGetForbiddenNamespace(
        ISymbol symbol,
        out string namespaceName)
    {
        var resolvedNamespaceName = symbol switch
        {
            INamespaceSymbol namespaceSymbol => namespaceSymbol.ToDisplayString(),
            _ => symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty
        };
        namespaceName = resolvedNamespaceName;

        return ForbiddenNamespacePrefixes.Any(prefix =>
            string.Equals(resolvedNamespaceName, prefix, StringComparison.Ordinal) ||
            resolvedNamespaceName.StartsWith(prefix + ".", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Services", "Product")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
