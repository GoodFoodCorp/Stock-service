namespace Stock.Domain.Errors;

public enum DomainErrorCode
{
    Validation,
    NotFound,
    Forbidden,
    Conflict,
    InvalidTransition,
}

/// <summary>
/// Typed business error. The API layer maps codes to HTTP status codes;
/// Domain and Application never reference HTTP.
/// </summary>
public sealed class DomainException(DomainErrorCode code, string message) : Exception(message)
{
    public DomainErrorCode Code { get; } = code;

    public static DomainException Validation(string message) => new(DomainErrorCode.Validation, message);

    public static DomainException NotFound(string message) => new(DomainErrorCode.NotFound, message);

    public static DomainException Forbidden(string message) => new(DomainErrorCode.Forbidden, message);

    public static DomainException Conflict(string message) => new(DomainErrorCode.Conflict, message);

    public static DomainException InvalidTransition(string from, string to) =>
        new(DomainErrorCode.InvalidTransition, $"Cannot transition from {from} to {to}.");
}
