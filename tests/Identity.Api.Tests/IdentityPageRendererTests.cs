using Identity.Api.Features.Presentation;

namespace Identity.Api.Tests;

public sealed class IdentityPageRendererTests
{
    private readonly IdentityPageRenderer _renderer = new();

    [Fact]
    public void ConfirmationPageEncodesAttributeValues()
    {
        var page = _renderer.RenderConfirmEmail(
            new ConfirmEmailPageModel(
                Guid.Parse("2a43fb37-f199-43f8-a573-34ebcb93446d"),
                "\"><script>alert(1)</script>",
                "<antiforgery>"));

        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
        Assert.Contains("&lt;antiforgery&gt;", page);
    }

    [Fact]
    public void LogoutPageEncodesTheFormAction()
    {
        var page = _renderer.RenderLogout(
            new LogoutPageModel(
                "/connect/logout?returnUrl=\"><script>alert(1)</script>",
                "token"));

        Assert.DoesNotContain("<script>", page);
        Assert.Contains("&lt;script&gt;", page);
    }
}
