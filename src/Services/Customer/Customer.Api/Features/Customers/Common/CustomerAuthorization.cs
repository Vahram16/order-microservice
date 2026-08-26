namespace Customer.Api.Features.Customers.Common;

internal static class CustomerAuthorization
{
    public const string ReadRole = "customers.self.read";
    public const string UpdateRole = "customers.self.update";
    public const string AddressWriteRole = "customers.addresses.write";
    public const string ExportRole = "customers.self.export";
    public const string DeleteRole = "customers.self.delete";
}
