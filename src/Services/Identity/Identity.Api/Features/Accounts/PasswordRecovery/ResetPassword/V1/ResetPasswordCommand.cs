using Microservices.Application;

namespace Identity.Api.Features.Accounts.PasswordRecovery.ResetPassword.V1;

public sealed record ResetPasswordCommand(
    Guid UserId,
    string Code,
    string NewPassword) : ICommand;
