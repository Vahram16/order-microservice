namespace Identity.Api.Infrastructure;

internal static class AccountEnumerationResistance
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(350);

    public static async Task CompleteAsync(
        TimeProvider timeProvider,
        long startedAt,
        CancellationToken cancellationToken)
    {
        var remaining = MinimumDuration - timeProvider.GetElapsedTime(startedAt);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, timeProvider, cancellationToken);
        }
    }
}
