namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

public sealed record ConfirmEmailRequest(
    Guid UserId,
    string Code);
