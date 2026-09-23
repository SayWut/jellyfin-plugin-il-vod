# Jellyfin mako plugin — Step C (the catalogue by genre) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the one pinned show with every show mako files under one of its five real genres, each show carrying its genres, so the "mako test" Shows library shows one row per genre in the apps.

**Architecture:** The genre menu and each genre's list are ordinary mako pages (`/mako-vod-index?filter=…`), so the existing `MakoPageSource` fetches, caches, coalesces and stale-serves them unchanged — no new source class. A new pure parser `MakoIndex` turns the menu into the real genres and a list into `(path, name, poster)` entries; `MakoCatalog.GetShowsAsync` merges the lists into `Show`s with genres. `FolderId` gets new prefixes and a season id that carries its show's path, so Jellyfin creates every show afresh (with genres) and a season can name its show. `MakoChannel`, `MakoPages`, `StreamResolver` and `Transcoding/` do not change.

**Tech Stack:** C# / .NET 10, Jellyfin.Controller + Jellyfin.Model 12.1.0, xUnit, `System.Text.Json`. No new NuGet packages.

**Spec:** `jellyfin-plugin-mako/docs/spec.md` §6 (step C) — read §6.0 first for what the research settled and §6.2 for the behaviour; §2's N1 table has the new "Open the channel" row. Step B's plan (`docs/plan-step-b.md`) describes the classes this plan builds on.

## Global Constraints

- **N1 — on demand.** Opening the channel costs 1 (menu) + 5 (genre lists) requests to `www.mako.co.il` when the catalogue cache is cold, 0 while fresh; nothing is fetched ahead of a user opening the channel. Task C4's budget test is the definition of done.
- **N2 — cached.** Menu and lists: `CatalogueCacheHours`, default 24 h, persisted with the browse cache. Show page 12 h, past season 7 d, current season 6 h, stream link 5 min memory-only — unchanged.
- **N4 — gentle.** `MakoHttpClient`'s pacing (one in flight, 750 ms gap, 5-minute back-off on a challenge) applies to the burst unchanged.
- **Never answer a folder with an empty list on failure** (spec §5.4/§6.2): a missing list with no stale copy is a thrown `BotChallengeException`/`MakoRequestException`/`FormatException`, which `MakoChannel` already logs and rethrows. A partial catalogue (some genre lists missing) is a failure, never a shorter answer — Jellyfin would delete the missing shows.
- **N6 — clean code.** Invoke the `clean-code` skill at the start of every task: one job per class, intention-revealing names, no magic values (page paths, prefixes, JSON property names each in one named constant), typed errors, tests first and readable as a description of behaviour. `TreatWarningsAsErrors` stays on.
- **Tests never call mako.** Fixtures under `tests/Jellyfin.Plugin.Mako.Tests/Fixtures/`: `genre-menu.json` (the real menu, analytics stripped), `genre-list-comedy.json` (5 real entries incl. `/mako-vod-keshet/shemesh` and `/mako-vod-keshet/Arabic-work`), `genre-list-drama.json` (4 real entries incl. `/mako-vod-keshet/Arabic-work`) — all three committed with this plan; the csproj already copies `Fixtures\**\*`.
- **Do not undo steps A/B** (spec §5.0): Series/Season folder types, the placeholder media source, the real source's shape, the probe-size limiter, `EpisodeShapeRevision`, the no-user refusal, the measured duration.
- Project root `jellyfin-plugin-mako/`; nothing outside it is created or modified. Root namespace `Jellyfin.Plugin.Mako`; everything under `Mako/` uses namespace `Jellyfin.Plugin.Mako.MakoApi`.
- **Progress record:** after every task update `PROGRESS.md` (add a "Step C" table with rows C1…C6 on first use; set the row to `done` with a one-line note; add a deviations row whenever reality differed from this plan) and commit it with the task.
- Git: branch `claude/jellyfin-plugin-mako`. Stage only paths under `jellyfin-plugin-mako/`; never `git add -A`/`.` at the repo root, never `git stash`, `git checkout --`, `git reset --hard`, `git clean`. Commit with exactly:
  `git commit --author="Claude Code <claude@anthropic.com>" -m "<subject>" -m "Co-Authored-By: Claude Code <claude@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_012SRDPq2UvzLWYFM7dW3zr6"`
- Tasks C1–C5 never touch the phone. Only C6 does, with the user's go-ahead.
- Run the suite with `dotnet test` from `jellyfin-plugin-mako/` (280 tests green before C1).

## File Structure (new or changed)

```
src/Jellyfin.Plugin.Mako/
├── Mako/MakoIndex.cs                       C1  menu JSON → genres; list JSON → entries (parsing only)
├── Mako/MakoIndexPaths.cs                  C1  the two page-path constants/rules for the index
├── Browsing/FolderId.cs                    C2  new prefixes; season id = showPath|seasonPath
├── Browsing/ChannelItemMapper.cs           C2  ToFolder(Season) needs the show path → signature change
├── MakoChannel.cs                          C2  DataVersion "5"; season folder → (showPath, seasonPath)
├── Catalog/MakoCatalog.cs                  C3  GetShowsAsync from the genre lists; GetSeasonsAsync any show; GetEpisodesAsync names the show
├── Catalog/CatalogueLifetimes.cs           C3  replaces CacheLifetimes: adds Catalogue
├── Catalog/PinnedShow.cs                   C3  deleted
├── Configuration/PluginConfiguration.cs    C5  PinnedShowPath/Name removed; CatalogueCacheHours added
├── Configuration/configPage.html           C5  pinned inputs removed
└── PluginServiceRegistrator.cs             C5  wiring
tests/Jellyfin.Plugin.Mako.Tests/
├── Fixtures/genre-menu.json, genre-list-comedy.json, genre-list-drama.json   (committed with this plan)
├── Mako/MakoIndexTests.cs                  C1
├── FolderIdTests.cs, ChannelItemMapperTests.cs, MakoChannelTests.cs, Support/FakeCatalog.cs   C2
├── Catalog/MakoCatalogTests.cs, Catalog/OnDemandRequestBudgetTests.cs   C3, C4
└── Configuration/PluginConfigurationTests.cs, PluginServiceRegistratorTests.cs   C5
```

---

### Task 1 (C1): Index parsing — the real genres and a genre's entries

**Files:**
- Create: `src/Jellyfin.Plugin.Mako/Mako/MakoIndex.cs`, `src/Jellyfin.Plugin.Mako/Mako/MakoIndexPaths.cs`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/Mako/MakoIndexTests.cs`

**Interfaces:**
- Consumes: `FixtureFile.ReadText(name)` (tests/Support), the four guarded accessors pattern in `Mako/MakoPages.cs` (`TryGetString`/`TryGetArray`/`TryGetObject` — copy the pattern, do not reuse the private methods).
- Produces:
  - `public sealed record Genre(string Name, string PagePath);`
  - `public sealed record IndexEntry(string PagePath, string Name, string? PosterUrl);`
  - `public static class MakoIndex { public static IReadOnlyList<Genre> ParseGenres(string menuJson); public static IReadOnlyList<IndexEntry> ParseEntries(string listJson); }` — both throw `FormatException("mako index page is not valid JSON")` for unparseable text and `FormatException("mako index page is not a JSON object")` for a non-object root, like `MakoPages`.
  - `public static class MakoIndexPaths { public const string GenreMenu = "/mako-vod-index?filter=menu"; public static bool IsGenreList(string pagePath); }` — `IsGenreList` is true only for a path whose query has `filter=genre`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Jellyfin.Plugin.Mako.MakoApi;
using Jellyfin.Plugin.Mako.Tests.Support;

namespace Jellyfin.Plugin.Mako.Tests.Mako;

public class MakoIndexTests
{
    private static readonly string MenuJson = FixtureFile.ReadText("genre-menu.json");
    private static readonly string ComedyJson = FixtureFile.ReadText("genre-list-comedy.json");

    [Fact]
    public void OnlyTheRealGenresAreKeptFromTheMenu()
    {
        IReadOnlyList<Genre> genres = MakoIndex.ParseGenres(MenuJson);

        Assert.Equal(new[] { "ריאליטי", "דוקומנטרי", "דרמה", "קומדיה", "בישול" }, genres.Select(genre => genre.Name));
        Assert.All(genres, genre => Assert.StartsWith("/mako-vod-index?filter=genre&vcmId=", genre.PagePath, StringComparison.Ordinal));
    }

    [Fact]
    public void ProvidersTheAbcListAndPlaylistsAreNotGenres()
    {
        IReadOnlyList<Genre> genres = MakoIndex.ParseGenres(MenuJson);

        Assert.DoesNotContain(genres, genre => genre.Name is "החדשות" or "פודקאסטים" or "ערוץ 24" or "פלייליסטים" or "כל התכניות");
        Assert.DoesNotContain(genres, genre => genre.PagePath.Contains("ABC_Programs_Slider", StringComparison.Ordinal));
    }

    [Fact]
    public void AGenreListYieldsOneEntryPerShowInMakosOrder()
    {
        IReadOnlyList<IndexEntry> entries = MakoIndex.ParseEntries(ComedyJson);

        Assert.Equal(5, entries.Count);
        Assert.Equal(new IndexEntry("/mako-vod-keshet/shemesh", "שמש", "https://img.mako.co.il/2025/06/09/Shemesh_e.jpg"), entries[0]);
        Assert.Equal("/mako-vod-keshet/Arabic-work", entries[1].PagePath);
    }

    [Fact]
    public void AnEntryWithoutAPathOrTitleIsSkippedAndAMissingPictureIsNull()
    {
        const string list = """{"items":[{"title":"no path"},{"pageUrl":"/x","title":""},{"pageUrl":"/mako-vod-keshet/ok","title":"ok"},"not an object"]}""";

        IReadOnlyList<IndexEntry> entries = MakoIndex.ParseEntries(list);

        Assert.Equal([new IndexEntry("/mako-vod-keshet/ok", "ok", null)], entries);
    }

    [Fact]
    public void AMenuEntryWithoutAUrlOrTitleIsSkipped()
    {
        const string menu = """{"menuItems":[{"title":"x"},{"url":"/mako-vod-index?filter=genre&vcmId=1"},{"title":"דרמה","url":"/mako-vod-index?filter=genre&vcmId=2"}]}""";

        Assert.Equal([new Genre("דרמה", "/mako-vod-index?filter=genre&vcmId=2")], MakoIndex.ParseGenres(menu));
    }

    [Theory]
    [InlineData("<html>Access Denied</html>", "mako index page is not valid JSON")]
    [InlineData("[]", "mako index page is not a JSON object")]
    public void TextThatIsNotAnIndexPageIsAFormatException(string text, string message)
    {
        Assert.Equal(message, Assert.Throws<FormatException>(() => MakoIndex.ParseGenres(text)).Message);
        Assert.Equal(message, Assert.Throws<FormatException>(() => MakoIndex.ParseEntries(text)).Message);
    }

    [Fact]
    public void APageWithoutTheArrayIsEmptyNotAFailure()
    {
        Assert.Empty(MakoIndex.ParseGenres("{}"));
        Assert.Empty(MakoIndex.ParseEntries("{\"items\":\"nope\"}"));
    }

    [Theory]
    [InlineData("/mako-vod-index?filter=genre&vcmId=4f9d", true)]
    [InlineData("/mako-vod-index?filter=provider&vcmId=ee06", false)]
    [InlineData("/mako-vod-index?filter=ABC_Programs_Slider", false)]
    [InlineData("/mako-vod-index", false)]
    [InlineData("/mako-vod-keshet/shemesh?filter=genre", false)]
    public void OnlyAnIndexPathWithTheGenreFilterIsAGenreList(string pagePath, bool expected)
    {
        Assert.Equal(expected, MakoIndexPaths.IsGenreList(pagePath));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MakoIndexTests"`
Expected: compile errors `CS0246`/`CS0103` on `MakoIndex`, `Genre`, `IndexEntry`, `MakoIndexPaths`.

- [ ] **Step 3: Write the implementation**

`Mako/MakoIndexPaths.cs`:

```csharp
namespace Jellyfin.Plugin.Mako.MakoApi;

/// <summary>The page paths of mako's show index (spec §6.0): the genre menu, and the rule that
/// tells a real genre's list from the provider, A–B and playlist entries the menu also carries.</summary>
public static class MakoIndexPaths
{
    public const string GenreMenu = "/mako-vod-index?filter=menu";

    private const string IndexPath = "/mako-vod-index";
    private const string GenreFilter = "filter=genre";

    public static bool IsGenreList(string pagePath)
    {
        int queryStart = pagePath.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || pagePath[..queryStart] != IndexPath)
        {
            return false;
        }

        return pagePath[(queryStart + 1)..].Split('&').Contains(GenreFilter, StringComparer.Ordinal);
    }
}
```

`Mako/MakoIndex.cs`:

```csharp
using System.Text.Json;

namespace Jellyfin.Plugin.Mako.MakoApi;

public sealed record Genre(string Name, string PagePath);

public sealed record IndexEntry(string PagePath, string Name, string? PosterUrl);

/// <summary>
/// Parsing only, for mako's show index: the genre menu (<c>menuItems[]</c>) and a genre's list
/// (<c>items[]</c>, the same shape as the full index). Tolerant like <c>MakoPages</c>: a missing
/// or wrongly-typed property degrades to a skipped entry or null, never a throw; only text that
/// is not a JSON object at all is a <see cref="FormatException"/>.
/// </summary>
public static class MakoIndex
{
    private const string MenuItemsProperty = "menuItems";
    private const string ItemsProperty = "items";
    private const string TitleProperty = "title";
    private const string UrlProperty = "url";
    private const string PageUrlProperty = "pageUrl";
    private const string PictureProperty = "pic";

    private const string NotJsonMessage = "mako index page is not valid JSON";
    private const string NotObjectMessage = "mako index page is not a JSON object";

    public static IReadOnlyList<Genre> ParseGenres(string menuJson)
    {
        using JsonDocument document = ParseObjectOrThrow(menuJson);
        return ObjectsIn(document.RootElement, MenuItemsProperty)
            .Select(TryReadGenre)
            .OfType<Genre>()
            .ToList();
    }

    public static IReadOnlyList<IndexEntry> ParseEntries(string listJson)
    {
        using JsonDocument document = ParseObjectOrThrow(listJson);
        return ObjectsIn(document.RootElement, ItemsProperty)
            .Select(TryReadEntry)
            .OfType<IndexEntry>()
            .ToList();
    }

    private static Genre? TryReadGenre(JsonElement item)
    {
        string? name = TryGetString(item, TitleProperty);
        string? pagePath = TryGetString(item, UrlProperty);
        return !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(pagePath) && MakoIndexPaths.IsGenreList(pagePath)
            ? new Genre(name, pagePath)
            : null;
    }

    private static IndexEntry? TryReadEntry(JsonElement item)
    {
        string? pagePath = TryGetString(item, PageUrlProperty);
        string? name = TryGetString(item, TitleProperty);
        return !string.IsNullOrEmpty(pagePath) && !string.IsNullOrEmpty(name)
            ? new IndexEntry(pagePath, name, TryGetString(item, PictureProperty))
            : null;
    }

    private static JsonDocument ParseObjectOrThrow(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new FormatException(NotJsonMessage);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new FormatException(NotObjectMessage);
        }

        return document;
    }

    private static IEnumerable<JsonElement> ObjectsIn(JsonElement root, string arrayName) =>
        root.TryGetProperty(arrayName, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(element => element.ValueKind == JsonValueKind.Object)
            : [];

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
```

Note the `.ToList()` before `document` is disposed: `JsonElement`s must not escape the `using`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test`
Expected: all green, 280 + 8 new (theories count per row) — no warnings.

- [ ] **Step 5: Update PROGRESS.md and commit**

Add a "## Step C — the catalogue by genre" section with a table (`Task | Title | Driver | Status | Notes`), row C1 `done`.

```bash
git add jellyfin-plugin-mako/src/Jellyfin.Plugin.Mako/Mako/MakoIndex.cs jellyfin-plugin-mako/src/Jellyfin.Plugin.Mako/Mako/MakoIndexPaths.cs jellyfin-plugin-mako/tests/Jellyfin.Plugin.Mako.Tests/Mako/MakoIndexTests.cs jellyfin-plugin-mako/tests/Jellyfin.Plugin.Mako.Tests/Fixtures/genre-menu.json jellyfin-plugin-mako/tests/Jellyfin.Plugin.Mako.Tests/Fixtures/genre-list-comedy.json jellyfin-plugin-mako/tests/Jellyfin.Plugin.Mako.Tests/Fixtures/genre-list-drama.json jellyfin-plugin-mako/PROGRESS.md
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: parse the genre menu and a genre's show list" -m "Co-Authored-By: Claude Code <claude@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_012SRDPq2UvzLWYFM7dW3zr6"
```

---

### Task 2 (C2): Folder ids — new prefixes, and a season id that knows its show

**Files:**
- Modify: `src/Jellyfin.Plugin.Mako/Browsing/FolderId.cs`, `src/Jellyfin.Plugin.Mako/Browsing/ChannelItemMapper.cs` (`ToFolder(Season)`), `src/Jellyfin.Plugin.Mako/MakoChannel.cs` (`ContentVersion`, the Season case of `ItemsInside`), `src/Jellyfin.Plugin.Mako/Catalog/ICatalog.cs`, `src/Jellyfin.Plugin.Mako/Catalog/MakoCatalog.cs` (signature only, behaviour in C3), `tests/.../Support/FakeCatalog.cs`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/FolderIdTests.cs`, `ChannelItemMapperTests.cs`, `MakoChannelTests.cs`

**Interfaces:**
- Produces:
  - `FolderId`: `ShowPrefix = "mako-series"`, `SeasonPrefix = "mako-season"`; `public static FolderId ForSeason(string showId, string seasonId)` (value `"{showId}|{seasonId}"`); `public (string ShowId, string SeasonId) SeasonParts()` — throws `InvalidOperationException` when `Kind != Season`; `Parse` rejects a season value without exactly one `|` with `FormatException`.
  - `ChannelItemMapper.ToFolder(Season season, string showId)`.
  - `ICatalog.GetEpisodesAsync(string showId, string seasonId, CancellationToken)` — the show id is now passed in.
  - `MakoChannel.ContentVersion = "5"`.

Why: Jellyfin creates a channel item only once and never re-copies genres (spec §6.0); a new prefix makes every show a new item. The show path inside the season id is what lets `GetEpisodesAsync` name the show — mako's season page carries no show name.

- [ ] **Step 1: Write the failing tests**

Replace the body of `FolderIdTests.cs`'s existing round-trip/parse tests so they use the new prefixes, and add:

```csharp
[Fact]
public void AShowIdUsesTheNewPrefixSoJellyfinCreatesEveryShowAfreshWithItsGenres()
{
    Assert.Equal("mako-series|/mako-vod-keshet/shemesh", FolderId.ForShow("/mako-vod-keshet/shemesh").ToString());
}

[Fact]
public void ASeasonIdCarriesItsShowsPathAndRoundTrips()
{
    FolderId season = FolderId.ForSeason("/mako-vod-keshet/shemesh", "/mako-vod-keshet/shemesh-s1");

    Assert.Equal("mako-season|/mako-vod-keshet/shemesh|/mako-vod-keshet/shemesh-s1", season.ToString());
    Assert.Equal(season, FolderId.Parse(season.ToString()));
    Assert.Equal(("/mako-vod-keshet/shemesh", "/mako-vod-keshet/shemesh-s1"), season.SeasonParts());
}

[Theory]
[InlineData("mako-season|/only-one-part")]
[InlineData("mako-season|/a|/b|/c")]
[InlineData("series|/mako-vod-keshet/shemesh")]
[InlineData("series-season|/mako-vod-keshet/shemesh-s1")]
public void OldPrefixesAndMalformedSeasonValuesAreNotFolderIds(string raw)
{
    Assert.Throws<FormatException>(() => FolderId.Parse(raw));
}

[Fact]
public void SeasonPartsOfANonSeasonIsAProgrammingError()
{
    Assert.Throws<InvalidOperationException>(() => FolderId.ForShow("/x").SeasonParts());
}
```

In `ChannelItemMapperTests.cs`, change the season-folder test to call `ChannelItemMapper.ToFolder(season, "/mako-vod-keshet/shemesh")` and assert `Id == "mako-season|/mako-vod-keshet/shemesh|" + season.Id`.

In `MakoChannelTests.cs`, replace every `"series|shemesh"` with `"mako-series|shemesh"` and every `"series-season|shemesh-s1"` with `"mako-season|shemesh|shemesh-s1"` (the fake's show id is `shemesh`, its seasons `shemesh-s1`/`shemesh-s2`); `OpeningTheChannelListsTheShowsAsFolders` asserts `"mako-series|shemesh"`; `OpeningAShowListsItsSeasons` asserts `new[] { "mako-season|shemesh|shemesh-s1", "mako-season|shemesh|shemesh-s2" }`. Add:

```csharp
[Fact]
public async Task OpeningASeasonPassesBothItsShowAndItselfToTheCatalogue()
{
    var catalog = new FakeCatalog();

    await Open(ChannelOver(catalog), "mako-season|shemesh|shemesh-s1");

    Assert.Equal(("shemesh", "shemesh-s1"), catalog.LastEpisodesRequest);
}
```

`FakeCatalog`: `GetEpisodesAsync(string showId, string seasonId, …)` records `LastEpisodesRequest = (showId, seasonId)` (a `public (string ShowId, string SeasonId)? LastEpisodesRequest { get; private set; }`) and keeps answering by `seasonId` as today.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test`
Expected: compile errors on `ForSeason(string, string)`, `SeasonParts`, `ToFolder(Season, string)`, `GetEpisodesAsync` arity.

- [ ] **Step 3: Implement**

`FolderId.cs` — replace the prefixes and add the two-part season value:

```csharp
private const char Separator = '|';
// Prefixes changed twice: once when shows became Series items (step A), once in step C when
// every show had to be created afresh so Jellyfin would copy its genres — it copies them
// only when it first creates an item.
private const string ShowPrefix = "mako-series";
private const string SeasonPrefix = "mako-season";
private const int SeasonValueParts = 2;

public static FolderId ForSeason(string showId, string seasonId) => new(FolderKind.Season, $"{showId}{Separator}{seasonId}");

/// <summary>The show path and season path a season id carries; mako's season page names no show,
/// so the id is the only place the show can come from.</summary>
public (string ShowId, string SeasonId) SeasonParts()
{
    if (Kind != FolderKind.Season)
    {
        throw new InvalidOperationException($"A {Kind} folder id has no season parts.");
    }

    string[] parts = Value.Split(Separator);
    return (parts[0], parts[1]);
}
```

In `Parse`, the `SeasonPrefix` arm becomes `SeasonPrefix => ParseSeason(value, raw)` with:

```csharp
private static FolderId ParseSeason(string value, string raw)
{
    string[] parts = value.Split(Separator);
    if (parts.Length != SeasonValueParts || parts.Any(string.IsNullOrEmpty))
    {
        throw new FormatException($"'{raw}' is not a mako folder id.");
    }

    return new FolderId(FolderKind.Season, value);
}
```

`ChannelItemMapper.ToFolder(Season season, string showId)`: `Id = FolderId.ForSeason(showId, season.Id).ToString()`.

`MakoChannel`: `ContentVersion = "5"`; in `ItemsInside`, `case FolderKind.Show:` becomes `seasons.Select(season => ChannelItemMapper.ToFolder(season, folder.Value)).ToList()`; `case FolderKind.Season:` becomes

```csharp
(string showId, string seasonId) = folder.SeasonParts();
IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync(showId, seasonId, cancellationToken).ConfigureAwait(false);
```

`ICatalog`/`MakoCatalog`/`FakeCatalog`: `GetEpisodesAsync(string showId, string seasonId, CancellationToken)`. In `MakoCatalog` for now, ignore `showId` (C3 uses it). Update `MakoCatalogTests`/`OnDemandRequestBudgetTests` call sites to pass `PinnedPagePath` as the show id so they compile — C3 rewrites those tests.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test`
Expected: all green, no warnings.

- [ ] **Step 5: Update PROGRESS.md (row C2, plus a deviations row for the two public signature changes) and commit**

```bash
git add jellyfin-plugin-mako/src jellyfin-plugin-mako/tests jellyfin-plugin-mako/PROGRESS.md
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: new folder id prefixes; a season id carries its show's path" -m "Co-Authored-By: Claude Code <claude@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_012SRDPq2UvzLWYFM7dW3zr6"
```

---

### Task 3 (C3): The catalogue answers from the genre lists

**Files:**
- Create: `src/Jellyfin.Plugin.Mako/Catalog/CatalogueLifetimes.cs`
- Delete: `src/Jellyfin.Plugin.Mako/Catalog/PinnedShow.cs`, `src/Jellyfin.Plugin.Mako/Catalog/CacheLifetimes.cs`
- Modify: `src/Jellyfin.Plugin.Mako/Catalog/MakoCatalog.cs`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/Catalog/MakoCatalogTests.cs` (rewrite the show/season/name tests; keep the stream-link, HTML-page and persisted-cache tests)

**Interfaces:**
- Consumes: `MakoIndex.ParseGenres/ParseEntries`, `MakoIndexPaths.GenreMenu`, `MakoPageSource.GetAsync(pagePath, lifetimeOf, ct)` / `TryGetCached`, `MakoPages.ParseShowPage/ParseEpisodes/IsLatestSeason` (unchanged).
- Produces:
  - `public sealed record CatalogueLifetimes(TimeSpan Catalogue, TimeSpan ShowPage, TimeSpan PastSeason, TimeSpan CurrentSeason, TimeSpan StreamLink)` with `Default` = 24 h / 12 h / 7 d / 6 h / 5 min.
  - `public MakoCatalog(MakoPageSource pages, StreamResolver resolver, TimedCache streamLinks, Func<CatalogueLifetimes> readLifetimes)` — four arguments; no pinned show.
  - `GetShowsAsync`: menu → real genres → each genre's list (in menu order) → one `Show` per distinct `PagePath`, `Name`/`PosterUrl` from the first list naming it, `Genres` = every genre naming it in menu order, `Overview` = null; shows ordered by first appearance.
  - `GetSeasonsAsync(showId)`: no pinned check — any page path `MakoUrls.Page` accepts is fetched (an `ArgumentException` for a bad one is already an empty view in `MakoChannel`).
  - `GetEpisodesAsync(showId, seasonId)`: the show's name comes from the cached genre lists via a private `TryFindShowName(showId)` that only peeks (`TryGetCached` on the menu and each cached genre list) — never fetches; unknown → `string.Empty` as the episodes' `ShowName`.

- [ ] **Step 1: Write the failing tests**

In `MakoCatalogTests.cs`: remove `PinnedPagePath`/`PinnedShowName`/`Pinned`; add

```csharp
private const string ShemeshPagePath = "/mako-vod-keshet/shemesh";
private const string ArabicWorkPagePath = "/mako-vod-keshet/Arabic-work";
private static readonly string MenuJson = FixtureFile.ReadText("genre-menu.json");
private static readonly string ComedyJson = FixtureFile.ReadText("genre-list-comedy.json");
private static readonly string DramaJson = FixtureFile.ReadText("genre-list-drama.json");
private static readonly string EmptyListJson = """{"items":[]}""";
```

`SeededHttp()` additionally responds to the menu URL and the five genre URLs (`MakoUrls.Page(BaseUrlTemplate, genre.PagePath)` for each `MakoIndex.ParseGenres(MenuJson)`): comedy → `ComedyJson`, drama → `DramaJson`, the other three → `EmptyListJson`. `CreateCatalogAsync` builds `new MakoCatalog(pages, resolver, streamLinks, () => CatalogueLifetimes.Default)`. The show page fixture stays registered under `ShemeshPagePath`'s URL.

Tests:

```csharp
[Fact]
public async Task TheChannelListsEveryShowOfEveryRealGenreOnceWithItsGenres()
{
    FakeMakoHttp http = SeededHttp();
    MakoCatalog catalog = await CreateCatalogAsync(http);

    IReadOnlyList<Show> shows = await catalog.GetShowsAsync(CancellationToken.None);

    Assert.Equal(8, shows.Count); // 5 comedy + 4 drama − 1 shared
    Show shemesh = Assert.Single(shows, show => show.Id == ShemeshPagePath);
    Assert.Equal(new Show(ShemeshPagePath, "שמש", "https://img.mako.co.il/2025/06/09/Shemesh_e.jpg", Overview: null, Genres: ["קומדיה"]), shemesh);
    Show shared = Assert.Single(shows, show => show.Id == ArabicWorkPagePath);
    Assert.Equal(new[] { "דרמה", "קומדיה" }, shared.Genres); // menu order: drama comes before comedy
}

[Fact]
public async Task OpeningTheChannelCostsTheMenuAndOneRequestPerRealGenreAndThenNothing()
{
    FakeMakoHttp http = SeededHttp();
    MakoCatalog catalog = await CreateCatalogAsync(http);

    await catalog.GetShowsAsync(CancellationToken.None);
    Assert.Equal(6, http.Requests.Count(url => url.Contains(MakoUrls.BrowseHost, StringComparison.Ordinal)));
    Assert.DoesNotContain(http.Requests, url => url.Contains("filter=provider", StringComparison.Ordinal) || url.Contains("ABC_Programs", StringComparison.Ordinal) || url.Contains("filter=playlists", StringComparison.Ordinal));

    await catalog.GetShowsAsync(CancellationToken.None);
    Assert.Equal(6, http.Requests.Count(url => url.Contains(MakoUrls.BrowseHost, StringComparison.Ordinal)));
}

[Fact]
public async Task AGenreListThatFailsFailsTheWholeAnswerRatherThanShorteningIt()
{
    FakeMakoHttp http = SeededHttp();
    http.Challenge(MakoUrls.Page(BaseUrlTemplate, MakoIndex.ParseGenres(MenuJson)[2].PagePath)); // דרמה
    MakoCatalog catalog = await CreateCatalogAsync(http);

    await Assert.ThrowsAsync<BotChallengeException>(() => catalog.GetShowsAsync(CancellationToken.None));
}

[Fact]
public async Task AStaleCatalogueIsServedWhenMakoRefusesTheRefetch()
{
    FakeClock clock = new();
    FakeMakoHttp http = SeededHttp();
    MakoCatalog catalog = await CreateCatalogAsync(http, clock); // add this overload: it builds both TimedCaches over the given FakeClock
    IReadOnlyList<Show> first = await catalog.GetShowsAsync(CancellationToken.None);

    clock.Advance(CatalogueLifetimes.Default.Catalogue + TimeSpan.FromMinutes(1));
    foreach (Genre genre in MakoIndex.ParseGenres(MenuJson)) { http.Challenge(MakoUrls.Page(BaseUrlTemplate, genre.PagePath)); }
    http.Challenge(MakoUrls.Page(BaseUrlTemplate, MakoIndexPaths.GenreMenu));

    Assert.Equal(first, await catalog.GetShowsAsync(CancellationToken.None));
}

[Fact]
public async Task AMenuThatIsNotJsonIsNeverCachedAndFailsTheChannel()
{
    FakeMakoHttp http = SeededHttp();
    http.Respond(MakoUrls.Page(BaseUrlTemplate, MakoIndexPaths.GenreMenu), HtmlErrorPage);
    MakoCatalog catalog = await CreateCatalogAsync(http);

    await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
    await Assert.ThrowsAsync<FormatException>(() => catalog.GetShowsAsync(CancellationToken.None));
    Assert.Equal(2, http.Requests.Count(url => url.Contains("filter=menu", StringComparison.Ordinal)));
}

[Fact]
public async Task EpisodesAreNamedAfterTheirShowFromTheCachedListsWithoutARequest()
{
    FakeMakoHttp http = SeededHttp();
    MakoCatalog catalog = await CreateCatalogAsync(http);
    await catalog.GetShowsAsync(CancellationToken.None);
    int requestsBefore = http.Requests.Count;

    IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);

    Assert.All(episodes, episode => Assert.Equal("שמש", episode.ShowName));
    Assert.Equal(requestsBefore + 1, http.Requests.Count); // the season page only
}

[Fact]
public async Task AnUnknownShowStillListsItsEpisodesWithNoSeriesName()
{
    FakeMakoHttp http = SeededHttp();
    MakoCatalog catalog = await CreateCatalogAsync(http);

    IReadOnlyList<Episode> episodes = await catalog.GetEpisodesAsync("/mako-vod-keshet/never-listed", SeasonOnePagePath, CancellationToken.None);

    Assert.NotEmpty(episodes);
    Assert.All(episodes, episode => Assert.Equal(string.Empty, episode.ShowName));
    Assert.DoesNotContain(http.Requests, url => url.Contains("mako-vod-index", StringComparison.Ordinal));
}

[Fact]
public async Task AnyShowsSeasonsCanBeOpenedNotJustAPinnedOne()
{
    FakeMakoHttp http = SeededHttp();
    MakoCatalog catalog = await CreateCatalogAsync(http);

    Assert.NotEmpty(await catalog.GetSeasonsAsync(ShemeshPagePath, CancellationToken.None));
}
```

Delete the tests that asserted the pinned-show behaviour (`…UnknownShow…ReturnsEmpty`, the `"   "` blank-path test, `ShowFromCachedPage…` if it asserted the name-only fallback) — the spec no longer has a pinned show. Keep `NoSignedUrlEverReachesThePersistedBrowseCacheFile`, the two HTML-page tests, and every stream-link test, updating their `GetEpisodesAsync` calls to `(ShemeshPagePath, SeasonOnePagePath, …)`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test`
Expected: compile errors on `CatalogueLifetimes` and the four-argument `MakoCatalog` constructor.

- [ ] **Step 3: Implement**

`Catalog/CatalogueLifetimes.cs`:

```csharp
namespace Jellyfin.Plugin.Mako.Catalog;

public sealed record CatalogueLifetimes(TimeSpan Catalogue, TimeSpan ShowPage, TimeSpan PastSeason, TimeSpan CurrentSeason, TimeSpan StreamLink)
{
    public static CatalogueLifetimes Default { get; } = new(
        Catalogue: TimeSpan.FromHours(24),
        ShowPage: TimeSpan.FromHours(12),
        PastSeason: TimeSpan.FromDays(7),
        CurrentSeason: TimeSpan.FromHours(6),
        StreamLink: TimeSpan.FromMinutes(5));
}
```

`MakoCatalog.cs` — the changed members (the rest stays as step B left it, with `CacheLifetimes` renamed to `CatalogueLifetimes`):

```csharp
private const string UnknownShowName = "";

public MakoCatalog(MakoPageSource pages, StreamResolver resolver, TimedCache streamLinks, Func<CatalogueLifetimes> readLifetimes) { … }

/// <summary>
/// Every show mako files under a real genre (spec §6.2): the genre menu, then each genre's list,
/// merged by page path. The lists are pages like any other, so MakoPageSource caches, coalesces
/// and stale-serves them; a list that fails with no stale copy fails the whole answer, because a
/// shorter answer would make Jellyfin delete the missing shows.
/// </summary>
public async Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken)
{
    TimeSpan lifetime = _readLifetimes().Catalogue;
    IReadOnlyList<Genre> genres = MakoIndex.ParseGenres(await _pages.GetAsync(MakoIndexPaths.GenreMenu, IndexLifetimeOf(lifetime), cancellationToken).ConfigureAwait(false));

    var showsByPath = new Dictionary<string, Show>(StringComparer.Ordinal);
    var order = new List<string>();
    foreach (Genre genre in genres)
    {
        string listJson = await _pages.GetAsync(genre.PagePath, IndexLifetimeOf(lifetime), cancellationToken).ConfigureAwait(false);
        foreach (IndexEntry entry in MakoIndex.ParseEntries(listJson))
        {
            if (showsByPath.TryGetValue(entry.PagePath, out Show? known))
            {
                showsByPath[entry.PagePath] = known with { Genres = [.. known.Genres, genre.Name] };
                continue;
            }

            showsByPath[entry.PagePath] = new Show(entry.PagePath, entry.Name, entry.PosterUrl, Overview: null, Genres: [genre.Name]);
            order.Add(entry.PagePath);
        }
    }

    return order.Select(path => showsByPath[path]).ToList();
}

public async Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken)
{
    CatalogueLifetimes lifetimes = _readLifetimes();
    string showJson = await _pages.GetAsync(showId, ShowPageLifetimeOf(lifetimes), cancellationToken).ConfigureAwait(false);
    ShowPage showPage = MakoPages.ParseShowPage(showJson);
    RememberDisplayedSeason(showPage, showJson, showId, lifetimes);
    return showPage.Seasons;
}

public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(string showId, string seasonId, CancellationToken cancellationToken)
{
    CatalogueLifetimes lifetimes = _readLifetimes();
    string seasonJson = await _pages.GetAsync(seasonId, pageJson => LifetimeOf(pageJson, seasonId, lifetimes), cancellationToken).ConfigureAwait(false);
    return MakoPages.ParseEpisodes(seasonJson, ShowNameOf(showId), SeasonNumberOf(seasonJson, seasonId));
}

/// <summary>The index is parsed before a lifetime is returned, so an HTML error page mako
/// answered with status 200 is never written to the cache (same rule as the show page).</summary>
private static Func<string, TimeSpan> IndexLifetimeOf(TimeSpan lifetime) =>
    indexJson =>
    {
        MakoIndex.ParseEntries(indexJson);
        return lifetime;
    };

/// <summary>Only peeks at the cached menu and lists — a season open must not turn into an index
/// burst. A show no list names (a stale folder id) gets episodes with no series name.</summary>
private string ShowNameOf(string showId)
{
    if (!_pages.TryGetCached(MakoIndexPaths.GenreMenu, out string? menuJson))
    {
        return UnknownShowName;
    }

    foreach (Genre genre in ParseOrEmpty(() => MakoIndex.ParseGenres(menuJson)))
    {
        if (_pages.TryGetCached(genre.PagePath, out string? listJson)
            && ParseOrEmpty(() => MakoIndex.ParseEntries(listJson)).FirstOrDefault(entry => entry.PagePath == showId) is { } entry)
        {
            return entry.Name;
        }
    }

    return UnknownShowName;
}

private static IReadOnlyList<T> ParseOrEmpty<T>(Func<IReadOnlyList<T>> parse)
{
    try
    {
        return parse();
    }
    catch (FormatException)
    {
        return [];
    }
}
```

`IndexLifetimeOf` for the menu parses with `ParseGenres`, not `ParseEntries` — write it as `Func<string, TimeSpan> MenuLifetimeOf(TimeSpan lifetime)` and `ListLifetimeOf(TimeSpan lifetime)`, two one-liners, so each validates the shape it caches. Delete `BuildShow`, `NameOnlyShow`, `IsConfigured`, `PinnedShow.cs`, `CacheLifetimes.cs`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test`
Expected: all green (the `OnDemandRequestBudgetTests` and `PluginServiceRegistrator` may still fail to compile — fix their `MakoCatalog` construction and `GetEpisodesAsync` calls minimally here; C4 rewrites the budget test's assertions).

- [ ] **Step 5: Update PROGRESS.md (row C3; deviations row: `MakoCatalog` constructor is four arguments, `PinnedShow` and `CacheLifetimes` deleted) and commit**

```bash
git add jellyfin-plugin-mako/src jellyfin-plugin-mako/tests jellyfin-plugin-mako/PROGRESS.md
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: the catalogue lists every show of every real genre" -m "Co-Authored-By: Claude Code <claude@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_012SRDPq2UvzLWYFM7dW3zr6"
```

---

### Task 4 (C4): The request budget — N1 for step C

**Files:**
- Test: `tests/Jellyfin.Plugin.Mako.Tests/Catalog/OnDemandRequestBudgetTests.cs`

**Interfaces:**
- Consumes: everything C3 produced; `FakeMakoHttp.Requests`/`Calls`; the existing `RequestCountToHost`/`BrowseHostRequestCount` helpers.

- [ ] **Step 1: Rewrite the budget table test**

Seed the fake as in C3 (menu + five genre lists, comedy/drama fixtures, three empty lists); register the show page under `ShemeshPagePath` and the season page under `SeasonOnePagePath`. The cumulative `www.mako.co.il` counts become:

```csharp
// | Open the channel (cold catalogue)          | 6 (menu + 5 genre lists)                      |
await catalog.GetShowsAsync(CancellationToken.None);
Assert.Equal(6, BrowseHostRequestCount(http));
// | Open the channel again (fresh catalogue)   | 6, unchanged                                  |
await catalog.GetShowsAsync(CancellationToken.None);
Assert.Equal(6, BrowseHostRequestCount(http));
// | Open a show                                | 7 (the show page)                             |
await catalog.GetSeasonsAsync(ShemeshPagePath, CancellationToken.None);
Assert.Equal(7, BrowseHostRequestCount(http));
// | Open the season the show page displayed    | 7, unchanged                                  |
await catalog.GetEpisodesAsync(ShemeshPagePath, DisplayedSeasonPagePath, CancellationToken.None); // the test's existing constant for the season the show page displays (/mako-vod-keshet/shemesh-s6)
Assert.Equal(7, BrowseHostRequestCount(http));
// | Open another season                        | 8                                             |
await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);
Assert.Equal(8, BrowseHostRequestCount(http));
// | Press play (protected)                     | 9 on www + 1 POST token + 2 CDN GETs          |
```

…and the existing play-step assertions continue from 9. Keep `NothingIsEverRequestedForSeasonsNobodyOpened` (its positive control included) and add:

```csharp
[Fact]
public async Task NothingIsEverRequestedForShowsNobodyOpened()
{
    FakeMakoHttp http = SeededHttp();
    MakoCatalog catalog = await CreateCatalogAsync(http);

    await catalog.GetShowsAsync(CancellationToken.None);

    Assert.DoesNotContain(http.Requests, url => url.Contains("/mako-vod-keshet/", StringComparison.Ordinal));
}

[Fact]
public async Task ASeasonOpenNeverTurnsIntoAnIndexBurst()
{
    FakeMakoHttp http = SeededHttp();
    MakoCatalog catalog = await CreateCatalogAsync(http);

    await catalog.GetEpisodesAsync(ShemeshPagePath, SeasonOnePagePath, CancellationToken.None);

    Assert.DoesNotContain(http.Requests, url => url.Contains("mako-vod-index", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run the budget test — it pins C3's behaviour, so it should pass immediately; if any count is off, the catalogue is wrong: fix C3, never the numbers**

Run: `dotnet test --filter "FullyQualifiedName~OnDemandRequestBudgetTests"`
Expected: PASS. (RED for this task was C3's compile failure; this task pins the budget.)

- [ ] **Step 3: Update PROGRESS.md (row C4) and commit**

```bash
git add jellyfin-plugin-mako/tests/Jellyfin.Plugin.Mako.Tests/Catalog/OnDemandRequestBudgetTests.cs jellyfin-plugin-mako/PROGRESS.md
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: pin step C's request budget" -m "Co-Authored-By: Claude Code <claude@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_012SRDPq2UvzLWYFM7dW3zr6"
```

---

### Task 5 (C5): Settings and wiring

**Files:**
- Modify: `src/Jellyfin.Plugin.Mako/Configuration/PluginConfiguration.cs`, `src/Jellyfin.Plugin.Mako/Configuration/configPage.html`, `src/Jellyfin.Plugin.Mako/PluginServiceRegistrator.cs`, `README.md`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/Configuration/PluginConfigurationTests.cs`, `tests/Jellyfin.Plugin.Mako.Tests/PluginServiceRegistratorTests.cs`

**Interfaces:**
- Produces: `PluginConfiguration` without `PinnedShowPath`/`PinnedShowName`, with `public int CatalogueCacheHours { get; set; } = 24;`; `PluginServiceRegistrator.CatalogueLifetimesFrom(PluginConfiguration)` building `CatalogueLifetimes` (private static); `MakoCatalog` registered with the four-argument constructor.

- [ ] **Step 1: Write the failing tests**

`PluginConfigurationTests`: rename the defaults test to `DefaultsMatchTheSpecForTheAppVersionAndLifetimes`, drop the two pinned asserts, add `Assert.Equal(24, configuration.CatalogueCacheHours);`. Add:

```csharp
[Fact]
public void ThePinnedShowSettingsAreGone()
{
    Assert.Null(typeof(PluginConfiguration).GetProperty("PinnedShowPath"));
    Assert.Null(typeof(PluginConfiguration).GetProperty("PinnedShowName"));
}
```

`PluginServiceRegistratorTests` line ~127 asserted the pinned name on the root answer; change that test to assert the resolved `ICatalog` is a `MakoCatalog` and that opening the root with an `IMakoHttp` that is the real `MakoHttpClient` is NOT done (the DI smoke test must not hit the network): keep it to resolution only — `Assert.IsType<MakoCatalog>(provider.GetRequiredService<ICatalog>())`.

`PluginPagesTests` (embedded settings page) — add: `Assert.DoesNotContain("PinnedShow", pageHtml, StringComparison.Ordinal)` and `Assert.Contains("CatalogueCacheHours", pageHtml, StringComparison.Ordinal)`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test`
Expected: the three new assertions fail (properties still present, page still has the inputs).

- [ ] **Step 3: Implement**

`PluginConfiguration.cs`: delete the two pinned constants and properties; add `private const int DefaultCatalogueCacheHours = 24;` and `public int CatalogueCacheHours { get; set; } = DefaultCatalogueCacheHours;`.

`configPage.html`: remove the two pinned `inputContainer` blocks and their two JS lines (load and save); add, before App version:

```html
<div class="inputContainer">
    <label class="inputLabel inputLabelUnfocused" for="CatalogueCacheHours">Catalogue cache (hours)</label>
    <input id="CatalogueCacheHours" name="CatalogueCacheHours" type="number" min="1" is="emby-input" />
    <div class="fieldDescription">
        How long the genre lists are kept before mako is asked again. Opening the channel with a
        cold cache costs six requests, paced.
    </div>
</div>
```

and the matching load line (`document.querySelector('#CatalogueCacheHours').value = config.CatalogueCacheHours;`) and save line (`config.CatalogueCacheHours = parseInt(document.querySelector('#CatalogueCacheHours').value, 10);`) — the page has no other number input to copy from.

`PluginServiceRegistrator.cs`: replace `PinnedShowFrom` with nothing; `CacheLifetimesFrom` becomes

```csharp
private static CatalogueLifetimes CatalogueLifetimesFrom(PluginConfiguration configuration) => new(
    Catalogue: TimeSpan.FromHours(configuration.CatalogueCacheHours),
    ShowPage: TimeSpan.FromHours(configuration.ShowPageCacheHours),
    PastSeason: TimeSpan.FromDays(configuration.PastSeasonCacheDays),
    CurrentSeason: TimeSpan.FromHours(configuration.CurrentSeasonCacheHours),
    StreamLink: TimeSpan.FromMinutes(configuration.StreamLinkCacheMinutes));
```

and the `MakoCatalog` registration passes `() => CatalogueLifetimesFrom(configuration())` as its fourth argument.

`README.md`: replace the "set the pinned show" paragraph with what the channel now lists (every show under mako's five real genres), the new cache setting, and the manual library-tile steps from spec §6.5 (Dashboard → Libraries → Add → Shows → folder = the channel's `Path` from `/Users/{id}/Items/{channelId}` → disable every metadata and image fetcher → save without scanning; then hide the "mako" channel tile in Display settings).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test`
Expected: all green, no warnings. Then `./scripts/build.sh` succeeds.

- [ ] **Step 5: Update PROGRESS.md (row C5) and commit**

```bash
git add jellyfin-plugin-mako/src jellyfin-plugin-mako/tests jellyfin-plugin-mako/README.md jellyfin-plugin-mako/PROGRESS.md
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: settings and wiring for the catalogue by genre; pinned show removed" -m "Co-Authored-By: Claude Code <claude@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_012SRDPq2UvzLWYFM7dW3zr6"
```

---

### Task 6 (C6): Install and run the step C checks (agent + user)

**Files:** none in the repo except `PROGRESS.md`.

Needs the user's go-ahead: restarts Jellyfin, and the first open sends the six-request burst and creates ~430 Series items.

- [ ] **Step 1:** `./scripts/build.sh`, then `SSH_HOST=… SSH_USER=… ./scripts/install.sh` (answer `y`). Confirm `Mako 0.1.0.0: Active`.
- [ ] **Step 2:** As user `test`, `GET /Channels/{channelId}/Items?userId=` once (this is the user-action open; it carries a user id). Read the log: exactly six `mako: request "GET" "www.mako.co.il/mako-vod-index…"` lines, no challenge; then `GET /Genres?parentId=<mako test lib>&includeItemTypes=Series` returns five genres; `GET /Items?parentId=<lib>&includeItemTypes=Series&recursive=true` returns ~430 shows.
- [ ] **Step 3:** Repeat the open: zero new requests to mako.
- [ ] **Step 4:** Watch the log for the poster downloads and the refused self-walks; confirm no `MetadataFetcher`/`ImageFetcher` for mako items runs (spec §6.7 check 5).
- [ ] **Step 5:** User: on web, Android TV and Fladder open the "mako test" tile → Genres view shows five rows; open one show from each genre → seasons → episodes → play. Report.
- [ ] **Step 6:** Record everything in `PROGRESS.md` ("Step C live findings"), set C6 `done`, commit.
