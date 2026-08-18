using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;

namespace Payment.Api.Tests;

public sealed class PaymentApiIntegrationTests(PaymentApiFactory factory)
    : IClassFixture<PaymentApiFactory>, IAsyncLifetime
{
    private const string Subject = "payment-integration-subject";
    private const string WriteScope = "payments.methods.write";

    public Task InitializeAsync() => factory.InitializeDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SetupAndStripeWebhookConvergeThroughTransactionalOutbox()
    {
        var paymentCustomerId = Guid.NewGuid();
        var customer = PaymentCustomer.Create(
            paymentCustomerId,
            Guid.NewGuid(),
            "keycloak",
            Subject,
            DateTimeOffset.UtcNow);
        Assert.True(customer.IsSuccess);

        await using (var dbContext = await factory.CreateDbContextAsync())
        {
            dbContext.PaymentCustomers.Add(customer.Value);
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient(Subject, WriteScope);
        var idempotencyKey = Guid.NewGuid();

        using var setupRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/payment-methods/setup");
        setupRequest.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            idempotencyKey.ToString("D"));

        var setupResponse = await client.SendAsync(setupRequest);
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        Assert.Equal("no-store", setupResponse.Headers.CacheControl?.ToString());

        var providerEventId = $"evt_{Guid.NewGuid():N}";
        var firstWebhook = await SendWebhookAsync(client, providerEventId);
        Assert.Equal(HttpStatusCode.OK, firstWebhook.StatusCode);

        await WaitForWebhookReconciliationAsync(providerEventId);

        await using (var dbContext = await factory.CreateDbContextAsync())
        {
            var persistedCustomer = await dbContext.PaymentCustomers
                .AsNoTracking()
                .SingleAsync(item => item.Id == paymentCustomerId);
            var operation = await dbContext.PaymentMethodSetupOperations
                .AsNoTracking()
                .SingleAsync(item => item.Id == idempotencyKey);
            var method = await dbContext.PaymentMethods
                .AsNoTracking()
                .SingleAsync();
            var webhook = await dbContext.PaymentWebhookEvents
                .AsNoTracking()
                .SingleAsync(item => item.ProviderEventId == providerEventId);

            Assert.Equal("cus_payment_integration", persistedCustomer.ProviderCustomerId);
            Assert.Equal("seti_payment_integration", operation.ProviderSetupIntentId);
            Assert.Equal("pm_payment_integration", method.ProviderPaymentMethodId);
            Assert.Equal("4242", method.Last4);
            Assert.True(method.IsDefault);
            Assert.NotNull(webhook.ProcessedAt);
        }

        var duplicateWebhook = await SendWebhookAsync(client, providerEventId);
        Assert.Equal(HttpStatusCode.OK, duplicateWebhook.StatusCode);

        await using (var dbContext = await factory.CreateDbContextAsync())
        {
            Assert.Equal(1, await dbContext.PaymentWebhookEvents.CountAsync());
            Assert.Equal(1, await dbContext.PaymentMethods.CountAsync());
        }
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        string providerEventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(providerEventId, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", "test-signature");
        return await client.SendAsync(request);
    }

    private async Task WaitForWebhookReconciliationAsync(string providerEventId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        do
        {
            await using var dbContext = await factory.CreateDbContextAsync();
            var processed = await dbContext.PaymentWebhookEvents
                .AsNoTracking()
                .AnyAsync(item =>
                    item.ProviderEventId == providerEventId &&
                    item.ProcessedAt != null);
            if (processed && await dbContext.PaymentMethods.AsNoTracking().AnyAsync())
            {
                return;
            }

            await Task.Delay(100);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException(
            $"Stripe webhook '{providerEventId}' was not reconciled before the test deadline.");
    }
}
