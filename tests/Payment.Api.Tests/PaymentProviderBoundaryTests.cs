namespace Payment.Api.Tests;

public sealed class PaymentProviderBoundaryTests
{
    [Fact]
    public void StripeSdkReferencesStayInsideStripeInfrastructure()
    {
        var apiPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Services",
            "Payment",
            "Payment.Api");
        var stripeInfrastructure = Path.Combine(apiPath, "Infrastructure", "Stripe");
        var violations = Directory.EnumerateFiles(apiPath, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(stripeInfrastructure, StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("global::Stripe", StringComparison.Ordinal) ||
                       source.Contains("using Stripe;", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(apiPath, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
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
