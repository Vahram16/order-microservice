namespace Order.Api.Persistence;

internal static class OrderDatabaseConstraints
{
    public const string SubmissionPrimaryKey = "pk_order_submissions";
    public const string SubmissionOrder = "ux_order_submissions_order";
    public const string CustomerIdentity = "ux_order_customers_identity";
    public const string ReferenceDataSynchronizationPrimaryKey = "pk_order_reference_data_synchronization";
}
