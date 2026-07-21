using Microservices.Application;

namespace Identity.Api.Features.Accounts.RecoveringPassword.V1;

public sealed record RequestPasswordResetCommand(string Email) : ICommand;
