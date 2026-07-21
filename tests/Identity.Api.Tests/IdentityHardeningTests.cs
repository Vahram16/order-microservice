using System.Reflection;
using System.Text.Json;
using Identity.Api.Notifications;
using Identity.Api.Persistence;
using Identity.Api.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Api.Tests;

public sealed class IdentityHardeningTests
{
    [Fact]
    public void ProvisionerRefusesToAdoptOperatorManagedRegistrations()
    {
        var properties = new Dictionary<string, JsonElement>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AuthorizationServerProvisioner.EnsureManaged(
                properties,
                "client",
                "operator-client"));

        Assert.Contains("not owned", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Refusing to adopt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionerAllowsReconciliationOfOwnedRegistrations()
    {
        var properties = new Dictionary<string, JsonElement>
        {
            [AuthorizationServerProvisioner.OwnershipProperty] =
                JsonSerializer.SerializeToElement(
                    AuthorizationServerProvisioner.OwnershipValue)
        };

        AuthorizationServerProvisioner.EnsureManaged(
            properties,
            "client",
            "managed-client");
    }

    [Fact]
    public void NotificationWorkerCreatesScopesInsteadOfCapturingDbContext()
    {
        var workerParameters = GetConstructorParameters(
            typeof(IdentityNotificationOutboxWorker));
        var dispatcherParameters = GetConstructorParameters(
            typeof(IdentityNotificationOutboxDispatcher));

        Assert.Contains(
            workerParameters,
            parameter => parameter.ParameterType == typeof(IServiceScopeFactory));
        Assert.DoesNotContain(
            workerParameters,
            parameter => parameter.ParameterType == typeof(IdentityServiceDbContext));
        Assert.Contains(
            dispatcherParameters,
            parameter => parameter.ParameterType == typeof(IdentityServiceDbContext));
    }

    [Fact]
    public void NotificationRetryStopsBeforeTokenExpiry()
    {
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

        var nextAttempt = IdentityNotificationOutboxDispatcher.GetNextAttemptAtUtc(
            completedAttempts: 5,
            maximumAttempts: 12,
            now,
            expiresAtUtc: now + TimeSpan.FromMinutes(1));

        Assert.Null(nextAttempt);
    }

    [Fact]
    public void NotificationRetryUsesExponentialBackoffWithinTokenLifetime()
    {
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

        var nextAttempt = IdentityNotificationOutboxDispatcher.GetNextAttemptAtUtc(
            completedAttempts: 3,
            maximumAttempts: 12,
            now,
            expiresAtUtc: now + TimeSpan.FromHours(2));

        Assert.Equal(now + TimeSpan.FromSeconds(40), nextAttempt);
    }

    [Fact]
    public void NotificationRetryStopsAtMaximumAttempts()
    {
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

        var nextAttempt = IdentityNotificationOutboxDispatcher.GetNextAttemptAtUtc(
            completedAttempts: 12,
            maximumAttempts: 12,
            now,
            expiresAtUtc: now + TimeSpan.FromHours(2));

        Assert.Null(nextAttempt);
    }

    private static ParameterInfo[] GetConstructorParameters(Type type) =>
        type.GetConstructors(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic)
            .Single()
            .GetParameters();
}
