namespace Jellyfin.Plugin.Mako.Tests.Transcoding;

/// <summary>
/// Shared mutable sink that <see cref="FakeTranscodeManager"/> writes to. Kept separate from
/// the fake so a test can hold a reference to it even when the fake itself was constructed by
/// a DI container the test does not otherwise touch.
/// </summary>
public sealed class CallRecorder
{
    public string? LastCommandLineArguments { get; set; }

    public int StartFfMpegCallCount { get; set; }

    public string? LastPingedPlaySessionId { get; set; }

    public string? LastRequestedPlaySessionId { get; set; }

    public bool WasDisposed { get; set; }

    public int DisposeCallCount { get; set; }
}
