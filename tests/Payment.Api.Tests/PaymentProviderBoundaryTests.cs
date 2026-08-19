using System.Net;
using System.Text;
using Microservices.Messaging;
using Payment.Api.Features.PaymentMethods.Common;
using Payment.Api.Infrastructure.Stripe;
using Payment.Api.Webhooks;
using StripeClient = Stripe.StripeClient;
using StripeSystemNetHttpClient = Stripe.SystemNetHttpClient;

namespace Payment.Api.Tests;

public sealed class PaymentProviderBoundaryTests
{
    [Theory]
    [InlineData(StripeProviderOperation.CreateCustomer, "stripe.customer_create_failed")]
    [InlineData(StripeProviderOperation.CreateSetup, "stripe.setup_intent_create_failed")]
    [InlineData(StripeProviderOperation.GetSetup, "stripe.setup_intent_get_failed")]
    [InlineData(StripeProviderOperation.GetPaymentMethod, "stripe.payment_method_get_failed")]
    public async Task NetworkFailureIsTranslatedWithOperationCode(
        StripeProviderOperation operation,
        string expectedCode)
    {
        using var httpClient = CreateHttpClient(
            _ => new HttpRequestException("Stripe is unreachable."));
        var provider = CreateProvider(httpClient);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => InvokeAsync(provider, operation, CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(PaymentProviderFailureKind.Transient, exception.FailureKind);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ProviderTimeoutIsTranslatedToProviderFailure()
    {
        using var httpClient = CreateHttpClient(
            _ => new OperationCanceledException("Stripe timed out."));
        var provider = CreateProvider(httpClient);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.CreateCustomerAsync(Guid.NewGuid(), "customer-key", CancellationToken.None));

        Assert.Equal("stripe.customer_create_failed", exception.Code);
        Assert.Equal(PaymentProviderFailureKind.Transient, exception.FailureKind);
        Assert.IsType<OperationCanceledException>(exception.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Conflict, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task StripeStatusDeterminesFailureKind(
        HttpStatusCode statusCode,
        bool expectedTransient)
    {
        using var httpClient = CreateStripeErrorHttpClient(statusCode);
        var provider = CreateProvider(httpClient);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.CreateCustomerAsync(Guid.NewGuid(), "customer-key", CancellationToken.None));

        Assert.Equal(
            expectedTransient ? PaymentProviderFailureKind.Transient : PaymentProviderFailureKind.Permanent,
            exception.FailureKind);
        Assert.IsType<Stripe.StripeException>(exception.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "true", true)]
    [InlineData(HttpStatusCode.InternalServerError, "false", false)]
    public async Task StripeRetryHeaderOverridesStatusClassification(
        HttpStatusCode statusCode,
        string shouldRetry,
        bool expectedTransient)
    {
        using var httpClient = CreateStripeErrorHttpClient(statusCode, shouldRetry);
        var provider = CreateProvider(httpClient);

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.CreateCustomerAsync(Guid.NewGuid(), "customer-key", CancellationToken.None));

        Assert.Equal(
            expectedTransient ? PaymentProviderFailureKind.Transient : PaymentProviderFailureKind.Permanent,
            exception.FailureKind);
    }

    [Fact]
    public void TransientProviderFailureGetsTransientConsumerMarker()
    {
        var providerException = PaymentProviderException.Transient(
            "stripe.failure",
            new HttpRequestException());

        var classified = ProcessStripeWebhookConsumer.ClassifyProviderFailure(providerException);

        Assert.IsAssignableFrom<ITransientConsumerFailure>(classified);
        Assert.IsNotAssignableFrom<IPermanentConsumerFailure>(classified);
    }

    [Fact]
    public void PermanentProviderFailureGetsPermanentConsumerMarker()
    {
        var providerException = PaymentProviderException.Permanent(
            "stripe.failure",
            new InvalidOperationException());

        var classified = ProcessStripeWebhookConsumer.ClassifyProviderFailure(providerException);

        Assert.IsAssignableFrom<IPermanentConsumerFailure>(classified);
        Assert.IsNotAssignableFrom<ITransientConsumerFailure>(classified);
    }

    [Fact]
    public async Task CallerCancellationIsNotTranslatedToProviderFailure()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        using var httpClient = CreateHttpClient(
            token => new OperationCanceledException("Caller cancelled.", token));
        var provider = CreateProvider(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.CreateCustomerAsync(
                Guid.NewGuid(),
                "customer-key",
                cancellationSource.Token));
    }

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

    private static StripePaymentProvider CreateProvider(HttpClient httpClient)
    {
        var stripeHttpClient = new StripeSystemNetHttpClient(httpClient, maxNetworkRetries: 0);
        return new StripePaymentProvider(new StripeClient("sk_test_boundary", httpClient: stripeHttpClient));
    }

    private static async Task InvokeAsync(
        StripePaymentProvider provider,
        StripeProviderOperation operation,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case StripeProviderOperation.CreateCustomer:
                await provider.CreateCustomerAsync(Guid.NewGuid(), "customer-key", cancellationToken);
                break;
            case StripeProviderOperation.CreateSetup:
                await provider.CreatePaymentMethodSetupAsync(
                    Guid.NewGuid(),
                    "cus_boundary",
                    "setup-key",
                    cancellationToken);
                break;
            case StripeProviderOperation.GetSetup:
                await provider.GetPaymentMethodSetupAsync("seti_boundary", cancellationToken);
                break;
            case StripeProviderOperation.GetPaymentMethod:
                await provider.GetPaymentMethodAsync("pm_boundary", cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static HttpClient CreateHttpClient(Func<CancellationToken, Exception> exceptionFactory) =>
        new(new ThrowingHttpMessageHandler(exceptionFactory));

    private static HttpClient CreateStripeErrorHttpClient(
        HttpStatusCode statusCode,
        string? shouldRetry = null) =>
        new(new StripeErrorHttpMessageHandler(statusCode, shouldRetry));

    private sealed class ThrowingHttpMessageHandler(
        Func<CancellationToken, Exception> exceptionFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exceptionFactory(cancellationToken));
    }

    private sealed class StripeErrorHttpMessageHandler(
        HttpStatusCode statusCode,
        string? shouldRetry) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    "{\"error\":{\"type\":\"api_error\",\"message\":\"Stripe failed.\"}}",
                    Encoding.UTF8,
                    "application/json")
            };
            if (shouldRetry is not null)
            {
                response.Headers.TryAddWithoutValidation("Stripe-Should-Retry", shouldRetry);
            }

            return Task.FromResult(response);
        }
    }

    public enum StripeProviderOperation
    {
        CreateCustomer,
        CreateSetup,
        GetSetup,
        GetPaymentMethod
    }
}
