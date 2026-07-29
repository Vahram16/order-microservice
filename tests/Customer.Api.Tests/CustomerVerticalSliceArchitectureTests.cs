namespace Customer.Api.Tests;

public sealed class CustomerVerticalSliceArchitectureTests
{
    private static readonly string[] SliceNames =
    [
        "Provisioning",
        "GettingCurrent",
        "UpdatingDetails",
        "AddingAddress",
        "UpdatingAddress",
        "RemovingAddress",
        "Exporting",
        "ClosingAccount"
    ];

    [Fact]
    public void CustomerUseCasesMustRemainIndependentVerticalSlices()
    {
        var repositoryRoot = FindRepositoryRoot();
        var customerFeatures = Path.Combine(
            repositoryRoot,
            "src",
            "Services",
            "Customer",
            "Customer.Api",
            "Features",
            "Customers");
        var removedMonolith = Path.Combine(
            Directory.GetParent(customerFeatures)!.FullName,
            "CustomerEndpoints.cs");

        Assert.False(
            File.Exists(removedMonolith),
            "Customer use cases must not be recombined into a monolithic endpoint file.");

        foreach (var sliceName in SliceNames)
        {
            var sliceDirectory = Path.Combine(customerFeatures, sliceName, "V1");
            Assert.True(
                Directory.Exists(sliceDirectory),
                $"Customer slice '{sliceName}' is missing its V1 directory.");

            var sourceFile = Assert.Single(
                Directory.EnumerateFiles(sliceDirectory, "*.cs", SearchOption.TopDirectoryOnly));
            var source = File.ReadAllText(sourceFile);

            Assert.Contains(
                $"namespace Customer.Api.Features.Customers.{sliceName}.V1;",
                source,
                StringComparison.Ordinal);
            Assert.Contains("public static void Map(", source, StringComparison.Ordinal);
            Assert.Contains("IRequest<", source, StringComparison.Ordinal);
            Assert.Contains("IRequestHandler<", source, StringComparison.Ordinal);

            foreach (var otherSlice in SliceNames.Where(name => name != sliceName))
            {
                Assert.DoesNotContain(
                    $"Customer.Api.Features.Customers.{otherSlice}.V1",
                    source,
                    StringComparison.Ordinal);
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
