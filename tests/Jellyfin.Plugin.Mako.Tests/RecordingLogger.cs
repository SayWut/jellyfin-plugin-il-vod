using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako.Tests;

/// <summary>
/// Hand-written fake logger (no mocking library, matching the rest of the test suite) that
/// records formatted messages by level, so a test can assert a warning was, or was not, written
/// without inspecting a real logging sink.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            Warnings.Add(formatter(state, exception));
        }
    }
}
