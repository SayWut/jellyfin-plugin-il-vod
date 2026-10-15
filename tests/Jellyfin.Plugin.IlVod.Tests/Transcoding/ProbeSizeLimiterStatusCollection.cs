namespace Jellyfin.Plugin.IlVod.Tests.Transcoding;

/// <summary>
/// <see cref="Jellyfin.Plugin.IlVod.Transcoding.ProbeSizeLimiterStatus"/> is a single
/// process-wide flag. xunit runs different test classes in parallel by default, so every test
/// class that reads or writes it is tagged with this collection to force them to run
/// sequentially instead of racing each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ProbeSizeLimiterStatusCollection
{
    public const string Name = "ProbeSizeLimiterStatus";
}
