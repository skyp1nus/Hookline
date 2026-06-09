namespace Hookline.SharedKernel.Common;

/// <summary>A machine-readable error, mapped to RFC-7807 ProblemDetails at the edge.</summary>
public sealed record Error(string Code, string Message, int Status = 400)
{
    public static readonly Error NotFound = new("not_found", "The requested resource was not found.", 404);
    public static readonly Error Unauthorized = new("unauthorized", "Authentication is required.", 401);
    public static readonly Error Forbidden = new("forbidden", "You do not have permission to do that.", 403);
    public static Error Conflict(string message) => new("conflict", message, 409);
    public static Error Validation(string message) => new("validation", message, 400);
}

/// <summary>Outcome of an operation that can fail without throwing.</summary>
public readonly record struct Result
{
    public bool IsSuccess { get; }
    public Error? Error { get; }

    private Result(bool ok, Error? error)
    {
        IsSuccess = ok;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Outcome carrying a value on success.</summary>
public readonly record struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    private Result(bool ok, T? value, Error? error)
    {
        IsSuccess = ok;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
