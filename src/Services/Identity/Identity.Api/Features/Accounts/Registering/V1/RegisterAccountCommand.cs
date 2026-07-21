using Microservices.Application;

namespace Identity.Api.Features.Accounts.Registering.V1;

public sealed record RegisterAccountCommand(
    string Email,
    string Password,
    string DisplayName) : ICommand;
