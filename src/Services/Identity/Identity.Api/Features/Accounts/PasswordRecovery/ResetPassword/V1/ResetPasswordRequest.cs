namespace Identity.Api.Features.Accounts.PasswordRecovery.ResetPassword.V1;

public sealed record ResetPasswordRequest(
    Guid UserId,
    string Code,
    string NewPassword);
