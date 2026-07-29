using System.Text.RegularExpressions;

namespace Customer.Api.Tests;

public sealed class CustomerVerticalSliceArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedFiles =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Provisioning"] =
            [
                "ProvisionCustomerCommand.cs",
                "ProvisionCustomerEndpoint.cs",
                "ProvisionCustomerHandler.cs",
                "ProvisionCustomerResult.cs",
                "ProvisionCustomerValidator.cs"
            ],
            ["GettingCurrent"] =
            [
                "GetCurrentCustomerEndpoint.cs",
                "GetCurrentCustomerHandler.cs",
                "GetCurrentCustomerQuery.cs",
                "GetCurrentCustomerValidator.cs"
            ],
            ["UpdatingDetails"] =
            [
                "UpdateCustomerDetailsCommand.cs",
                "UpdateCustomerDetailsEndpoint.cs",
                "UpdateCustomerDetailsHandler.cs",
                "UpdateCustomerDetailsRequest.cs",
                "UpdateCustomerDetailsValidator.cs"
            ],
            ["AddingAddress"] =
            [
                "AddCustomerAddressCommand.cs",
                "AddCustomerAddressDataValidator.cs",
                "AddCustomerAddressEndpoint.cs",
                "AddCustomerAddressHandler.cs",
                "AddCustomerAddressRequest.cs",
                "AddCustomerAddressResult.cs",
                "AddCustomerAddressValidator.cs"
            ],
            ["UpdatingAddress"] =
            [
                "UpdateCustomerAddressCommand.cs",
                "UpdateCustomerAddressDataValidator.cs",
                "UpdateCustomerAddressEndpoint.cs",
                "UpdateCustomerAddressHandler.cs",
                "UpdateCustomerAddressRequest.cs",
                "UpdateCustomerAddressValidator.cs"
            ],
            ["RemovingAddress"] =
            [
                "RemoveCustomerAddressCommand.cs",
                "RemoveCustomerAddressEndpoint.cs",
                "RemoveCustomerAddressHandler.cs",
                "RemoveCustomerAddressValidator.cs"
            ],
            ["Exporting"] =
            [
                "CustomerExportResponse.cs",
                "ExportCustomerEndpoint.cs",
                "ExportCustomerHandler.cs",
                "ExportCustomerQuery.cs",
                "ExportCustomerValidator.cs"
            ],
            ["ClosingAccount"] =
            [
                "CloseCustomerAccountCommand.cs",
                "CloseCustomerAccountEndpoint.cs",
                "CloseCustomerAccountHandler.cs",
                "CloseCustomerAccountValidator.cs"
            ]
        };

    [Fact]
    public void CustomerUseCasesMustRemainIndependentVerticalSlices()
    {
        var customerFeatures = GetCustomerFeaturesDirectory();

        Assert.Empty(
            Directory.EnumerateFiles(
                customerFeatures,
                "*Slice.cs",
                SearchOption.AllDirectories));

        foreach (var (sliceName, expectedFiles) in ExpectedFiles)
        {
            var sliceDirectory = Path.Combine(customerFeatures, sliceName, "V1");
            Assert.True(
                Directory.Exists(sliceDirectory),
                $"Customer slice '{sliceName}' is missing its V1 directory.");

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
                    $"namespace Customer.Api.Features.Customers.{sliceName}.V1;",
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
                        $"Customer.Api.Features.Customers.{otherSlice}.V1",
                        source,
                        StringComparison.Ordinal);
                }
            }

            var endpointSource = sources.Single(pair => pair.Key.EndsWith("Endpoint.cs", StringComparison.Ordinal)).Value;
            Assert.Contains("public static void Map(", endpointSource, StringComparison.Ordinal);

            var messageSource = sources.Single(
                pair =>
                    pair.Key.EndsWith("Command.cs", StringComparison.Ordinal) ||
                    pair.Key.EndsWith("Query.cs", StringComparison.Ordinal)).Value;
            Assert.True(
                messageSource.Contains(": ICommand<", StringComparison.Ordinal) ||
                messageSource.Contains(": IQuery<", StringComparison.Ordinal));
            Assert.DoesNotContain(": IRequest", messageSource, StringComparison.Ordinal);

            var handlerSource = sources.Single(pair => pair.Key.EndsWith("Handler.cs", StringComparison.Ordinal)).Value;
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
    public void SharedCustomerContractsMustUseOneTopLevelTypePerFile()
    {
        var commonDirectory = Path.Combine(GetCustomerFeaturesDirectory(), "Common");

        Assert.False(File.Exists(Path.Combine(commonDirectory, "CustomerContracts.cs")));
        Assert.True(File.Exists(Path.Combine(commonDirectory, "CustomerResponse.cs")));
        Assert.True(File.Exists(Path.Combine(commonDirectory, "CustomerAddressResponse.cs")));
        Assert.True(File.Exists(Path.Combine(commonDirectory, "CustomerMappings.cs")));
    }

    private static string GetCustomerFeaturesDirectory() =>
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Services",
            "Customer",
            "Customer.Api",
            "Features",
            "Customers");

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
