namespace Identity.Api.Features.Accounts.EmailConfirmation.Confirm.V1;

public sealed record ConfirmEmailRequest(
    Guid UserId,
    string Code);
