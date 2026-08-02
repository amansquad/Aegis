namespace Aegis.Domain.Common;

/// <summary>
/// Classifies a failure so that the API layer can map it to the correct HTTP status code without
/// the domain or application layers ever referencing HTTP.
/// </summary>
public enum ErrorType
{
    /// <summary>An unclassified business rule violation. Maps to 400.</summary>
    Failure = 0,

    /// <summary>Input failed validation. Maps to 400 with a field-level error dictionary.</summary>
    Validation = 1,

    /// <summary>The requested resource does not exist, or is not visible to this tenant. Maps to 404.</summary>
    NotFound = 2,

    /// <summary>The request conflicts with current state (duplicate key, stale version). Maps to 409.</summary>
    Conflict = 3,

    /// <summary>The caller is not authenticated. Maps to 401.</summary>
    Unauthorized = 4,

    /// <summary>The caller is authenticated but lacks permission. Maps to 403.</summary>
    Forbidden = 5,

    /// <summary>An upstream dependency failed. Maps to 502.</summary>
    External = 6,
}

/// <summary>
/// A machine-readable failure description: a stable code, a human-readable message, and a type.
/// </summary>
/// <param name="Code">
/// A stable, dot-delimited identifier such as <c>Asset.NotFound</c>. Clients branch on this;
/// it must not change once released, even if the message is reworded or localised.
/// </param>
/// <param name="Message">A human-readable description, safe to display to an end user.</param>
/// <param name="Type">The failure classification used for HTTP status mapping.</param>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    /// <summary>Sentinel representing the absence of an error.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>
    /// Field-level failures, keyed by property name, for validation errors.
    /// </summary>
    /// <remarks>
    /// Carried on <see cref="Error"/> itself rather than on a derived <c>ValidationError</c> type.
    /// A single concrete error type keeps pattern matching in the HTTP translation layer to one
    /// switch on <see cref="Type"/>, and record inheritance would complicate the value equality
    /// that <see cref="Result"/> relies on.
    /// </remarks>
    public IReadOnlyDictionary<string, string[]>? Details { get; init; }

    /// <summary>
    /// Creates a <see cref="ErrorType.Validation"/> error carrying field-level failures, as
    /// produced by the FluentValidation pipeline behaviour.
    /// </summary>
    public static Error Validation(IReadOnlyDictionary<string, string[]> details) =>
        new("Validation.Failed", "One or more validation errors occurred.", ErrorType.Validation)
        {
            Details = details,
        };

    /// <summary>Creates a <see cref="ErrorType.NotFound"/> error.</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Creates a <see cref="ErrorType.Conflict"/> error.</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Creates a <see cref="ErrorType.Validation"/> error.</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>Creates a <see cref="ErrorType.Unauthorized"/> error.</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>Creates a <see cref="ErrorType.Forbidden"/> error.</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>Creates an <see cref="ErrorType.External"/> error for upstream dependency faults.</summary>
    public static Error External(string code, string message) => new(code, message, ErrorType.External);
}
