using System.Text.Json;
using Jellyfin.Plugin.Mako.Cache;
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Model;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

public class StreamResolverTests
{
    private const string AppVersion = "43.0";
    private const string NewPlaylistKey = "TESTKEY-PLAYLIST-NEW-24b";
    private const string OldPlaylistKey = "TESTKEY-PLAYLIST-OLD-24b";
    private const string TokenKey = "TESTKEY-TOKEN-TICKET-24b";
    private const string MediaUrl = "https://cdn.example.test/master.m3u8";
    private const string DecodedTicket = "hdnea=st=1~exp=2~acl=/*~hmac=ab";

    private static readonly string MainConfigJson = FixtureFile.ReadText("main-config.json");
    private static readonly string CoreConfigJson = FixtureFile.ReadText("core-config.json");
    private static readonly string NotesConfigJson = FixtureFile.ReadText("notes-config.json");
    private static readonly string MasterPlaylistText = FixtureFile.ReadText("master.m3u8");

    private static readonly string MainConfigUrl = MakoUrls.MainConfigUrl(AppVersion);
    private static readonly string CoreConfigUrl = MakoConfig.CoreConfigUrlOf(MainConfigJson);
    private static readonly string NotesConfigUrl = MakoConfig.NotesUrlOf(CoreConfigJson);
    private static readonly MakoConfig ParsedConfig = MakoConfig.Parse(MainConfigJson, CoreConfigJson, NotesConfigJson);

    private static readonly EpisodeKey Episode = new("vcm1", "channel1", "gallery1");
    private static readonly string PlaylistUrl =
        MakoUrls.Playlist(ParsedConfig.PlaylistUrlTemplate, Episode.VcmId, Episode.ChannelId, Episode.GalleryChannelId);
    private static readonly string TokenUrl = ParsedConfig.TokenUrl;

    /// <summary>A notes config whose playlist keys match nothing already encrypted with the real
    /// fixture keys — standing in for a plugin cache that is (up to 24h) behind mako's own key
    /// rotation.</summary>
    private static readonly string StaleNotesConfigJson =
        """
        {
          "notes": {
            "note1_n": "STALEKEY-PLAYLIST1-N-24b",
            "note1_o": "STALEKEY-PLAYLIST1-O-24b",
            "note3": "TESTKEY-TOKEN-TICKET-24b"
          }
        }
        """;

    [Fact]
    public async Task AnUnprotectedEpisodeNeedsNoToken()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: false));
        http.Respond(MediaUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(MediaUrl, link.Url);
        Assert.Null(http.LastPostBody);
    }

    [Fact]
    public async Task AProtectedEpisodeGetsItsTicketAppended()
    {
        string finalUrl = $"{MediaUrl}?{DecodedTicket}";
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: true));
        http.Respond(TokenUrl, EncryptedTokenResponse(DecodedTicket));
        http.Respond(finalUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(finalUrl, link.Url);
    }

    [Fact]
    public async Task TheTicketIsAppendedWithAmpersandWhenTheUrlAlreadyHasAQuery()
    {
        const string mediaUrlWithQuery = "https://cdn.example.test/master.m3u8?quality=hd";
        string finalUrl = $"{mediaUrlWithQuery}&{DecodedTicket}";
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(mediaUrlWithQuery, isProtected: true));
        http.Respond(TokenUrl, EncryptedTokenResponse(DecodedTicket));
        http.Respond(finalUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(finalUrl, link.Url);
    }

    [Fact]
    public async Task TheTokenRequestCarriesTheAppsFields()
    {
        const string cdn = "akamai-1";
        string finalUrl = $"{MediaUrl}?{DecodedTicket}";
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, cdn: cdn, isProtected: true));
        http.Respond(TokenUrl, EncryptedTokenResponse(DecodedTicket));
        http.Respond(finalUrl, MasterPlaylistText);
        DeviceIdentity device = CreateDevice();

        await CreateResolver(http, device).ResolveAsync(Episode, CancellationToken.None);

        JsonElement body = JsonDocument.Parse(PayloadCipher.Decrypt(TokenKey, http.LastPostBody!)).RootElement;
        List<string> propertyNames = [.. body.EnumerateObject().Select(property => property.Name)];
        Assert.Equal(["da", "du", "et", "na", "rv", "lp", "dv"], propertyNames);
        Assert.Equal(ParsedConfig.AppId, body.GetProperty("da").GetString());
        Assert.Equal(device.Get(), body.GetProperty("du").GetString());
        Assert.Equal("ngt", body.GetProperty("et").GetString());
        Assert.Equal(AppVersion, body.GetProperty("na").GetString());
        Assert.Equal(cdn, body.GetProperty("rv").GetString());
        Assert.Equal(MediaUrl, body.GetProperty("lp").GetString());
        Assert.Equal(Episode.VcmId, body.GetProperty("dv").GetString());
    }

    [Fact]
    public async Task TheCdnWithTheHighestWeightIsChosen()
    {
        const string lowWeightUrl = "https://cdn.example.test/low.m3u8";
        const string highWeightUrl = "https://cdn.example.test/high.m3u8";
        string playlistJson =
            $$"""
            {
              "media": [
                { "url": "{{lowWeightUrl}}", "cdn": "low", "cdnLB": "20" },
                { "url": "{{highWeightUrl}}", "cdn": "high", "cdnLB": "80" }
              ],
              "videoDetails": { "extraParams": { "protectedContent": false } }
            }
            """;
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, playlistJson));
        http.Respond(highWeightUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(highWeightUrl, link.Url);
    }

    [Fact]
    public async Task ThePlaylistEncryptedWithTheOldKeyStillDecrypts()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(OldPlaylistKey, PlaylistJson(MediaUrl, isProtected: false)));
        http.Respond(MediaUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(MediaUrl, link.Url);
    }

    [Fact]
    public async Task ARefusedEntitlementFailsWithTheCaseId()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: true));
        http.Respond(TokenUrl, PayloadCipher.Encrypt(TokenKey, """{"caseId":"4","tickets":[]}"""));

        EntitlementDeniedException exception = await Assert.ThrowsAsync<EntitlementDeniedException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal("4", exception.CaseId);
    }

    [Fact]
    public async Task AStalePlaylistKeyTriggersOneConfigRefetchThenSucceeds()
    {
        SequencedResponses http = new();
        EnqueueConfig(http, notesConfigJson: StaleNotesConfigJson);
        EnqueueConfig(http);
        http.Enqueue(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: false));
        http.Enqueue(MediaUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(MediaUrl, link.Url);
        Assert.Equal(1, http.RequestCountFor(PlaylistUrl));
        Assert.Equal(2, http.RequestCountFor(NotesConfigUrl));
    }

    [Fact]
    public async Task ASecondDecryptFailurePropagatesWithoutLooping()
    {
        SequencedResponses http = new();
        EnqueueConfig(http, notesConfigJson: StaleNotesConfigJson);
        EnqueueConfig(http, notesConfigJson: StaleNotesConfigJson);
        http.Enqueue(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: false));

        await Assert.ThrowsAsync<PayloadDecryptException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal(1, http.RequestCountFor(PlaylistUrl));
        Assert.Equal(2, http.RequestCountFor(NotesConfigUrl));
        Assert.Equal(2, http.RequestCountFor(MainConfigUrl));
    }

    [Fact]
    public async Task AnUnreadableMasterPlaylistFallsBackToTheDefaultDescription()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: false));
        http.Fail(MediaUrl);
        RecordingLogger<StreamResolver> logger = new();

        StreamLink link = await CreateResolver(http, logger: logger).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(StreamDescription.MakoDefault, link.Description);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task TheStreamIsDescribedFromTheMasterPlaylist()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: false));
        http.Respond(MediaUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(MasterPlaylist.Describe(MasterPlaylistText), link.Description);
    }

    [Fact]
    public async Task NoLogLineEverContainsTheTicketQueryString()
    {
        string finalUrl = $"{MediaUrl}?{DecodedTicket}";
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: true));
        http.Respond(TokenUrl, EncryptedTokenResponse(DecodedTicket));
        http.Respond(finalUrl, MasterPlaylistText);
        RecordingLogger<StreamResolver> logger = new();

        await CreateResolver(http, logger: logger).ResolveAsync(Episode, CancellationToken.None);

        Assert.DoesNotContain(logger.Lines, line => line.Contains(DecodedTicket, StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Lines, line => line.Contains("hmac=ab", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EntriesWithoutAUsableUrlAreIgnoredWhenChoosingMedia()
    {
        string playlistJson =
            $$"""
            {
              "media": [ { "cdnLB": "90" }, { "url": "{{MediaUrl}}", "cdn": "AWS", "cdnLB": "10" } ],
              "videoDetails": { "extraParams": { "protectedContent": false } }
            }
            """;
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, playlistJson));
        http.Respond(MediaUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(MediaUrl, link.Url);
    }

    [Fact]
    public async Task NonObjectMediaEntriesFailWithTheSameExceptionAsAnEmptyArray()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, """{ "media": ["x", 42, null] }"""));

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal("playlist has no media", exception.Message);
    }

    [Fact]
    public async Task AMediaPropertyThatIsNotAnArrayFailsTheSameWay()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, """{ "media": "not-an-array" }"""));

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal("playlist has no media", exception.Message);
    }

    [Fact]
    public async Task AMissingMediaPropertyFailsTheSameWay()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, """{ "videoDetails": {} }"""));

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal("playlist has no media", exception.Message);
    }

    [Fact]
    public async Task ATieInCdnWeightKeepsTheFirstEntry()
    {
        const string firstUrl = "https://cdn.example.test/first.m3u8";
        const string secondUrl = "https://cdn.example.test/second.m3u8";
        string playlistJson =
            $$"""
            {
              "media": [
                { "url": "{{firstUrl}}", "cdn": "first", "cdnLB": "50" },
                { "url": "{{secondUrl}}", "cdn": "second", "cdnLB": "50" }
              ],
              "videoDetails": { "extraParams": { "protectedContent": false } }
            }
            """;
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, playlistJson));
        http.Respond(firstUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(firstUrl, link.Url);
    }

    [Fact]
    public async Task AnUnparseableCdnWeightCountsAsZero()
    {
        const string garbageWeightUrl = "https://cdn.example.test/garbage-weight.m3u8";
        const string realWeightUrl = "https://cdn.example.test/real-weight.m3u8";
        string playlistJson =
            $$"""
            {
              "media": [
                { "url": "{{garbageWeightUrl}}", "cdn": "g", "cdnLB": "abc" },
                { "url": "{{realWeightUrl}}", "cdn": "r", "cdnLB": "1" }
              ],
              "videoDetails": { "extraParams": { "protectedContent": false } }
            }
            """;
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, playlistJson));
        http.Respond(realWeightUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(realWeightUrl, link.Url);
    }

    [Fact]
    public async Task NoVideoDetailsAtAllIsTreatedAsUnprotected()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, $$"""{ "media": [ { "url": "{{MediaUrl}}", "cdn": "cdn1", "cdnLB": "50" } ] }"""));
        http.Respond(MediaUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(MediaUrl, link.Url);
        Assert.Null(http.LastPostBody);
    }

    [Fact]
    public async Task VideoDetailsWithoutExtraParamsIsTreatedAsUnprotected()
    {
        string playlistJson =
            $$"""
            {
              "media": [ { "url": "{{MediaUrl}}", "cdn": "cdn1", "cdnLB": "50" } ],
              "videoDetails": {}
            }
            """;
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, playlistJson));
        http.Respond(MediaUrl, MasterPlaylistText);

        StreamLink link = await CreateResolver(http).ResolveAsync(Episode, CancellationToken.None);

        Assert.Equal(MediaUrl, link.Url);
        Assert.Null(http.LastPostBody);
    }

    [Fact]
    public async Task ANumericCaseIdIsReadAsText()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: true));
        http.Respond(TokenUrl, PayloadCipher.Encrypt(TokenKey, """{"caseId":4,"tickets":[]}"""));

        EntitlementDeniedException exception = await Assert.ThrowsAsync<EntitlementDeniedException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal("4", exception.CaseId);
    }

    [Fact]
    public async Task AMissingTicketsKeyFailsWithEntitlementDenied()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: true));
        http.Respond(TokenUrl, PayloadCipher.Encrypt(TokenKey, """{"caseId":"9"}"""));

        EntitlementDeniedException exception = await Assert.ThrowsAsync<EntitlementDeniedException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal("9", exception.CaseId);
    }

    [Fact]
    public async Task InvalidPlaylistJsonSurfacesATypedException()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, PayloadCipher.Encrypt(NewPlaylistKey, "not json at all"));

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal("playlist is not valid JSON", exception.Message);
    }

    [Fact]
    public async Task InvalidTokenResponseJsonSurfacesATypedException()
    {
        FakeMakoHttp http = SeededHttp();
        http.Respond(PlaylistUrl, EncryptedPlaylist(MediaUrl, isProtected: true));
        http.Respond(TokenUrl, PayloadCipher.Encrypt(TokenKey, "not json at all"));

        MakoRequestException exception = await Assert.ThrowsAsync<MakoRequestException>(
            () => CreateResolver(http).ResolveAsync(Episode, CancellationToken.None));

        Assert.Equal("token response is not valid JSON", exception.Message);
    }

    private static void EnqueueConfig(SequencedResponses http, string? notesConfigJson = null)
    {
        http.Enqueue(MainConfigUrl, MainConfigJson);
        http.Enqueue(CoreConfigUrl, CoreConfigJson);
        http.Enqueue(NotesConfigUrl, notesConfigJson ?? NotesConfigJson);
    }

    private static StreamResolver CreateResolver(IMakoHttp http, DeviceIdentity? device = null, RecordingLogger<StreamResolver>? logger = null) =>
        new(
            http,
            new MakoConfigLoader(http, new TimedCache(new FakeClock()), () => AppVersion),
            device ?? CreateDevice(),
            () => AppVersion,
            logger ?? new RecordingLogger<StreamResolver>());

    /// <summary>
    /// A directory that does not exist means <see cref="DeviceIdentity"/> can neither read nor
    /// write it, so it always falls back to an in-memory id — keeping these tests free of real
    /// disk side effects; <see cref="DeviceIdentityTests"/> is where the file itself is tested.
    /// </summary>
    private static DeviceIdentity CreateDevice() =>
        new(Path.Combine(Path.GetTempPath(), $"mako-stream-resolver-tests-{Guid.NewGuid():N}-missing-dir", "device.id"));

    private static FakeMakoHttp SeededHttp()
    {
        FakeMakoHttp http = new();
        http.Respond(MainConfigUrl, MainConfigJson);
        http.Respond(CoreConfigUrl, CoreConfigJson);
        http.Respond(NotesConfigUrl, NotesConfigJson);
        return http;
    }

    private static string EncryptedPlaylist(string mediaUrl, string cdn = "cdn1", bool isProtected = false) =>
        PayloadCipher.Encrypt(NewPlaylistKey, PlaylistJson(mediaUrl, cdn, isProtected));

    private static string PlaylistJson(string mediaUrl, string cdn = "cdn1", bool isProtected = false) =>
        $$"""
        {
          "media": [ { "url": "{{mediaUrl}}", "cdn": "{{cdn}}", "cdnLB": "50" } ],
          "videoDetails": { "extraParams": { "protectedContent": {{(isProtected ? "true" : "false")}} } }
        }
        """;

    private static string EncryptedTokenResponse(string decodedTicket, string caseId = "1") =>
        PayloadCipher.Encrypt(TokenKey, TokenResponseJson(decodedTicket, caseId));

    private static string TokenResponseJson(string decodedTicket, string caseId = "1") =>
        $$"""{ "caseId": {{caseId}}, "tickets": [ { "ticket": "{{Uri.EscapeDataString(decodedTicket)}}" } ] }""";

    /// <summary>
    /// Serves a queue of responses per URL, falling back to the last one once exhausted, and
    /// counts requests per URL — lets a test simulate mako rotating a config value between an
    /// initial fetch and a forced refetch, which <see cref="FakeMakoHttp"/> (one fixed response
    /// per URL) cannot express.
    /// </summary>
    private sealed class SequencedResponses : IMakoHttp
    {
        private readonly Dictionary<string, Queue<string>> _responseQueues = [];
        private readonly Dictionary<string, int> _requestCounts = [];

        public void Enqueue(string url, string body)
        {
            if (!_responseQueues.TryGetValue(url, out Queue<string>? queue))
            {
                queue = new Queue<string>();
                _responseQueues[url] = queue;
            }

            queue.Enqueue(body);
        }

        public int RequestCountFor(string url) => _requestCounts.GetValueOrDefault(url);

        public Task<string> GetTextAsync(string url, CancellationToken cancellationToken) => Task.FromResult(Next(url));

        public Task<string> PostTextAsync(string url, string body, string contentType, CancellationToken cancellationToken) =>
            Task.FromResult(Next(url));

        private string Next(string url)
        {
            _requestCounts[url] = _requestCounts.GetValueOrDefault(url) + 1;
            Queue<string> queue = _responseQueues[url];
            return queue.Count > 1 ? queue.Dequeue() : queue.Peek();
        }
    }
}
