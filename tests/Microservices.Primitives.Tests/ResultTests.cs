using System.Globalization;
using Microservices.Primitives;

namespace Microservices.Primitives.Tests;

public sealed class ResultTests
{
    [Fact]
    public void SuccessExposesValueAndNotError()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void SuccessRejectsNullReferenceValue()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));
    }

    [Fact]
    public void FailureExposesErrorAndNotValue()
    {
        var error = OperationError.StateConflict("test.conflict", "Conflict.");
        Result<int> result = error;

        Assert.True(result.IsFailure);
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void MapTransformsSuccess()
    {
        var result = Result.Success(21);

        var mapped = result.Map(value => value * 2);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(42, mapped.Value);
    }

    [Fact]
    public void BindShortCircuitsFailureWithoutReplacingError()
    {
        var error = OperationError.InvalidInput("test.validation", "Invalid.");
        Result<int> result = error;
        var invoked = false;

        var bound = result.Bind(value =>
        {
            invoked = true;
            return Result.Success(value.ToString(CultureInfo.InvariantCulture));
        });

        Assert.False(invoked);
        Assert.True(bound.IsFailure);
        Assert.Same(error, bound.Error);
    }

    [Fact]
    public void BindRejectsNullResult()
    {
        var result = Result.Success(42);

        Assert.Throws<InvalidOperationException>(() =>
            result.Bind<string>(_ => null!));
    }

    [Fact]
    public void ErrorCopiesPublicMetadata()
    {
        var source = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["maximum"] = 20
        };
        var error = OperationError.StateConflict("test.limit", "Limit reached.", source);

        source["maximum"] = 99;

        Assert.Equal(20, error.Metadata["maximum"]);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<string, object?>)error.Metadata).Add("extra", true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" field ")]
    public void ErrorRejectsInvalidMetadataKeys(string metadataKey)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [metadataKey] = true
        };

        Assert.Throws<ArgumentException>(() =>
            OperationError.InvalidInput("test.invalid_metadata", "Invalid.", metadata));
    }

    [Fact]
    public void NonGenericResultMaintainsSuccessFailureInvariant()
    {
        var success = Result.Success();
        var error = OperationError.Unexpected("test.failure", "Failed.");
        Result failure = error;

        Assert.True(success.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => success.Error);
        Assert.True(failure.IsFailure);
        Assert.Same(error, failure.Error);
    }
}
