# Jellyfin mako plugin — Step B (live mako, one pinned show) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the static catalogue with live mako calls for one pinned show — real seasons and episodes fetched on demand and cached, and the stream URL resolved (AES playlist + token) at the moment play is pressed.

**Architecture:** `MakoChannel` and everything under `Browsing/` and `Transcoding/` stay as step A left them. A new `MakoCatalog : ICatalog` answers from four small collaborators: `MakoPageSource` (a page's JSON text, cached), `MakoPages` (pure parsing), `StreamResolver` (playlist → token → URL) and `MasterPlaylist` (stream description). `MakoHttpClient` is the only class that touches the network; `TimedCache` is the only class that knows about expiry.

**Tech Stack:** C# / .NET 10, Jellyfin.Controller + Jellyfin.Model 12.1.0, xUnit, `System.Text.Json`, `System.Security.Cryptography.Aes`. No new NuGet packages in the plugin project.

**Spec:** `jellyfin-plugin-mako/docs/spec.md` — read §2 (the needs) and §5 (step B) first; §5.0 lists what step A settled and must not be undone. API notes: `~/Downloads/12+/_analysis/mako_api.md`. The behaviour being ported is `~/Downloads/12+/_analysis/mako12_vod.py` (read it; do not call it).

## Global Constraints

- **N1 — on demand.** A request to mako happens only because a user opened a folder or pressed play. No scheduled task, no background service, no hosted service, no `ISupportsLatestMedia`, no `ISupportsMediaProbe`. Opening a show never fetches all of its seasons. The request budget is spec §2's table; Task B9 has a test that counts requests per user action — it is the definition of done for N1.
- **N2 — cached.** Re-opening a show or season inside the cache lifetime makes no request. Lifetimes: config 24 h; show page 12 h; past season 7 days; current season 6 h; resolved stream link 5 min, memory only, never on disk.
- **N3 — play-time URLs.** A signed URL is never persisted and never logged in full (log the host and path, not the query string).
- **N4 — gentle.** At most one request to `www.mako.co.il` in flight; consecutive requests spaced by at least 750 ms; a redirect to `validate.perfdrive.com` is a `BotChallengeException`, followed by a 5-minute back-off during which no request is sent to `www.mako.co.il`.
- **N5 — honest failure.** Use the playlist's `media` list (not `mediaClean`). An empty `tickets` array fails playback with `EntitlementDeniedException` carrying the `caseId`; never work around it.
- **N6 — clean code.** Invoke the `clean-code` skill at the start of every task: one job per class, intention-revealing names, small functions, no magic values (URLs, IV, header strings, durations each in one named place), typed errors with a reason, tests first and readable as a description of behaviour. `TreatWarningsAsErrors` stays on; no suppressions.
- **Tests never call mako.** Everything runs against `tests/Jellyfin.Plugin.Mako.Tests/Fixtures/` (already committed: `show-page-shemesh.json`, `season-page-shemesh-s1.json`, `master.m3u8`, `main-config.json`, `core-config.json`, `notes-config.json` — the last holds SYNTHETIC 24-byte keys) and fakes. Fixture files are copied to the test output directory (Task B1 adds the csproj item).
- **Do not undo step A** (spec §5.0): Series/Season folder types and the `series|` / `series-season|` id prefixes; the placeholder media source on every episode; the real source's empty `Id`, name `mako`, `AnalyzeDurationMs`; the probe-size limiter; `EpisodeShapeRevision`.
- Project root `jellyfin-plugin-mako/`; nothing outside it is created or modified. Root namespace `Jellyfin.Plugin.Mako`. Target `net10.0`. `dotnet` may be at `/home/ofek/.dotnet/dotnet`.
- **Progress record:** after every task update `PROGRESS.md` (add a "Step B" table with rows B1…B11 on first use; set the row to `done` with a one-line note; add a deviations row whenever reality differed from this plan) and commit it with the task.
- Git: branch `claude/jellyfin-plugin-mako`. The working tree holds unrelated uncommitted changes — stage only paths under `jellyfin-plugin-mako/`; never `git add -A`/`.` at the repo root, never `git stash`, `git checkout --`, `git reset --hard`, `git clean`. Commit with exactly:
  `git commit --author="Claude Code <claude@anthropic.com>" -m "<subject>" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"`
- Tasks B1–B10 never touch the phone: no `scripts/install.sh`, no `scripts/uninstall.sh`, no SSH. Only B11 does, with the user's go-ahead.

## File Structure (new or changed)

```
src/Jellyfin.Plugin.Mako/
├── Cache/IClock.cs, SystemClock.cs, TimedCache.cs, CacheRead.cs          B1
├── Mako/PayloadCipher.cs, PayloadDecryptException.cs                     B2
├── Mako/MakoUrls.cs, MakoConfig.cs                                       B3
├── Mako/IMakoHttp.cs, MakoHttpClient.cs, RequestPacer.cs,
│        BotChallengeException.cs, MakoRequestException.cs                B4
├── Mako/MakoConfigLoader.cs                                              B4
├── Mako/MakoPages.cs, EpisodeKey.cs, ShowPage.cs                         B5
├── Model/StreamDescription.cs; Mako/MasterPlaylist.cs                    B6
├── Mako/StreamResolver.cs, DeviceIdentity.cs, EntitlementDeniedException.cs   B7
├── Mako/MakoPageSource.cs                                                B8
├── Catalog/MakoCatalog.cs                                                B9
├── Configuration/PluginConfiguration.cs, configPage.html                 B10 (changed)
├── PluginServiceRegistrator.cs, Browsing/ChannelItemMapper.cs,
│   Browsing/MediaSourceFactory.cs, Model/Show.cs, Model/Episode.cs,
│   Model/StreamLink.cs                                                   B6/B10 (changed)
└── Catalog/StaticCatalog.cs                                              B10 (deleted)
tests/Jellyfin.Plugin.Mako.Tests/
├── Fixtures/…                                                            (committed with this plan)
├── Support/FixtureFile.cs, FakeClock.cs, FakeMakoHttp.cs                 B1, B4
└── one test file per production class
```

---

### Task B1: Clock and timed cache

**Files:** Create `Cache/IClock.cs`, `Cache/SystemClock.cs`, `Cache/CacheRead.cs`, `Cache/TimedCache.cs`; tests `Cache/TimedCacheTests.cs`, `Support/FakeClock.cs`, `Support/FixtureFile.cs`; modify the test csproj.

**Interfaces — Produces:**
```csharp
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock            // UtcNow => DateTimeOffset.UtcNow
public readonly record struct CacheRead(string? Value, bool IsFresh)
{   public bool HasValue => Value is not null; }     // stale value: HasValue && !IsFresh
public sealed class TimedCache
{
    public TimedCache(IClock clock, string? persistencePath = null);   // null = memory only
    public CacheRead Read(string key);
    public void Write(string key, string value, TimeSpan lifetime);
}
// tests/Support
public sealed class FakeClock : IClock { public DateTimeOffset UtcNow { get; set; } public void Advance(TimeSpan by); }
public static class FixtureFile { public static string ReadText(string name); }   // reads Fixtures/<name> from AppContext.BaseDirectory
```

Behaviour: values are strings (callers cache page text; parsing is cheap). `Read` of an unknown key → `new CacheRead(null, false)`. After the lifetime passes the value is still returned with `IsFresh == false` (the stale copy is what is served during a bot challenge). With a `persistencePath`, every `Write` saves all entries (key, value, expiry) as JSON to that file atomically (write `<path>.tmp`, then `File.Move(..., overwrite: true)`), and the constructor loads the file if it exists; a missing, empty or corrupt file means an empty cache, never an exception. Thread-safe (a private lock).

- [ ] **Step 1: csproj — copy fixtures to the output.** Add to `tests/Jellyfin.Plugin.Mako.Tests/Jellyfin.Plugin.Mako.Tests.csproj`:
```xml
<ItemGroup>
  <None Update="Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```
- [ ] **Step 2: Write the failing tests** (`TimedCacheTests`): `UnknownKeyHasNoValue`; `AValueIsFreshInsideItsLifetime`; `AValueTurnsStaleButStaysReadableAfterItsLifetime` (advance the `FakeClock` past the lifetime → `HasValue` true, `IsFresh` false); `WritingAgainReplacesTheValueAndItsExpiry`; `APersistedCacheSurvivesBeingRecreated` (two `TimedCache` instances over one temp file path; the second reads what the first wrote, with the same freshness); `ACorruptPersistenceFileMeansAnEmptyCache` (write `"{not json"` to the path first); `MemoryOnlyCacheWritesNoFile`. Plus `FixtureFileTests.ReadsACommittedFixture` (`FixtureFile.ReadText("master.m3u8")` starts with `#EXTM3U`).
- [ ] **Step 3:** run `dotnet test`, see them fail to compile. **Step 4:** implement. **Step 5:** `dotnet test` green, 0 warnings. **Step 6:** PROGRESS.md + commit `mako plugin: clock and timed cache with stale reads and file persistence`.

---

### Task B2: Payload cipher

**Files:** Create `Mako/PayloadCipher.cs`, `Mako/PayloadDecryptException.cs`; test `Mako/PayloadCipherTests.cs`.

**Interfaces — Produces:**
```csharp
public sealed class PayloadDecryptException : Exception { public PayloadDecryptException(string message, Exception? inner = null); }
public static class PayloadCipher
{
    public static string Decrypt(string keyText, string base64Ciphertext);  // → UTF-8 plaintext
    public static string Encrypt(string keyText, string plaintext);          // → Base64 (single line is fine)
    public static string DecryptWithAnyKey(IReadOnlyList<string> keyTexts, string base64Ciphertext, string what);
}
```
Exact algorithm (from the app's `Utils.decrypt/encrypt`): AES, CBC, PKCS7; key = the key text's UTF-8 bytes (24 bytes → AES-192); IV = the ASCII bytes of `theExact16Chars=` (a named constant). Input Base64 may contain line breaks and surrounding whitespace (Android `Base64.DEFAULT` wraps at 76 chars) — strip all whitespace before decoding. `Decrypt` throws `PayloadDecryptException` for bad Base64, wrong key/padding, or non-UTF-8 output. `DecryptWithAnyKey` tries keys in order and throws `PayloadDecryptException($"could not decrypt {what} with any of {n} keys")` when all fail.

```csharp
private static byte[] Transform(string keyText, byte[] input, bool encrypt)
{
    using Aes aes = Aes.Create();
    aes.Key = Encoding.UTF8.GetBytes(keyText);
    return encrypt
        ? aes.EncryptCbc(input, InitializationVector, PaddingMode.PKCS7)
        : aes.DecryptCbc(input, InitializationVector, PaddingMode.PKCS7);
}
```

- [ ] **Tests first:** `WhatIsEncryptedDecryptsBackToTheSameText` (Hebrew text, key `TESTKEY-PLAYLIST-NEW-24b`); `DecryptsBase64ThatIsWrappedAcrossLines` (encrypt a 300-char JSON, insert `\n` every 76 chars, decrypt); `AWrongKeyIsReportedAsADecryptFailure`; `GarbageInputIsReportedAsADecryptFailure` (`"not base64!!"`); `FallsBackToTheSecondKeyWhenTheFirstFails`; `ReportsWhatCouldNotBeDecryptedWhenEveryKeyFails` (message contains the `what` argument); `MatchesAKnownVector` — compute the expected Base64 once with Python (`Cryptodome`, same key/IV, plaintext `{"ok":true}`) and paste the literal into the test, so the C# port is pinned to the prototype, not only to itself. Python is available on this machine: `python3 -c "..."` with `from Cryptodome.Cipher import AES`.
- [ ] RED → implement → GREEN → PROGRESS.md + commit `mako plugin: AES-192-CBC payload cipher matching the app's`.

---

### Task B3: URL rules and the config model

**Files:** Create `Mako/MakoUrls.cs`, `Mako/MakoConfig.cs`; tests `Mako/MakoUrlsTests.cs`, `Mako/MakoConfigTests.cs`.

**Interfaces — Produces:**
```csharp
public sealed record MakoConfig(string BaseUrlTemplate, string PlaylistUrlTemplate, string TokenUrl,
                                string AppId, IReadOnlyList<string> PlaylistKeys, string TokenKey)
{
    public static MakoConfig Parse(string mainConfigJson, string coreConfigJson, string notesConfigJson);
    public static string CoreConfigUrlOf(string mainConfigJson);   // sources.ksCoreConfig
    public static string NotesUrlOf(string coreConfigJson);        // sources.notes
}
public static class MakoUrls
{
    public static string MainConfigUrl(string appVersion);   // https://apps.mako.co.il/mobile/config/12Plus/android/mobile/{appVersion}/mainConfig.json
    public static string Page(string baseUrlTemplate, string pagePath);
    public static string Playlist(string playlistUrlTemplate, string vcmId, string channelId, string galleryChannelId);
    public const string BotChallengeHost = "validate.perfdrive.com";
    public const string BrowseHost = "www.mako.co.il";
}
```
`Page` rule, verbatim from the app: replace `%PAGE_URL%` in the template with the path; **if the result contains no `?`, replace the FIRST `&` with `?`**. Examples (these are the tests): template `https://www.mako.co.il%PAGE_URL%&platform=mobileApp` + `/mako-vod-keshet/shemesh` → `https://www.mako.co.il/mako-vod-keshet/shemesh?platform=mobileApp`; + `/mako-vod-index?filter=menu` → `https://www.mako.co.il/mako-vod-index?filter=menu&platform=mobileApp`. `Playlist` replaces `%VCM_ID%`, `%CHANNEL_ID%`, `%GALLERY_ID%`, and `%CONSUMER%` with the constant `mobileApp`. `MakoConfig.Parse`: `BaseUrlTemplate` = main `sources.baseUrl`; `AppId` = main `params.appId`; `PlaylistUrlTemplate`/`TokenUrl` = core `sources.playlist`/`sources.token`; `PlaylistKeys` = notes `notes.note1_n` then `notes.note1_o` (skip a missing one, require at least one); `TokenKey` = `notes.note3`. A missing required field throws `FormatException` naming the field.

- [ ] **Tests first:** the two `Page` examples; `PlaylistUrlFillsEveryPlaceholder` (assert the full expected URL from `core-config.json`'s template); `MainConfigUrlCarriesTheAppVersion`; `ParsesTheThreeFixtureConfigs` (assert every property, keys in order new→old); `ASingleAvailablePlaylistKeyIsEnough`; `AMissingFieldIsNamedInTheError`.
- [ ] RED → implement → GREEN → commit `mako plugin: mako URL rules and the config model`.

---

### Task B4: The HTTP boundary, pacing, and the config loader

**Files:** Create `Mako/IMakoHttp.cs`, `Mako/MakoHttpClient.cs`, `Mako/RequestPacer.cs`, `Mako/BotChallengeException.cs`, `Mako/MakoRequestException.cs`, `Mako/MakoConfigLoader.cs`; tests `Mako/MakoHttpClientTests.cs`, `Mako/RequestPacerTests.cs`, `Mako/MakoConfigLoaderTests.cs`, `Support/FakeMakoHttp.cs`, `Support/StubHttpHandler.cs`.

**Interfaces — Produces:**
```csharp
public interface IMakoHttp
{
    Task<string> GetTextAsync(string url, CancellationToken cancellationToken);
    Task<string> PostTextAsync(string url, string body, string contentType, CancellationToken cancellationToken);
}
public sealed class BotChallengeException : Exception      // (string url)
public sealed class MakoRequestException : Exception        // (string message, Exception? inner = null)
public sealed class RequestPacer                            // (IClock clock, Func<TimeSpan, CancellationToken, Task> delay)
{   public Task<IDisposable> EnterAsync(CancellationToken ct);      // one at a time + MinimumGap since the last exit
    public void BackOff();  public bool IsBackingOff { get; } }     // BackOffDuration = 5 min
public sealed class MakoHttpClient : IMakoHttp              // (HttpClient http, RequestPacer pacer, ILogger<MakoHttpClient> logger)
public sealed class MakoConfigLoader                        // (IMakoHttp http, TimedCache cache, Func<string> readAppVersion)
{   public Task<MakoConfig> LoadAsync(CancellationToken ct);  public void Forget(); }
// tests/Support
public sealed class FakeMakoHttp : IMakoHttp
{   public void Respond(string url, string body);  public void Challenge(string url);  public void Fail(string url);
    public IReadOnlyList<string> Requests { get; }  public string? LastPostBody { get; } }
```

`MakoHttpClient` behaviour: headers on every request — `User-Agent: Mozilla/5.0 (Linux; Android 13; Pixel 6) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Mobile Safari/537.36` and `Accept-Language: he-IL,he;q=0.9,en;q=0.8` (named constants). Requests whose host is `MakoUrls.BrowseHost` go through the `RequestPacer` (one in flight, `MinimumGap` = 750 ms between them) and are refused immediately with `BotChallengeException` while `pacer.IsBackingOff`; requests to other hosts (`apps.mako.co.il`, `mass.mako.co.il`, the CDNs) are not paced. **Challenge detection:** the `HttpClient` is built with `AllowAutoRedirect = false`; a 3xx whose `Location` host contains `validate.perfdrive.com` → `pacer.BackOff()` then `BotChallengeException`; any other 3xx is followed manually up to 5 hops (same detection at each hop). Non-success status → `MakoRequestException("HTTP {code} for {host}{path}")`. `HttpRequestException`/timeout → `MakoRequestException` with the inner exception. Timeout 15 s. **Never log a query string** (N3): log `{Host}{Path}` only, one Information line per request: `mako: request {Method} {Host}{Path}` — this line is what B11's request-count check reads.
No automatic retries: a user action that fails can be repeated by the user, and retries multiply load on a server that is already refusing.

`RequestPacer` takes the delay function as a dependency so tests use a fake that records the requested delay instead of sleeping.

`MakoConfigLoader.LoadAsync`: cache keys `config:main`, `config:core`, `config:notes`, lifetime 24 h; on a fresh hit for all three → `MakoConfig.Parse` with **no request**; otherwise fetch main → core (`CoreConfigUrlOf`) → notes (`NotesUrlOf`), write all three, parse. `Forget()` marks the three entries stale by rewriting them with a zero lifetime (used once by `StreamResolver` after a decrypt failure, B7).

- [ ] **Tests first.**
  `RequestPacerTests`: `TheFirstRequestIsNotDelayed`; `ASecondRequestWaitsOutTheRemainderOfTheGap` (fake clock advanced 200 ms → recorded delay 550 ms); `NoDelayOnceTheGapHasPassed`; `OnlyOneRequestIsInsideAtATime` (second `EnterAsync` does not complete until the first handle is disposed); `BackingOffEndsAfterFiveMinutes`.
  `MakoHttpClientTests` (with `StubHttpHandler : HttpMessageHandler` returning queued responses and recording requests): `SendsTheAppLikeHeaders`; `ARedirectToTheBotChallengeHostIsABotChallengeAndStartsTheBackOff`; `WhileBackingOffBrowseRequestsAreRefusedWithoutTouchingTheNetwork` (handler saw 0 requests); `RequestsToOtherHostsAreNotPacedOrRefused`; `AnOrdinaryRedirectIsFollowed`; `AnErrorStatusIsAMakoRequestExceptionThatNamesHostAndPathButNotTheQuery`; `PostSendsTheBodyWithItsContentType`.
  `MakoConfigLoaderTests` (with `FakeMakoHttp` + fixtures): `LoadsTheThreeFilesInOrderAndParsesThem` (3 requests, in order main/core/notes); `ASecondLoadInsideADayMakesNoRequest`; `AfterForgetTheConfigIsFetchedAgain`.
- [ ] RED → implement → GREEN → commit `mako plugin: the HTTP boundary — app-like headers, pacing, bot-challenge back-off — and the config loader`.

---

### Task B5: Page parsing and the episode key

**Files:** Create `Mako/EpisodeKey.cs`, `Mako/ShowPage.cs`, `Mako/MakoPages.cs`; modify `Model/Show.cs` (no change needed), `Model/Episode.cs` (add `string? ThumbnailUrl` as the LAST parameter; update every existing `new Episode(...)` call site and test); tests `Mako/EpisodeKeyTests.cs`, `Mako/MakoPagesTests.cs`.

**Interfaces — Produces:**
```csharp
public sealed record EpisodeKey(string VcmId, string ChannelId, string GalleryChannelId)
{   public override string ToString();                 // "{VcmId}~{ChannelId}~{GalleryChannelId}"
    public static EpisodeKey Parse(string episodeId);   // FormatException if not exactly 3 non-empty parts
}
public sealed record ShowPage(string? Overview, string? PosterUrl, IReadOnlyList<Season> Seasons, string? CurrentSeasonId);
public static class MakoPages
{
    public static ShowPage ParseShowPage(string pageJson);
    public static IReadOnlyList<Episode> ParseEpisodes(string pageJson, string showName, int seasonNumber);
    public static bool IsCurrentSeason(string pageJson, string seasonPagePath);
}
```
`~` is safe as a separator: mako ids are `[0-9a-zA-Z]` only (e.g. `d13f539206c35910VgnVCM200000650a10acRCRD`). `Season.Id` is the season's `pageUrl` (e.g. `/mako-vod-keshet/shemesh-s1`).

Parsing rules (spec §5.2) — every one is a test against the committed fixtures:
- `ParseShowPage(show-page-shemesh.json)` → 6 seasons numbered 1…6 from `seasonTitle` ("עונה 6" → 6) with names kept verbatim and ids = `pageUrl`; `CurrentSeasonId == "/mako-vod-keshet/shemesh-s6"`; `Overview` starts with "העלילה מתמקדת"; `PosterUrl` = `hero.pics[0].picUrl`.
- A `seasonTitle` with no integer → that season is numbered by its 1-based position. A page with no `seasons` array → one season: number 1, name "עונה 1", id = the page's own `pageUrl`, and it is current.
- `ParseEpisodes(season-page-shemesh-s1.json, "שמש", 1)` → exactly 4 episodes (the fixture's `allEpisodes` tab has 4 real entries + 1 `componentLayout: "ad"` entry, which is skipped; the `shorts` tab is ignored). First: name "שמש בעל הנס", number 1, season 1, air date 1997-12-25 (UTC), thumbnail = its `pics[0].picUrl`, `Duration == null`, id = `EpisodeKey` of its three ids.
- Episode number = the integer after "פרק" in `extraInfo`; when absent, the 1-based position among the kept episodes. Air date = the `dd.MM.yy` after `@`; two-digit years 70–99 → 19xx, else 20xx; unparseable → null.
- An entry missing any of the three ids is skipped (step B does not back-fill ids with an extra request — that would break N1's budget; note it in PROGRESS.md open items if a real show needs it).
- No tab with `id == "allEpisodes"` → fall back to the first tab that has a `vods` array (the prototype's rule).
- `IsCurrentSeason(json, path)` → the `seasons[]` entry whose `pageUrl == path` has `current == true`; no `seasons` array → true.
- Malformed JSON → `FormatException("mako page is not valid JSON")`.
Use `System.Text.Json` (`JsonDocument`); tolerate missing properties everywhere (`TryGetProperty`).

- [ ] **Tests first** (one `[Fact]` per bullet above, plus `EpisodeKeyTests`: round trip; rejects 2 parts; rejects an empty part). RED → implement → GREEN → commit `mako plugin: parse mako's show and season pages; pack playback ids into the episode id`.

---

### Task B6: Stream description from the master playlist

**Files:** Create `Model/StreamDescription.cs`, `Mako/MasterPlaylist.cs`; modify `Model/StreamLink.cs`, `Browsing/MediaSourceFactory.cs`, their tests, and every `new StreamLink(...)` call site; test `Mako/MasterPlaylistTests.cs`.

**Interfaces — Produces:**
```csharp
public sealed record StreamDescription(int? Width, int? Height, int? BitsPerSecond, string VideoProfile, double? VideoLevel)
{   public static StreamDescription MakoDefault { get; } }   // null, null, null, "Main", null — what mako's streams measured as
public sealed record StreamLink(string Url, string Container, StreamDescription Description);
public static class MasterPlaylist
{   public static StreamDescription Describe(string masterPlaylistText); }
```
`Describe`: among `#EXT-X-STREAM-INF` lines pick the highest `BANDWIDTH`; take its `RESOLUTION=WxH` and bandwidth; if it has `CODECS="avc1.PPCCLL,..."` derive profile from `PP` (`42`→"Baseline", `4D`→"Main", `58`→"Extended", `64`→"High"; unknown → "Main") and level from `LL` hex ÷ 10 (`1F`=31→3.1). No `CODECS` → profile "Main", level null. No stream-inf line at all → `StreamDescription.MakoDefault`. Attribute parsing must survive commas inside quoted values.
`MediaSourceFactory.Create(StreamLink link)`: the video `MediaStream` gains `Width`, `Height`, `BitRate`, `Profile`, `Level` from the description (leave a property unset when the value is null), plus `IsInterlaced = false`, `PixelFormat = "yuv420p"`, `BitDepth = 8`, `IsAVC = true`, `NalLengthSize = "0"`; the audio stream gains `SampleRate = 48000`, `Profile = "LC"`. Keep everything step A set (empty `Id`, `Name`, `Protocol`, flags, `AnalyzeDurationMs`). **Verify each `MediaStream` property name against the real 12.1 assembly by compiling; if one does not exist, drop that line and record it — do not guess.** Whether this gets Jellyfin to stream-copy the video is an open question answered live in B11 (check 6), not here.

- [ ] **Tests first:** `PicksTheHighestBandwidthVariant` (fixture → 576×432, 1008884); `ReadsProfileAndLevelFromCodecs` (inline playlist with `CODECS="avc1.64001F,mp4a.40.2"` → "High", 3.1); `FallsBackToMainWhenThereAreNoCodecs` (the fixture); `APlaylistWithNoVariantsGetsTheMakoDefault`; `MediaSourceFactoryTests`: the video stream carries the description; null description values leave the properties unset; all step A assertions still pass.
- [ ] RED → implement → GREEN → commit `mako plugin: describe the stream from mako's master playlist so Jellyfin can copy it`.

---

### Task B7: Stream resolver

**Files:** Create `Mako/DeviceIdentity.cs`, `Mako/EntitlementDeniedException.cs`, `Mako/StreamResolver.cs`; tests `Mako/DeviceIdentityTests.cs`, `Mako/StreamResolverTests.cs`.

**Interfaces — Produces:**
```csharp
public sealed class DeviceIdentity                       // (string filePath)
{   public string Get(); }                               // "A1" + 23 lowercase hex chars; created once, then read back
public sealed class EntitlementDeniedException : Exception { public string? CaseId { get; } }   // (string episodeId, string? caseId)
public sealed class StreamResolver    // (IMakoHttp http, MakoConfigLoader config, DeviceIdentity device, Func<string> readAppVersion, ILogger<StreamResolver> logger)
{   public Task<StreamLink> ResolveAsync(EpisodeKey episode, CancellationToken ct); }
```
Flow (spec §5.2 "Play"):
1. `config.LoadAsync`. 2. GET `MakoUrls.Playlist(...)`; `PayloadCipher.DecryptWithAnyKey(config.PlaylistKeys, body, "playlist")`. **On `PayloadDecryptException`: `config.Forget()`, reload, retry ONCE; a second failure propagates.** 3. Parse: `media[]` → pick the entry with the highest `cdnLB` (`cdnLB` is a JSON string; treat unparseable as 0; ties → first); no media → `MakoRequestException("playlist has no media")`. 4. `videoDetails.extraParams.protectedContent` (absent → false). 5. If protected: body JSON `{"da":appId,"du":deviceId,"et":"ngt","na":appVersion,"rv":media.cdn,"lp":media.url,"dv":vcmId}` — property order as listed, no `dac`; `PayloadCipher.Encrypt(config.TokenKey, json)`; POST to `config.TokenUrl` with content type `application/json; charset=utf-8`; decrypt the response with `TokenKey`; `tickets[0].ticket` URL-decoded (`Uri.UnescapeDataString`) and appended with `&` if the media URL contains `?`, else `?`. Empty/missing `tickets` → `EntitlementDeniedException(episodeId, caseId)` (`caseId` may be a JSON string or number — read either as text). 6. GET the final master URL (a CDN host, not paced) and `MasterPlaylist.Describe` it; **if that fetch fails, log a warning and use `StreamDescription.MakoDefault` — a missing description must not fail playback.** 7. Return `new StreamLink(finalUrl, "hls", description)`.
Logging: one Information line `mako: resolved stream for {VcmId} (protected: {Protected}, cdn: {Cdn})` — never the URL's query.
Request budget per play (N1): 1 playlist + at most 1 token on `mako` hosts, + 1 master playlist on the CDN, + 0–3 config requests only when the 24 h config cache is cold.

- [ ] **Tests first** (FakeMakoHttp; build encrypted fixtures in the test with `PayloadCipher.Encrypt` and the synthetic keys from `notes-config.json`): `AnUnprotectedEpisodeNeedsNoToken` (no POST made; URL = media url); `AProtectedEpisodeGetsItsTicketAppended` (assert final URL = `media.url + "?" + "hdnea=st=1~exp=2~acl=/*~hmac=ab"` when the ticket arrives URL-encoded); `TheTicketIsAppendedWithAmpersandWhenTheUrlAlreadyHasAQuery`; `TheTokenRequestCarriesTheAppsFields` (decrypt `FakeMakoHttp.LastPostBody` with the token key and assert each of the 7 fields and the absence of `dac`); `TheCdnWithTheHighestWeightIsChosen` (`"cdnLB":"20"` vs `"80"`); `ThePlaylistEncryptedWithTheOldKeyStillDecrypts`; `ARefusedEntitlementFailsWithTheCaseId` (`{"caseId":"4","tickets":[]}`); `AStalePlaylistKeyTriggersOneConfigRefetchThenSucceeds`; `AnUnreadableMasterPlaylistFallsBackToTheDefaultDescription`; `TheStreamIsDescribedFromTheMasterPlaylist`. `DeviceIdentityTests`: format `^A1[0-9a-f]{23}$`; the same id is returned by a second instance over the same file.
- [ ] RED → implement → GREEN → commit `mako plugin: resolve a stream at play time — playlist, token when protected, stream description`.

---

### Task B8: Cached page source

**Files:** Create `Mako/MakoPageSource.cs`; test `Mako/MakoPageSourceTests.cs`.

**Interfaces — Produces:**
```csharp
public sealed class MakoPageSource    // (IMakoHttp http, MakoConfigLoader config, TimedCache cache, ILogger<MakoPageSource> logger)
{
    public Task<string> GetAsync(string pagePath, Func<string, TimeSpan> lifetimeOf, CancellationToken ct);
    public void Remember(string pagePath, string pageJson, TimeSpan lifetime);
}
```
`GetAsync`: fresh cache hit → return it, **no request**. Otherwise fetch `MakoUrls.Page(config.BaseUrlTemplate, pagePath)`, store under key `page:{pagePath}` with `lifetimeOf(json)`, return it. **On `BotChallengeException` or `MakoRequestException`: if a stale copy exists, log a warning and return the stale copy; otherwise rethrow.** `Remember` stores a page fetched under another path (the show page doubles as the current season's page).

- [ ] **Tests first:** `TheFirstReadFetchesAndTheSecondDoesNot` (1 request total); `AnExpiredPageIsFetchedAgain`; `TheLifetimeIsDecidedFromThePageItself` (lifetimeOf receives the fetched JSON); `AStaleCopyIsServedWhenMakoChallenges`; `WithNoCopyAtAllTheChallengeSurfaces`; `ARememberedPageIsServedWithoutARequest`.
- [ ] RED → implement → GREEN → commit `mako plugin: cached page source that serves a stale copy when mako refuses`.

---

### Task B9: The live catalogue — and the on-demand proof

**Files:** Create `Catalog/MakoCatalog.cs`; test `Catalog/MakoCatalogTests.cs`, `Catalog/OnDemandRequestBudgetTests.cs`.

**Interfaces — Produces:**
```csharp
public sealed record PinnedShow(string PagePath, string Name);
public sealed record CacheLifetimes(TimeSpan ShowPage, TimeSpan PastSeason, TimeSpan CurrentSeason, TimeSpan StreamLink)
{   public static CacheLifetimes Default { get; } }   // 12 h, 7 d, 6 h, 5 min
public sealed class MakoCatalog : ICatalog   // (MakoPageSource pages, StreamResolver resolver, TimedCache streamLinks /* memory-only */, IClock clock, Func<PinnedShow> readPinnedShow, Func<CacheLifetimes> readLifetimes)
```
Ids: `Show.Id` = the show's page path; `Season.Id` = the season's page path; `Episode.Id` = `EpisodeKey.ToString()`.
- `GetShowsAsync` → exactly one `Show` built from the pinned show: **no request when the show page is not cached** (name from the setting, poster/overview null); when the show page IS cached (fresh or stale) enrich with its overview and poster — read the cache only, never fetch here. (Opening the channel must cost mako nothing in step B.)
- `GetSeasonsAsync(showId)` → `pages.GetAsync(showId, _ => lifetimes.ShowPage)`, `MakoPages.ParseShowPage`; then `pages.Remember(showPage.CurrentSeasonId, json, lifetimes.CurrentSeason)` so the current season costs nothing; return the seasons.
- `GetEpisodesAsync(seasonId)` → `pages.GetAsync(seasonId, json => MakoPages.IsCurrentSeason(json, seasonId) ? lifetimes.CurrentSeason : lifetimes.PastSeason)`; the season number comes from the page itself (its `seasons[]` entry for `seasonId`; fall back to 1); `MakoPages.ParseEpisodes(json, pinned.Name, seasonNumber)`.
- `GetStreamLinkAsync(episodeId)` → memory cache hit inside `lifetimes.StreamLink` → same link, no request; else `resolver.ResolveAsync(EpisodeKey.Parse(episodeId))`, remember, return. (Serialise the `StreamLink` to the cache as JSON; the cache used here is constructed WITHOUT a persistence path — N3.)
A show id other than the pinned show's → empty list (no request).

- [ ] **Tests first.** `MakoCatalogTests`: `TheChannelListsThePinnedShowWithoutAskingMako`; `AfterTheShowWasOpenedItsCardGainsOverviewAndPoster`; `OpeningTheShowListsItsSixSeasons`; `OpeningAPastSeasonListsItsEpisodesWithIdsThatCarryPlaybackKeys`; `AnUnknownShowHasNoSeasonsAndCostsNothing`; `PlayingResolvesOnceAndReusesTheLinkForFiveMinutes` (advance FakeClock 4 min → same link, 0 new requests; 6 min → resolved again).
  **`OnDemandRequestBudgetTests` — this is N1's definition of done.** One `FakeMakoHttp` wired through the REAL `MakoConfigLoader`, `MakoPageSource`, `StreamResolver` and `MakoCatalog`, config cache pre-warmed. Assert `http.Requests` after each step, cumulatively:
  | Step | Requests to `www.mako.co.il` so far |
  |---|---|
  | construct everything; `GetShowsAsync` | 0 |
  | `GetSeasonsAsync(show)` | 1 (the show page) |
  | `GetEpisodesAsync(current season)` | 1 (served from the remembered show page) |
  | `GetEpisodesAsync(season 1)` | 2 |
  | `GetEpisodesAsync(season 1)` again | 2 |
  | `GetSeasonsAsync(show)` again | 2 |
  | `GetStreamLinkAsync(protected episode)` | 3 on `www` (playlist) + 1 POST to `mass.mako.co.il` + 1 GET to the CDN |
  | same episode again within 5 min | unchanged |
  And `NothingIsEverRequestedForSeasonsNobodyOpened`: after opening the show and one season, no request URL contains `shemesh-s2`…`shemesh-s5`.
- [ ] RED → implement → GREEN → commit `mako plugin: live catalogue for the pinned show, with the on-demand request budget pinned by tests`.

---

### Task B10: Settings, wiring, and removing the step A fixture

**Files:** Modify `Configuration/PluginConfiguration.cs`, `Configuration/configPage.html`, `PluginServiceRegistrator.cs`, `Browsing/ChannelItemMapper.cs`, `MakoChannel.cs`, `README.md`, `docs/spec.md` (only if reality differed); delete `Catalog/StaticCatalog.cs` and its tests; update `MakoChannelTests` to run over a small in-test `ICatalog` fake.

- `PluginConfiguration`: remove `TestStreamUrl` and `TestStreamContainer`; add `PinnedShowPath` (default `/mako-vod-keshet/shemesh`), `PinnedShowName` (default `שמש`), `AppVersion` (default `43.0`), and the four lifetimes as whole numbers (`ShowPageCacheHours` 12, `PastSeasonCacheDays` 7, `CurrentSeasonCacheHours` 6, `StreamLinkCacheMinutes` 5). `configPage.html`: inputs for the three text settings (the lifetimes stay config-file-only — YAGNI for a settings page), loaded and saved as today; keep the GUID test passing.
- `ChannelItemMapper.ToPlayable`: `ImageUrl = episode.ThumbnailUrl`. Bump `EpisodeShapeRevision` to `2026-09-23` (the item shape changed: ids and thumbnails) and `MakoChannel`'s content version to `"4"`.
- `PluginServiceRegistrator`: register singletons — `IClock`→`SystemClock`; the persisted `TimedCache` (path: `Path.Combine(Plugin.Instance.DataFolderPath, "mako-cache.json")` — resolve the path lazily inside the factory, never at registration time, because `Plugin.Instance` does not exist yet then); a second memory-only `TimedCache` for stream links (use keyed services or two small wrapper types `BrowseCache`/`StreamLinkCache` so DI can tell them apart — prefer the wrapper types: they are names, not magic keys); `RequestPacer` (delay = `Task.Delay`); `MakoHttpClient` as `IMakoHttp` over an `HttpClient` created with `new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }` and a 15 s timeout; `MakoConfigLoader`; `DeviceIdentity` (file in the data folder); `StreamResolver`; `MakoPageSource`; `MakoCatalog` as `ICatalog` (delegates read `Plugin.Instance.Configuration` at call time). Keep the `IChannel` registration and `TranscodeManagerDecoration.Apply` exactly as they are. **No `IHostedService`, no scheduled task.** Verify `BasePlugin.DataFolderPath` exists in 12.1 by compiling; if it is named differently, use the real one and record it.
- `MakoChannel`: catch `BotChallengeException`/`MakoRequestException` from the catalogue in `GetChannelItems` → log a warning with the reason and return an empty result (the folder shows empty rather than the Channels view returning HTTP 500; Jellyfin 12.1 does not catch). Exceptions from `GetChannelItemMediaInfo` keep propagating (Jellyfin turns them into a clean playback error and logs them).
- Tests: `MakoChannelTests` over a fake `ICatalog`; add `ABotChallengeWhileBrowsingShowsAnEmptyFolderInsteadOfFailing`; add a DI smoke test `TheRegistratorBuildsAWorkingCatalogueGraph` — a `ServiceCollection` with logging + the registrator's registrations (extract them into an `internal static void AddMakoServices(IServiceCollection, Func<string> dataFolder, Func<PluginConfiguration> configuration)` so the test can supply a temp folder and a config without `Plugin.Instance`), then resolve `ICatalog` and `IChannel` and assert they are `MakoCatalog`/`MakoChannel`.
- README: replace "Test a stream (step A)" with how to set the pinned show; PROGRESS.md: step B table complete, step A test settings noted as removed.
- [ ] RED → implement → GREEN (`dotnet test`, 0 warnings) → `./scripts/build.sh` → the artifact folder holds exactly `Jellyfin.Plugin.Mako.dll` + `meta.json` → commit `mako plugin: wire the live catalogue, settings for the pinned show; remove the step A test fixture`.

---

### Task B11: Deploy and run the step B checks (needs the user)

This task restarts Jellyfin on the phone and makes real requests to mako. **Get the user's go-ahead first.**

- [ ] **Step 1:** whole-branch review of B1–B10 (most capable model), one fix wave, one scoped re-review — as in step A.
- [ ] **Step 2:** `SSH_HOST=… SSH_USER=… ./scripts/install.sh` → `Mako 0.1.0.0: Active`; the log shows no ERR since the restart.
- [ ] **Step 3 (agent, via the API as in step A):** open channel → show → a past season → play one episode with a simulated client. From the log (`mako: request …` lines) confirm the request budget of spec §2 exactly; confirm the ffmpeg command has `-probesize 20M`; record whether video is `copy` or `libx264` (check 6) and ffmpeg's RSS.
- [ ] **Step 4 (user, on web / Android TV / Fladder):** spec §5.5 checks 1, 3, 4, 5, 7, 8. Create `docs/step-b-results.md` in the shape of `docs/step-a-results.md` and fill it in.
- [ ] **Step 5:** leave it idle for an hour; confirm from the log that no `mako: request` line appeared (check 2).
- [ ] **Step 6:** commit the results; decide with the user what comes next (the full catalogue, genres, search — each designed separately).
