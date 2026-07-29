using Microservices.Application;

namespace Customer.Api.Features.Customers.Exporting.V1;

internal sealed record ExportCustomerQuery(
    string Provider,
    string Subject)
    : IQuery<CustomerExportResponse>;
