namespace Identity.Api.Features.Presentation;

internal sealed record ConfirmEmailPageModel(
    Guid UserId,
    string Code,
    string AntiforgeryToken);
