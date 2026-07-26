using Microsoft.Extensions.FileProviders;
using Notifications.Api.Configuration;

namespace Notifications.Api.Tests;

public sealed class NotificationOptionsTests
{
    [Fact]
    public void ProductionRejectsPostmarkTestToken()
    {
        var validator = new PostmarkOptionsValidator(new TestHostEnvironment("Production"));

        var result = validator.Validate(
            null,
            new PostmarkOptions
            {
                ServerToken = "POSTMARK_API_TEST",
                FromAddress = "notifications@example.com"
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void ProductionRejectsDevelopmentIngressKey()
    {
        var validator = new NotificationsIngressOptionsValidator(
            new TestHostEnvironment("Production"));

        var result = validator.Validate(
            null,
            new NotificationsIngressOptions
            {
                ApiKey = "local-development-notifications-webhook-key-2026"
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void DeliveryOptionsRejectUnboundedAttempts()
    {
        var validator = new NotificationDeliveryOptionsValidator();

        var result = validator.Validate(
            null,
            new NotificationDeliveryOptions { MaximumAttempts = 100 });

        Assert.True(result.Failed);
    }

    private sealed class TestHostEnvironment(string environmentName)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Notifications.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
