using System.Reflection;
using Jellyfin.Plugin.IlVod.Core;
using Jellyfin.Plugin.IlVod.Providers.Mako;
using Jellyfin.Plugin.IlVod.Providers.Mako.Transform;

namespace Jellyfin.Plugin.IlVod.Tests.Providers.Mako;

/// <summary>
/// <see cref="MakoTransformer"/> on its own: mako's records in, the shared model out, with no
/// HTTP, cache or clock anywhere — the point of keeping it apart from <c>MakoSource</c>.
/// </summary>
public class MakoTransformerTests
{
    private static readonly MakoTransformer Transformer = new();
    private static readonly EpisodeKey FirstKey = new("vcm1", "channel1", "gallery1");
    private static readonly EpisodeKey SecondKey = new("vcm2", "channel1", "gallery1");

    [Fact]
    public void HoldsNoStateSoItCanOnlyConvert()
    {
        FieldInfo[] fields = typeof(MakoTransformer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(fields);
    }

    [Fact]
    public void AShowInTwoGenresIsOneShowCarryingBothInMakosOrder()
    {
        MakoShowIndex index = new(
        [
            new MakoGenreList("דרמה", [new IndexEntry("/a", "א", "https://img/a.jpg"), new IndexEntry("/b", "ב", null)]),
            new MakoGenreList("קומדיה", [new IndexEntry("/b", "ב", null), new IndexEntry("/c", "ג", null)]),
        ]);

        IReadOnlyList<Show> shows = Transformer.ToShows(index);

        Assert.Equal(["/a", "/b", "/c"], shows.Select(show => show.Id));
        Assert.Equal(["דרמה", "קומדיה"], shows[1].Genres);
        Assert.Equal("https://img/a.jpg", shows[0].PosterUrl);
    }

    [Fact]
    public void AShowListedTwiceInOneGenreCarriesThatGenreOnce()
    {
        MakoShowIndex index = new([new MakoGenreList("דרמה", [new IndexEntry("/a", "א", null), new IndexEntry("/a", "א", null)])]);

        Show show = Assert.Single(Transformer.ToShows(index));

        Assert.Equal(["דרמה"], show.Genres);
    }

    [Fact]
    public void SeasonsKeepTheirPagePathAsTheirId()
    {
        ShowPage page = new(Overview: null, PosterUrl: null, Seasons: [new MakoSeason("/show/s2", 2, "עונה 2")], DisplayedSeasonId: "/show/s2");

        Season season = Assert.Single(Transformer.ToSeasons(page));

        Assert.Equal(new Season("/show/s2", 2, "עונה 2"), season);
    }

    [Fact]
    public void EpisodesCarryTheSeasonPagesShowAndSeasonAndAreNumberedFromExtraInfoOrPosition()
    {
        MakoSeasonPage page = new("שמש", 3,
        [
            new MakoEpisodeEntry(FirstKey, "ראשון", ExtraInfo: "פרק 7 @01.02.25", ThumbnailUrl: "https://img/1.jpg"),
            new MakoEpisodeEntry(SecondKey, "שני", ExtraInfo: null, ThumbnailUrl: null),
        ]);

        IReadOnlyList<Episode> episodes = Transformer.ToEpisodes(page);

        Assert.Equal(FirstKey.ToString(), episodes[0].Id);
        Assert.Equal(("שמש", 3, 7), (episodes[0].ShowName, episodes[0].SeasonNumber, episodes[0].Number));
        Assert.Equal(new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc), episodes[0].AirDate);
        Assert.Equal("https://img/1.jpg", episodes[0].ThumbnailUrl);
        Assert.Equal(2, episodes[1].Number);
        Assert.Null(episodes[1].AirDate);
    }

    [Fact]
    public void AStreamWithAVariantIsDescribedFromItInMakosFormat()
    {
        MakoResolvedStream stream = new("https://cdn/master.m3u8", new MakoStreamVariant(1920, 1080, 5_000_000, "High", 4.0), TimeSpan.FromMinutes(25));

        StreamLink link = Transformer.ToStreamLink(stream);

        Assert.Equal("https://cdn/master.m3u8", link.Url);
        Assert.Equal("hls", link.Container);
        Assert.Equal((1920, 1080, 5_000_000, "High", 4.0), (link.Description.Width, link.Description.Height, link.Description.BitsPerSecond, link.Description.VideoProfile, link.Description.VideoLevel));
        Assert.Equal(TimeSpan.FromMinutes(25), link.Description.Duration);
        Assert.Equal(MakoStreams.Format, link.Description.Format);
    }

    [Fact]
    public void AStreamWithNoVariantIsDescribedAsMakosDefault()
    {
        StreamLink link = Transformer.ToStreamLink(new MakoResolvedStream("https://cdn/master.m3u8", Variant: null, Duration: null));

        Assert.Equal(MakoStreams.DefaultDescription, link.Description);
    }
}
