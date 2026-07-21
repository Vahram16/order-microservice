namespace Identity.Api.Features.Presentation;

internal sealed record LogoutPageModel(
    string Action,
    string AntiforgeryToken);
