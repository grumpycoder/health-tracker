namespace FitRecoveryLog.Application.Common;

/// <summary>Outcome of an operation that can fail for an expected reason (no exception).</summary>
public readonly record struct Result(bool IsSuccess, string? Error)
{
    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}

/// <summary>Outcome carrying a value on success.</summary>
public readonly record struct Result<T>(bool IsSuccess, T? Value, string? Error)
{
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
