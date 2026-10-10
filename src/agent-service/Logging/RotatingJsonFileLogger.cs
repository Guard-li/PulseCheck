using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PulseCheck.AgentService.Logging;

public sealed class RotatingJsonFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private long _currentSize;

    public RotatingJsonFileLoggerProvider(string dataDirectory)
    {
        _directory = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(_directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception, IReadOnlyDictionary<string, object?>? state)
    {
        lock (_gate)
        {
            EnsureWriter();
            var record = new Dictionary<string, object?>
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["level"] = level.ToString(),
                ["eventId"] = eventId.Id,
                ["category"] = category,
                ["message"] = message,
                ["exception"] = exception?.ToString(),
                ["state"] = state
            };
            var line = JsonSerializer.Serialize(record, _json);
            _writer!.WriteLine(line);
            _writer.Flush();
            _currentSize += System.Text.Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            if (_currentSize > 10 * 1024 * 1024) Rollover();
        }
    }

    private void EnsureWriter()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_writer is not null && _currentDate == today) return;
        _writer?.Dispose();
        _currentDate = today;
        var path = Path.Combine(_directory, $"pulsecheck-{today:yyyyMMdd}.jsonl");
        _currentSize = File.Exists(path) ? new FileInfo(path).Length : 0;
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false));
    }

    private void Rollover()
    {
        _writer?.Dispose();
        var source = Path.Combine(_directory, $"pulsecheck-{_currentDate:yyyyMMdd}.jsonl");
        var index = 1;
        string target;
        do { target = Path.Combine(_directory, $"pulsecheck-{_currentDate:yyyyMMdd}.{index++}.jsonl"); }
        while (File.Exists(target));
        File.Move(source, target);
        _currentSize = 0;
        _writer = new StreamWriter(new FileStream(source, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new System.Text.UTF8Encoding(false));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly RotatingJsonFileLoggerProvider _provider;
        private readonly string _category;
        public FileLogger(RotatingJsonFileLoggerProvider provider, string category) { _provider = provider; _category = category; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state as IReadOnlyDictionary<string, object?>;
            var message = formatter(state, exception);
            if (message.Contains("Token", StringComparison.OrdinalIgnoreCase) || message.Contains("token", StringComparison.OrdinalIgnoreCase))
                message = "[日志消息包含敏感字段名，正文已省略]";
            _provider.Write(logLevel, _category, eventId, message, exception, values);
        }
    }
}