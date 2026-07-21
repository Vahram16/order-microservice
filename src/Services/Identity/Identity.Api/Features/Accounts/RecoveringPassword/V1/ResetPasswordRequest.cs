namespace Identity.Api.Features.Accounts.RecoveringPassword.V1;

public sealed record ResetPasswordRequest(
    Guid UserId,
    string Code,
    string NewPassword);
