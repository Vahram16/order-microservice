namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

public sealed record ConfirmEmailRequest(
    string UserId,
    string Code);
