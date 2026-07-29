using FluentValidation;

namespace Customer.Api.Features.Customers.Exporting.V1;

internal sealed class ExportCustomerValidator
    : AbstractValidator<ExportCustomerQuery>
{
    public ExportCustomerValidator()
    {
        RuleFor(query => query.Provider).NotEmpty().MaximumLength(32);
        RuleFor(query => query.Subject).NotEmpty().MaximumLength(255);
    }
}
