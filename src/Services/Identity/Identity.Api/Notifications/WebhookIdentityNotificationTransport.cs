using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Identity.Api.Notifications;

internal sealed class WebhookIdentityNotificationTransport(
    HttpClient httpClient,
    IOptions<IdentityNotificationOptions> options)
    : IIdentityNotificationTransport
{
    private readonly IdentityNotificationOptions _options = options.Value;

    public async Task SendAsync(
        IdentityNotificationPayload payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.WebhookEndpoint)
        {
            Content = JsonContent.Create(new
            {
                eventId = payload.EventId,
                template = payload.Template,
                recipient = payload.Recipient,
                actionUrl = payload.ActionUrl,
                expiresAtUtc = payload.ExpiresAtUtc
            })
        };
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            payload.EventId.ToString("N"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _options.WebhookApiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
