using Customer.Api.Domain;

namespace Customer.Api.Tests;

public sealed class CustomerDomainBoundaryTests
{
    private static readonly string[] ForbiddenReferences =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "MediatR",
        "FluentValidation",
        "Npgsql",
        "Microservices.Application",
        "Microservices.Security",
        "Microservices.ServiceDefaults",
        "Customer.Api.Features",
        "Customer.Api.Persistence",
        "Customer.Api.Infrastructure"
    ];

    [Fact]
    public void DomainCompilesIntoCustomerApiAssembly()
    {
        Assert.Equal(typeof(Program).Assembly, typeof(Domain.Customer).Assembly);
    }

    [Fact]
    public void DomainSourceDoesNotReferenceFrameworkOrOuterLayers()
    {
        var domainPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Services",
            "Customer",
            "Customer.Api",
            "Domain");
        var sourceFiles = Directory
            .EnumerateFiles(domainPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(sourceFiles);
        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            foreach (var forbiddenReference in ForbiddenReferences)
            {
                Assert.DoesNotContain(forbiddenReference, source, StringComparison.Ordinal);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Services", "Customer")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
