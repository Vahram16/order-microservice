using FluentValidation;
using MediatR;
using Microservices.Application;

namespace Microservices.Application.Tests;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task HandleWithoutValidatorsInvokesNext()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        var nextInvocations = 0;

        Task<string> Next(CancellationToken cancellationToken)
        {
            nextInvocations++;
            return Task.FromResult("handled");
        }

        var response = await behavior.Handle(
            new TestRequest("value"),
            Next,
            CancellationToken.None);

        Assert.Equal("handled", response);
        Assert.Equal(1, nextInvocations);
    }

    [Fact]
    public async Task HandleWithValidRequestInvokesNextAndPropagatesCancellationToken()
    {
        var validatorCancellationToken = CancellationToken.None;
        var nextCancellationToken = CancellationToken.None;
        using var cancellationTokenSource = new CancellationTokenSource();
        var validator = new InlineValidator<TestRequest>();
        validator.RuleFor(request => request.Value)
            .MustAsync((_, cancellationToken) =>
            {
                validatorCancellationToken = cancellationToken;
                return Task.FromResult(true);
            });
        var behavior = new ValidationBehavior<TestRequest, string>([validator]);

        Task<string> Next(CancellationToken cancellationToken)
        {
            nextCancellationToken = cancellationToken;
            return Task.FromResult("handled");
        }

        var response = await behavior.Handle(
            new TestRequest("value"),
            Next,
            cancellationTokenSource.Token);

        Assert.Equal("handled", response);
        Assert.Equal(cancellationTokenSource.Token, validatorCancellationToken);
        Assert.Equal(cancellationTokenSource.Token, nextCancellationToken);
    }

    [Fact]
    public async Task HandleWithInvalidRequestAggregatesEachFailureOnceAndDoesNotInvokeNext()
    {
        var requiredValidator = new InlineValidator<TestRequest>();
        requiredValidator.RuleFor(request => request.Value)
            .NotEmpty()
            .WithErrorCode("value.required");

        var formatValidator = new InlineValidator<TestRequest>();
        formatValidator.RuleFor(request => request.Value)
            .Must(_ => false)
            .WithMessage("Value has an invalid format.")
            .WithErrorCode("value.invalid_format");

        var request = new TestRequest(string.Empty);
        var behavior = new ValidationBehavior<TestRequest, string>(
            [requiredValidator, formatValidator]);
        var nextInvoked = false;

        Task<string> Next(CancellationToken cancellationToken)
        {
            nextInvoked = true;
            return Task.FromResult("handled");
        }

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(request, Next, CancellationToken.None));

        Assert.False(nextInvoked);
        var failures = exception.Errors.ToArray();
        Assert.Equal(2, failures.Length);
        Assert.Collection(
            failures,
            failure =>
            {
                Assert.Equal(nameof(TestRequest.Value), failure.PropertyName);
                Assert.Equal("value.required", failure.ErrorCode);
            },
            failure =>
            {
                Assert.Equal(nameof(TestRequest.Value), failure.PropertyName);
                Assert.Equal("value.invalid_format", failure.ErrorCode);
            });
    }

    private sealed record TestRequest(string Value);
}
