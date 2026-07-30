using Microsoft.AspNetCore.Routing;

namespace Customer.Api.Features.Customers.Common;

internal static class CustomerErrorCatalog
{
    private static readonly Dictionary<string, CustomerProblemDescriptor> Descriptors =
        new(StringComparer.Ordinal)
        {
            ["customer.not_found"] = new(
                "customer.not_found",
                "Customer not found",
                ErrorCategory.MissingResource,
                "No customer aggregate exists for the authenticated identity.",
                false),
            ["customer.address_not_found"] = new(
                "customer.address_not_found",
                "Customer address not found",
                ErrorCategory.MissingResource,
                "The requested address is not owned by the customer aggregate.",
                false),
            ["customer.inactive"] = new(
                "customer.inactive",
                "Customer is not active",
                ErrorCategory.StateConflict,
                "The requested mutation is not allowed in the customer's current lifecycle state.",
                false),
            ["customer.address_limit_exceeded"] = new(
                "customer.address_limit_exceeded",
                "Address limit reached",
                ErrorCategory.StateConflict,
                "The customer aggregate already contains the maximum allowed number of saved addresses.",
                false),
            ["customer.version_mismatch"] = new(
                "customer.version_mismatch",
                "Customer version mismatch",
                ErrorCategory.ConcurrencyConflict,
                "The aggregate changed after the supplied ETag was issued.",
                true),
            ["customer.validation"] = new(
                "customer.validation",
                "Customer validation failed",
                ErrorCategory.InvalidInput,
                "A customer-domain value failed invariant validation.",
                false),
            ["customer.invalid_email"] = new(
                "customer.invalid_email",
                "Invalid email",
                ErrorCategory.InvalidInput,
                "The email value is not a valid normalized email address.",
                false),
            ["customer.invalid_country_code"] = new(
                "customer.invalid_country_code",
                "Invalid country code",
                ErrorCategory.InvalidInput,
                "The country code is not an ISO 3166-1 alpha-2 value.",
                false),
            ["customer.precondition_required"] = new(
                "customer.precondition_required",
                "Precondition required",
                ErrorCategory.PreconditionRequired,
                "A current strong customer ETag is required for this operation.",
                false),
            ["customer.invalid_precondition"] = new(
                "customer.invalid_precondition",
                "Invalid precondition",
                ErrorCategory.InvalidInput,
                "The If-Match header is not a valid strong customer ETag.",
                false),
            ["customer.invalid_idempotency_key"] = new(
                "customer.invalid_idempotency_key",
                "Invalid idempotency key",
                ErrorCategory.InvalidInput,
                "The Idempotency-Key header is not a single non-empty GUID.",
                false),
            ["customer.authentication_required"] = new(
                "customer.authentication_required",
                "Authentication required",
                ErrorCategory.AuthenticationRequired,
                "A valid access token with the required identity claims is required.",
                false),
            ["customer.invalid_identity_claims"] = new(
                "customer.invalid_identity_claims",
                "Invalid identity claims",
                ErrorCategory.AuthenticationRequired,
                "The access token contains malformed or unsupported identity claims.",
                false),
            ["customer.default_shipping_conflict"] = new(
                "customer.default_shipping_conflict",
                "Default shipping address conflict",
                ErrorCategory.ConcurrencyConflict,
                "A concurrent request changed the default shipping address.",
                true),
            ["customer.default_billing_conflict"] = new(
                "customer.default_billing_conflict",
                "Default billing address conflict",
                ErrorCategory.ConcurrencyConflict,
                "A concurrent request changed the default billing address.",
                true),
            ["customer.idempotency_key_reused"] = new(
                "customer.idempotency_key_reused",
                "Idempotency key reused",
                ErrorCategory.StateConflict,
                "The idempotency key was previously committed with different request data.",
                false)
        };

    internal static CustomerProblemDescriptor GetRequired(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (!Descriptors.TryGetValue(error.Code, out var descriptor))
        {
            throw new InvalidOperationException(
                $"Error code '{error.Code}' is not registered in the Customer error catalog.");
        }

        if (descriptor.Category != error.Category)
        {
            throw new InvalidOperationException(
                $"Error code '{error.Code}' uses category '{error.Category}', but the catalog requires '{descriptor.Category}'.");
        }

        return descriptor;
    }

    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
                "/errors/v1/customer/{code}",
                IResult (string code) =>
                {
                    if (!Descriptors.TryGetValue(code, out var descriptor))
                    {
                        return Results.NotFound();
                    }

                    return Results.Ok(new
                    {
                        type = descriptor.Type,
                        descriptor.Code,
                        descriptor.Title,
                        descriptor.Status,
                        description = descriptor.Description,
                        descriptor.Retryable
                    });
                })
            .WithName("GetCustomerErrorDescriptionV1")
            .WithSummary("Describes a stable version 1 Customer API Problem Details type.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();
    }
}
