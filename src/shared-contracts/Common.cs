namespace PulseCheck.Contracts;

public static class PulseCheckConstants
{
    public const string ProductName = "PulseCheck";
    public const string ApiVersion = "v1";
    public const string DataVersion = "2026.10";
    public const string DefaultServiceName = "PulseCheckAgent";
    public const int DefaultApiPort = 17821;
    public const int DefaultDiscoveryPort = 17820;
}

public enum OperatingMode
{
    Restricted,
    Full
}

public enum DataAvailability
{
    Available,
    Unavailable,
    RequiresAdministrator,
    RequiresSensorDriver,
    NotSupported,
    PermissionDenied,
    Error
}

public sealed record MetricAvailability(
    DataAvailability Status,
    string? Reason = null);

public sealed record ApiError(
    string Code,
    string Message,
    string? Detail = null,
    string? TraceId = null);

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error,
    DateTimeOffset TimestampUtc)
{
    public static ApiResponse<T> Ok(T data) =>
        new(true, data, null, DateTimeOffset.UtcNow);

    public static ApiResponse<T> Fail(string code, string message, string? detail = null) =>
        new(false, default, new ApiError(code, message, detail), DateTimeOffset.UtcNow);
}

public sealed record PageResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Offset,
    int Limit);