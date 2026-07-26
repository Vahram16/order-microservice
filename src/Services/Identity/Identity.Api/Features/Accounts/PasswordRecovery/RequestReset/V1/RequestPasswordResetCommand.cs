using Microservices.Application;

namespace Identity.Api.Features.Accounts.PasswordRecovery.RequestReset.V1;

public sealed record RequestPasswordResetCommand(string Email) : ICommand;
