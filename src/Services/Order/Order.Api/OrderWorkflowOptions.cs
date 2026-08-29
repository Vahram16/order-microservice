namespace Order.Api;

internal sealed class OrderWorkflowOptions
{
    public const string SectionName = "OrderWorkflow";

    public TimeSpan CheckoutTimeout { get; init; } = TimeSpan.FromMinutes(15);
}
