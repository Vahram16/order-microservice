using System.Text.RegularExpressions;

namespace Product.Api.Tests;

public sealed class ProductVerticalSliceArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedFiles =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Creating"] =
            [
                "CreateProductCommand.cs",
                "CreateProductEndpoint.cs",
                "CreateProductHandler.cs",
                "CreateProductRequest.cs",
                "CreateProductValidator.cs"
            ],
            ["GettingById"] =
            [
                "GetProductByIdEndpoint.cs",
                "GetProductByIdHandler.cs",
                "GetProductByIdQuery.cs",
                "GetProductByIdValidator.cs"
            ],
            ["Listing"] =
            [
                "ListProductsEndpoint.cs",
                "ListProductsHandler.cs",
                "ListProductsQuery.cs",
                "ListProductsValidator.cs"
            ],
            ["Updating"] =
            [
                "UpdateProductCommand.cs",
                "UpdateProductEndpoint.cs",
                "UpdateProductHandler.cs",
                "UpdateProductRequest.cs",
                "UpdateProductValidator.cs"
            ],
            ["Deleting"] =
            [
                "DeleteProductCommand.cs",
                "DeleteProductEndpoint.cs",
                "DeleteProductHandler.cs",
                "DeleteProductValidator.cs"
            ]
        };

    [Fact]
    public void ProductUseCasesRemainIndependentVersionedVerticalSlices()
    {
        var productFeatures = GetProductFeaturesDirectory();

        Assert.Empty(
            Directory.EnumerateFiles(
                productFeatures,
                "*Slice.cs",
                SearchOption.AllDirectories));

        foreach (var (sliceName, expectedFiles) in ExpectedFiles)
        {
            var sliceDirectory = Path.Combine(productFeatures, sliceName, "V1");
            Assert.True(
                Directory.Exists(sliceDirectory),
                $"Product slice '{sliceName}' is missing its V1 directory.");

            var actualFiles = Directory
                .EnumerateFiles(sliceDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                expectedFiles.OrderBy(file => file, StringComparer.Ordinal),
                actualFiles);

            var sources = actualFiles.ToDictionary(
                file => file,
                file => File.ReadAllText(Path.Combine(sliceDirectory, file)),
                StringComparer.Ordinal);

            foreach (var source in sources.Values)
            {
                Assert.Contains(
                    $"namespace Product.Api.Features.Products.{sliceName}.V1;",
                    source,
                    StringComparison.Ordinal);
                Assert.Single(
                    Regex.Matches(
                            source,
                            @"\b(?:public|internal)\s+(?:sealed\s+)?(?:static\s+)?(?:class|record|interface)\s+\w+")
                        .Cast<Match>());

                foreach (var otherSlice in ExpectedFiles.Keys.Where(name => name != sliceName))
                {
                    Assert.DoesNotContain(
                        $"Product.Api.Features.Products.{otherSlice}.V1",
                        source,
                        StringComparison.Ordinal);
                }
            }

            var endpointSource = sources.Single(
                pair => pair.Key.EndsWith("Endpoint.cs", StringComparison.Ordinal)).Value;
            Assert.Contains("public static void Map(", endpointSource, StringComparison.Ordinal);

            var messageSource = sources.Single(pair =>
                pair.Key.EndsWith("Command.cs", StringComparison.Ordinal) ||
                pair.Key.EndsWith("Query.cs", StringComparison.Ordinal)).Value;
            Assert.True(
                messageSource.Contains(": ICommand<", StringComparison.Ordinal) ||
                messageSource.Contains(": IQuery<", StringComparison.Ordinal));
            Assert.DoesNotContain(": IRequest", messageSource, StringComparison.Ordinal);

            var handlerSource = sources.Single(
                pair => pair.Key.EndsWith("Handler.cs", StringComparison.Ordinal)).Value;
            Assert.True(
                handlerSource.Contains(": ICommandHandler<", StringComparison.Ordinal) ||
                handlerSource.Contains(": IQueryHandler<", StringComparison.Ordinal));
            Assert.DoesNotContain("IRequestHandler<", handlerSource, StringComparison.Ordinal);

            Assert.Contains(
                sources.Keys,
                file => file.EndsWith("Validator.cs", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SharedProductContractsUseOneTopLevelTypePerFile()
    {
        var commonDirectory = Path.Combine(GetProductFeaturesDirectory(), "Common");
        var commonFiles = Directory
            .EnumerateFiles(commonDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(commonFiles);
        Assert.Contains(commonFiles, file => Path.GetFileName(file) == "ProductResponse.cs");
        Assert.Contains(commonFiles, file => Path.GetFileName(file) == "ProductListResponse.cs");
        Assert.Contains(commonFiles, file => Path.GetFileName(file) == "ProductMappings.cs");
        foreach (var commonFile in commonFiles)
        {
            Assert.Single(
                Regex.Matches(
                        File.ReadAllText(commonFile),
                        @"\b(?:public|internal)\s+(?:sealed\s+)?(?:static\s+)?(?:class|record|interface)\s+\w+")
                    .Cast<Match>());
        }
    }

    private static string GetProductFeaturesDirectory() =>
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Services",
            "Product",
            "Product.Api",
            "Features",
            "Products");

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
