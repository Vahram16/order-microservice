namespace Payment.Api.Tests;

public sealed class PaymentNamingTests
{
    [Fact]
    public void ProductionCodeDoesNotUseSavedPaymentMethodAsADomainType()
    {
        var root = FindRepositoryRoot();
        var paymentApi = Path.Combine(root, "src", "Services", "Payment", "Payment.Api");
        var violations = Directory.EnumerateFiles(paymentApi, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("SavedPaymentMethod", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(paymentApi, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
