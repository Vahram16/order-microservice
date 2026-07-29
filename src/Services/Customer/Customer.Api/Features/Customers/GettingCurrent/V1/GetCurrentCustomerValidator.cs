using FluentValidation;

namespace Customer.Api.Features.Customers.GettingCurrent.V1;

internal sealed class GetCurrentCustomerValidator
    : AbstractValidator<GetCurrentCustomerQuery>
{
    public GetCurrentCustomerValidator()
    {
        RuleFor(query => query.Provider).NotEmpty().MaximumLength(32);
        RuleFor(query => query.Subject).NotEmpty().MaximumLength(255);
    }
}
