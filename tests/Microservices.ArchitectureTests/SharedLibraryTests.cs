using System.Xml.Linq;

namespace Microservices.ArchitectureTests;

public sealed class SharedLibraryTests
{
    private static readonly string[] ForbiddenSecurityReferencePrefixes =
    [
        "Keycloak",
        "Npgsql",
        "OpenIddict"
    ];

    [Fact]
    public void ContractsMustNotDependOnFrameworkOrInfrastructureProjects()
    {
        var references = typeof(Contracts.IIntegrationEvent).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.DoesNotContain(references, name =>
            name!.StartsWith("MassTransit", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name =>
            name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name =>
            name!.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    [Fact]
    public void SecurityMustRemainIndependentOfIdentityProviderAndPersistence()
    {
        var securityProject = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Shared",
            "Microservices.Security",
            "Microservices.Security.csproj");
        var declaredReferences = ReadDeclaredReferences(securityProject)
            .Where(IsForbiddenSecurityReference)
            .ToArray();

        Assert.Empty(declaredReferences);
    }

    [Fact]
    public void SharedProjectsMustNotReferenceDeployableServices()
    {
        var sharedDirectory = Path.Combine(FindRepositoryRoot(), "src", "Shared");
        var violations = Directory
            .EnumerateFiles(sharedDirectory, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => ReadProjectReferences(project)
                .Where(IsDeployableServiceProjectReference)
                .Select(reference => $"{Path.GetFileName(project)} -> {reference}"))
            .ToArray();

        Assert.Empty(violations);
    }

    private static bool IsForbiddenSecurityReference(string reference) =>
        reference.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase) ||
        ForbiddenSecurityReferencePrefixes.Any(prefix =>
            reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsDeployableServiceProjectReference(string reference)
    {
        var normalized = reference.Replace('\\', '/');
        return normalized.Contains("/Services/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("Services/", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ReadDeclaredReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName is
                "PackageReference" or "ProjectReference" or "Reference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();

    private static IEnumerable<string> ReadProjectReferences(string projectPath) =>
        XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Shared")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
