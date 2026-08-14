using FluentValidation;

namespace Product.Api.Features.Products.Listing.V1;

internal sealed class ListProductsValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Page)
            .Must((query, page) =>
                ((long)page - 1) * query.PageSize <= int.MaxValue)
            .When(query => query.Page >= 1 && query.PageSize >= 1)
            .WithMessage("The requested page is outside the supported pagination range.");
    }
}
