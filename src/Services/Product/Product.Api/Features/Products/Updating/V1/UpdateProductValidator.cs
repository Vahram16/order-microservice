using FluentValidation;

namespace Product.Api.Features.Products.Updating.V1;

internal sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.ExpectedVersion).GreaterThan(0);
        RuleFor(command => command.Sku)
            .Must(value => ProductInputValidation.IsTrimmedLengthAtMost(
                value,
                Domain.Product.MaximumSkuLength));
        RuleFor(command => command.Name)
            .Must(value => ProductInputValidation.IsTrimmedLengthAtMost(
                value,
                Domain.Product.MaximumNameLength));
        RuleFor(command => command.Description)
            .Must(value => ProductInputValidation.IsOptionalTrimmedLengthAtMost(
                value,
                Domain.Product.MaximumDescriptionLength));
        RuleFor(command => command.Price)
            .InclusiveBetween(0m, Domain.Product.MaximumPrice)
            .Must(price => decimal.Round(price, 2) == price)
            .WithMessage("Price can have at most two decimal places.");
        RuleFor(command => command.CurrencyCode)
            .Must(ProductInputValidation.IsCurrencyCode);
    }
}
