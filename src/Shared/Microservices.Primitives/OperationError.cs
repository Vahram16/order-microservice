using System.Collections.ObjectModel;

namespace Microservices.Primitives;

public sealed class OperationError
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyMetadata =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

    private OperationError(
        string code,
        string publicDescription,
        ErrorCategory category,
        IReadOnlyDictionary<string, object?>? metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicDescription);

        Code = code.Trim();
        PublicDescription = publicDescription.Trim();
        Category = category;
        Metadata = CopyMetadata(metadata);
    }

    public string Code { get; }

    public string PublicDescription { get; }

    public ErrorCategory Category { get; }

    public IReadOnlyDictionary<string, object?> Metadata { get; }

    public static OperationError InvalidInput(
        string code,
        string publicDescription,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, publicDescription, ErrorCategory.InvalidInput, metadata);

    public static OperationError MissingResource(
        string code,
        string publicDescription,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, publicDescription, ErrorCategory.MissingResource, metadata);

    public static OperationError StateConflict(
        string code,
        string publicDescription,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, publicDescription, ErrorCategory.StateConflict, metadata);

    public static OperationError ConcurrencyConflict(
        string code,
        string publicDescription,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, publicDescription, ErrorCategory.ConcurrencyConflict, metadata);

    public static OperationError AuthenticationRequired(
        string code,
        string publicDescription,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, publicDescription, ErrorCategory.AuthenticationRequired, metadata);

    public static OperationError AuthorizationDenied(
        string code,
        string publicDescription,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, publicDescription, ErrorCategory.AuthorizationDenied, metadata);

    public static OperationError PreconditionRequired(
        string code,
        string publicDescription,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, publicDescription, ErrorCategory.PreconditionRequired, metadata);

    public static OperationError Unexpected(
        string code,
        string publicDescription,
        IReadOnlyDictionary<string, object?>? metadata = null) =>
        new(code, publicDescription, ErrorCategory.Unexpected, metadata);

    private static IReadOnlyDictionary<string, object?> CopyMetadata(
        IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return EmptyMetadata;
        }

        var copy = new Dictionary<string, object?>(metadata.Count, StringComparer.Ordinal);
        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) ||
                !string.Equals(pair.Key, pair.Key.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Metadata keys must be non-empty and cannot contain leading or trailing whitespace.",
                    nameof(metadata));
            }

            copy.Add(pair.Key, pair.Value);
        }

        return new ReadOnlyDictionary<string, object?>(copy);
    }
}
