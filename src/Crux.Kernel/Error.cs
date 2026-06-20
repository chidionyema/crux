namespace Crux.Kernel;

/// <summary>
/// Categorizes the type of error for HTTP status code mapping (done in the API layer).
/// </summary>
public enum ErrorType
{
    None,
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Timeout,
    Internal,
    Unauthorized,
    Failure
}

/// <summary>
/// A type-safe error with a code, message, and category. Used throughout the
/// application instead of stringly-typed exceptions for expected failures.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Internal)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Timeout(string code, string message) => new(code, message, ErrorType.Timeout);
    public static Error Internal(string code, string message) => new(code, message, ErrorType.Internal);

    /// <summary>Validation error conventions shared across all domains.</summary>
    public static class ValidationErrors
    {
        public static readonly Error InvalidEmail = new("Validation.InvalidEmail", "Invalid email format");
        public static readonly Error RequiredField = new("Validation.Required", "Field is required");
        public static readonly Error InvalidQuantity = new("Validation.InvalidQuantity", "Quantity must be greater than zero");
        public static readonly Error TooManyItems = new("Validation.TooManyItems", "Too many items in order");
    }
}
