using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Payment.Api.Domain;

namespace Payment.Api.Tests;

public sealed class PaymentDomainBoundaryTests
{
    private const string SdkImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Net.Http;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "MediatR",
        "FluentValidation",
        "Npgsql",
        "Stripe",
        "Microservices.Application",
        "Microservices.Security",
        "Microservices.ServiceDefaults",
        "Payment.Api.Features",
        "Payment.Api.Persistence",
        "Payment.Api.Infrastructure",
        "Payment.Api.Webhooks"
    ];

    [Fact]
    public void DomainCompilesIntoPaymentApiAssembly()
    {
        Assert.Equal(typeof(Program).Assembly, typeof(PaymentCustomer).Assembly);
    }

    [Fact]
    public void DomainDoesNotSemanticallyReferenceFrameworkProviderOrOuterLayers()
    {
        var paymentApiPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Services",
            "Payment",
            "Payment.Api");
        var domainPath = Path.Combine(paymentApiPath, "Domain");
        var domainSourceFiles = Directory
            .EnumerateFiles(domainPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(domainSourceFiles);
        var domainSyntaxTrees = domainSourceFiles
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path))
            .ToArray();
        var syntaxTrees = domainSyntaxTrees
            .Concat([
                CSharpSyntaxTree.ParseText(
                    File.ReadAllText(Path.Combine(paymentApiPath, "GlobalUsings.cs")),
                    path: Path.Combine(paymentApiPath, "GlobalUsings.cs")),
                CSharpSyntaxTree.ParseText(
                    SdkImplicitUsings,
                    path: "Payment.Api.SdkImplicitUsings.g.cs")
            ])
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "PaymentDomainBoundaryAnalysis",
            syntaxTrees,
            CreateMetadataReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var compilationErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.True(
            compilationErrors.Length == 0,
            $"Payment Domain semantic analysis could not compile the boundary sources:{Environment.NewLine}{string.Join(Environment.NewLine, compilationErrors)}");

        var violations = new List<string>();
        foreach (var syntaxTree in domainSyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            foreach (var name in syntaxTree.GetRoot().DescendantNodes().OfType<NameSyntax>())
            {
                foreach (var symbol in GetReferencedSymbols(semanticModel, name))
                {
                    if (!TryGetForbiddenNamespace(symbol, out var namespaceName))
                    {
                        continue;
                    }

                    var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    violations.Add(
                        $"{Path.GetRelativePath(paymentApiPath, syntaxTree.FilePath)}:{line} references {symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} from forbidden namespace '{namespaceName}'.");
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

        var paymentAssembly = typeof(Program).Assembly;
        referencePaths.Add(paymentAssembly.Location);
        foreach (var referencedAssembly in paymentAssembly.GetReferencedAssemblies())
        {
            var path = Path.Combine(AppContext.BaseDirectory, $"{referencedAssembly.Name}.dll");
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

    private static bool TryGetForbiddenNamespace(ISymbol symbol, out string namespaceName)
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
                Directory.Exists(Path.Combine(directory.FullName, "src", "Services", "Payment")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
