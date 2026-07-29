namespace Microservices.Primitives;

public enum ErrorCategory
{
    None = 0,
    InvalidInput = 1,
    MissingResource = 2,
    StateConflict = 3,
    ConcurrencyConflict = 4,
    AuthenticationRequired = 5,
    AuthorizationDenied = 6,
    PreconditionRequired = 7,
    Unexpected = 8
}
