using System.Net;
using Microsoft.Extensions.Options;
using Notifications.Api.Configuration;
using Notifications.Api.Email;
using Notifications.Api.Email.Postmark;

namespace Notifications.Api.Tests;

public sealed class PostmarkEmailTransportTests
{
    [Fact]
    public async Task SendsTemplateRequestWithServerTokenAndMetadata()
    {
        string? body = null;
        string? serverToken = null;
        var handler = new TestHttpMessageHandler(async request =>
        {
            serverToken = request.Headers.GetValues("X-Postmark-Server-Token").Single();
            body = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"ErrorCode\":0,\"Message\":\"OK\",\"MessageID\":\"0d4fef66-66a3-4a0f-bf8f-bfef49f846b0\",\"SubmittedAt\":\"2026-07-27T12:00:00Z\",\"To\":\"customer@example.com\"}")
            };
        });
        var transport = CreateTransport(handler);

        var result = await transport.SendAsync(
            new EmailMessage(
                Guid.Parse("01984c4e-5212-7d87-a44f-d73743eb977f"),
                Guid.Parse("01984c4e-5212-7d87-a44f-d73743eb9780"),
                "identity.email-confirmation",
                "identity-email-confirmation-v1",
                "customer@example.com",
                "https://identity.example.com/account/confirm-email?code=opaque",
                new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        Assert.Equal("server-token", serverToken);
        Assert.Contains("identity-email-confirmation-v1", body, StringComparison.Ordinal);
        Assert.Contains("notificationId", body, StringComparison.Ordinal);
        Assert.Equal("0d4fef66-66a3-4a0f-bf8f-bfef49f846b0", result.ProviderMessageId);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    public async Task ClassifiesProviderFailures(
        HttpStatusCode statusCode,
        bool expectedTransient)
    {
        var transport = CreateTransport(new TestHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{\"ErrorCode\":300,\"Message\":\"failure\"}")
            })));

        var exception = await Assert.ThrowsAsync<EmailTransportException>(() =>
            transport.SendAsync(
                new EmailMessage(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "identity.password-reset",
                    "identity-password-reset-v1",
                    "customer@example.com",
                    "https://identity.example.com/account/reset-password?code=opaque",
                    DateTimeOffset.UtcNow.AddHours(1)),
                CancellationToken.None));

        Assert.Equal(expectedTransient, exception.IsTransient);
    }

    private static PostmarkEmailTransport CreateTransport(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.postmarkapp.com/")
        };
        return new PostmarkEmailTransport(
            client,
            Options.Create(new PostmarkOptions
            {
                ServerToken = "server-token",
                FromAddress = "notifications@example.com"
            }));
    }

    private sealed class TestHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request);
    }
}
