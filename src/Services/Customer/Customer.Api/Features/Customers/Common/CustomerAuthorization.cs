namespace Customer.Api.Features.Customers.Common;

internal static class CustomerAuthorization
{
    public const string Role = "customer-user";
    public const string ReadScope = "customers.self.read";
    public const string UpdateScope = "customers.self.update";
    public const string AddressWriteScope = "customers.addresses.write";
    public const string ExportScope = "customers.self.export";
    public const string DeleteScope = "customers.self.delete";
}
