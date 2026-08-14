using FluentValidation;

namespace Product.Api.Features.Products.GettingById.V1;

internal sealed class GetProductByIdValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdValidator() => RuleFor(query => query.ProductId).NotEmpty();
}
