using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Notifications.Api.Configuration;

namespace Notifications.Api.Email.Postmark;

internal sealed class PostmarkEmailTransport(
    HttpClient httpClient,
    IOptions<PostmarkOptions> options)
    : IEmailTransport
{
    private readonly PostmarkOptions _options = options.Value;

    public async Task<EmailDeliveryResult> SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "email/withTemplate")
        {
            Content = JsonContent.Create(new PostmarkTemplateRequest(
                _options.FromAddress!,
                message.Recipient,
                message.TemplateAlias,
                new PostmarkTemplateModel(
                    message.ActionUrl,
                    message.ExpiresAtUtc),
                _options.MessageStream,
                message.Template,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["notificationId"] = message.NotificationId.ToString("N"),
                    ["sourceEventId"] = message.SourceEventId.ToString("N"),
                    ["source"] = "identity"
                }))
        };
        request.Headers.TryAddWithoutValidation(
            "X-Postmark-Server-Token",
            _options.ServerToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EmailTransportException("PostmarkTimeout", isTransient: true);
        }
        catch (HttpRequestException exception)
        {
            throw new EmailTransportException(
                "PostmarkNetworkFailure",
                isTransient: true,
                exception);
        }

        using (response)
        {
            PostmarkResponse? providerResponse = null;
            try
            {
                providerResponse = await response.Content.ReadFromJsonAsync<PostmarkResponse>(
                    cancellationToken: cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
                // A bounded safe classification is more useful than retaining provider response text.
            }

            if (!response.IsSuccessStatusCode || providerResponse is null ||
                providerResponse.ErrorCode != 0 ||
                providerResponse.MessageId == Guid.Empty)
            {
                var statusCode = response.StatusCode;
                var transient = statusCode is HttpStatusCode.RequestTimeout or
                    HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
                var errorCode = providerResponse?.ErrorCode;
                throw new EmailTransportException(
                    errorCode is null
                        ? $"PostmarkHttp{(int)statusCode}"
                        : $"PostmarkError{errorCode.Value}",
                    transient);
            }

            return new EmailDeliveryResult(providerResponse.MessageId.ToString("D"));
        }
    }

    private sealed record PostmarkTemplateRequest(
        string From,
        string To,
        string TemplateAlias,
        PostmarkTemplateModel TemplateModel,
        string MessageStream,
        string Tag,
        IReadOnlyDictionary<string, string> Metadata);

    private sealed record PostmarkTemplateModel(
        string ActionUrl,
        DateTimeOffset ExpiresAtUtc);

    private sealed record PostmarkResponse(
        int ErrorCode,
        string? Message,
        Guid MessageId,
        DateTimeOffset? SubmittedAt,
        string? To);
}
