using Microservices.Application;

namespace Identity.Api.Features.Accounts.EmailConfirmation.Confirm.V1;

public sealed record ConfirmEmailCommand(
    Guid UserId,
    string Code) : ICommand;
