using FluentValidation;

namespace Order.Api.Features.Orders.Creating.V1;

internal sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(command => command.IdempotencyKey).NotEmpty();
        RuleFor(command => command.PaymentMethodId).NotEmpty();
        RuleFor(command => command.IdentityProvider).NotEmpty().MaximumLength(32);
        RuleFor(command => command.IdentitySubject).NotEmpty().MaximumLength(255);
        RuleFor(command => command.Items).NotNull().NotEmpty();
        RuleForEach(command => command.Items).ChildRules(item =>
        {
            item.RuleFor(value => value.ProductId).NotEmpty();
            item.RuleFor(value => value.Quantity).GreaterThan(0);
        });
        RuleFor(command => command.ShippingAddress).NotNull();
    }
}
