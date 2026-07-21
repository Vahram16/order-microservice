using Microservices.Application;

namespace Identity.Api.Features.Accounts.ConfirmingEmail.V1;

internal sealed record ConfirmEmailCommand(
    Guid UserId,
    string Code) : ICommand;
