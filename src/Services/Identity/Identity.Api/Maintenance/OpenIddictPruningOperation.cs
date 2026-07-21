using OpenIddict.Abstractions;

namespace Identity.Api.Maintenance;

internal interface IOpenIddictPruner
{
    ValueTask<long> PruneTokensAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken);

    ValueTask<long> PruneAuthorizationsAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken);
}

internal sealed class OpenIddictPruner(
    IOpenIddictTokenManager tokenManager,
    IOpenIddictAuthorizationManager authorizationManager)
    : IOpenIddictPruner
{
    public ValueTask<long> PruneTokensAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken) =>
        tokenManager.PruneAsync(threshold, cancellationToken);

    public ValueTask<long> PruneAuthorizationsAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken) =>
        authorizationManager.PruneAsync(threshold, cancellationToken);
}

internal readonly record struct OpenIddictPruningResult(
    long Tokens,
    long Authorizations);

internal sealed class OpenIddictPruningOperation(IOpenIddictPruner pruner)
{
    public async ValueTask<OpenIddictPruningResult> ExecuteAsync(
        DateTimeOffset threshold,
        CancellationToken cancellationToken)
    {
        var tokens = await pruner.PruneTokensAsync(threshold, cancellationToken);
        var authorizations = await pruner.PruneAuthorizationsAsync(
            threshold,
            cancellationToken);

        return new OpenIddictPruningResult(tokens, authorizations);
    }
}
