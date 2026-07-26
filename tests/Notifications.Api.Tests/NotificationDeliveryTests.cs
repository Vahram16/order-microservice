using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Api.Delivery;
using Notifications.Api.Persistence;

namespace Notifications.Api.Tests;

public sealed class NotificationDeliveryTests
{
    [Fact]
    public void DeliveryWorkerCreatesScopesInsteadOfCapturingDbContext()
    {
        var workerParameters = GetConstructorParameters(typeof(NotificationDeliveryWorker));
        var dispatcherParameters = GetConstructorParameters(typeof(NotificationDeliveryDispatcher));

        Assert.Contains(
            workerParameters,
            parameter => parameter.ParameterType == typeof(IServiceScopeFactory));
        Assert.DoesNotContain(
            workerParameters,
            parameter => parameter.ParameterType == typeof(NotificationDbContext));
        Assert.Contains(
            dispatcherParameters,
            parameter => parameter.ParameterType == typeof(NotificationDbContext));
    }

    [Fact]
    public void RetryUsesBoundedExponentialBackoff()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        var nextAttempt = NotificationDeliveryDispatcher.GetNextAttemptAtUtc(
            completedAttempts: 3,
            maximumAttempts: 8,
            now,
            expiresAtUtc: now + TimeSpan.FromHours(1));

        Assert.Equal(now + TimeSpan.FromSeconds(60), nextAttempt);
    }

    [Fact]
    public void RetryStopsBeforeNotificationExpiry()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        var nextAttempt = NotificationDeliveryDispatcher.GetNextAttemptAtUtc(
            completedAttempts: 5,
            maximumAttempts: 8,
            now,
            expiresAtUtc: now + TimeSpan.FromMinutes(1));

        Assert.Null(nextAttempt);
    }

    [Fact]
    public void RetryStopsAtMaximumAttempts()
    {
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        var nextAttempt = NotificationDeliveryDispatcher.GetNextAttemptAtUtc(
            completedAttempts: 8,
            maximumAttempts: 8,
            now,
            expiresAtUtc: now + TimeSpan.FromHours(1));

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
