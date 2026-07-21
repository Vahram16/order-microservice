using System.Text.Encodings.Web;

namespace Identity.Api.Features.Presentation;

internal sealed class IdentityPageRenderer
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    public string RenderConfirmEmail(ConfirmEmailPageModel model) =>
        $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>Confirm email</title>
        </head>
        <body>
          <main>
            <h1>Confirm your email</h1>
            <p>Confirm that you want to activate this account.</p>
            <form method="post" action="/account/confirm-email">
              <input type="hidden" name="__RequestVerificationToken" value="{Encoder.Encode(model.AntiforgeryToken)}">
              <input type="hidden" name="userId" value="{model.UserId:D}">
              <input type="hidden" name="code" value="{Encoder.Encode(model.Code)}">
              <button type="submit">Confirm email</button>
            </form>
          </main>
        </body>
        </html>
        """;

    public string RenderEmailConfirmed() =>
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>Email confirmed</title>
        </head>
        <body>
          <main>
            <h1>Email confirmed</h1>
            <p>You can return to the application and sign in.</p>
          </main>
        </body>
        </html>
        """;

    public string RenderLogout(LogoutPageModel model) =>
        $"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>Sign out</title>
        </head>
        <body>
          <main>
            <h1>Sign out</h1>
            <p>Confirm that you want to end your identity session.</p>
            <form method="post" action="{Encoder.Encode(model.Action)}">
              <input type="hidden" name="__RequestVerificationToken" value="{Encoder.Encode(model.AntiforgeryToken)}">
              <button type="submit">Sign out</button>
            </form>
          </main>
        </body>
        </html>
        """;
}
