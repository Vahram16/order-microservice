using Customer.Api.Features.Customers.Common;

namespace Customer.Api.Features.Customers.Exporting.V1;

public sealed record CustomerExportResponse(
    DateTimeOffset ExportedAt,
    CustomerResponse Customer);
