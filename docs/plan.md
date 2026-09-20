# Jellyfin mako plugin — Step A (static plugin) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and install a Jellyfin 12.1 Channel plugin with two hard-coded shows, and run the step A checks on web, Android TV and Fladder.

**Architecture:** `MakoChannel` is the only class Jellyfin talks to. It turns "folder opened" and "play pressed" into calls on `ICatalog`, and turns the answers into Jellyfin channel items. In step A the catalogue is `StaticCatalog` (fixed data, no network). Step B will put a live mako catalogue behind the same interface without touching `MakoChannel`.

**Tech Stack:** C# / .NET 10, `Jellyfin.Controller` and `Jellyfin.Model` 12.1.0, xUnit, bash for build and install scripts.

**Spec:** `jellyfin-plugin-mako/docs/spec.md` — read it first. This plan covers spec §4 (step A) only. **Step B gets its own plan after the gate in Task 8**, because step A's findings (whether the plugin loads, how the stream must be described, which channel layout the apps support) change what step B must build.

## Global Constraints

- **The need this prototype serves (spec N1): everything is fetched on demand.** The plugin does work only when Jellyfin calls it because a user opened a folder or pressed play. No scheduled task, no background service, no `ISupportsLatestMedia`. Step A makes no network calls at all.
- **Code standard (spec N6):** invoke the `clean-code` skill at the start of every task and hold the code to it: one job per class, names that say what a thing does, small functions, no magic values, typed errors with a reason, tests first. A task is not done until it meets this.
- **Progress record:** after every task, update `PROGRESS.md` — set the task's status, write a note, and add a row to the deviations log whenever reality differed from this plan. Commit it with the task.
- Project root is `jellyfin-plugin-mako/` at the repository root. Nothing under `stack/` is created, modified or referenced by the source code.
- Target: `net10.0`, `Jellyfin.Controller` 12.1.0, `Jellyfin.Model` 12.1.0, plugin `targetAbi` `12.1.0.0`.
- Plugin GUID: `1b35ed7d-7558-4592-aa1a-681b9d67d305`. Plugin name: `Mako`. Version: `0.1.0.0`.
- Root namespace: `Jellyfin.Plugin.Mako`. Do not create a namespace called `Channel` — it collides with Jellyfin's `Channel` class.
- Show and season folders use `ChannelFolderType.Container`. A `Series` folder makes Jellyfin run its metadata providers, which would rename mako's Hebrew titles to TMDb matches.
- Git: work on branch `claude/jellyfin-plugin-mako`. The working tree has unrelated uncommitted changes — **stage only paths under `jellyfin-plugin-mako/`**, never `git add -A` or `git add .` from the repo root. Every commit uses:
  `git commit --author="Claude Code <claude@anthropic.com>" -m "<subject>" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"`
- The phone is a real server in use. Restart Jellyfin only through `scripts/install.sh` / `scripts/uninstall.sh`, which ask first. Touch nothing on the phone outside `$HOME/config/jellyfin/plugins/Mako_0.1.0.0/`.
- All commands below run from `jellyfin-plugin-mako/` unless a step says otherwise.

## File Structure

```
jellyfin-plugin-mako/
├── .gitignore
├── README.md
├── Jellyfin.Plugin.Mako.sln
├── docs/spec.md, plan.md, step-a-results.md
├── src/Jellyfin.Plugin.Mako/
│   ├── Jellyfin.Plugin.Mako.csproj
│   ├── Plugin.cs                                plugin identity + settings page registration
│   ├── PluginServiceRegistrator.cs              wires ICatalog and the channel into Jellyfin's DI
│   ├── MakoChannel.cs                           Jellyfin-facing: folder opened → items; play → media source
│   ├── Configuration/PluginConfiguration.cs     one setting: TestStreamUrl
│   ├── Configuration/configPage.html            the settings page
│   ├── Model/Show.cs, Season.cs, Episode.cs, StreamLink.cs
│   ├── Catalog/ICatalog.cs                      what a catalogue can answer
│   ├── Catalog/StaticCatalog.cs                 step A data
│   ├── Catalog/StreamUnavailableException.cs    typed error: no playable URL
│   └── Browsing/
│       ├── FolderId.cs                          encodes/decodes the folder ids Jellyfin hands back
│       ├── ChannelItemMapper.cs                 Show/Season/Episode → ChannelItemInfo
│       └── MediaSourceFactory.cs                StreamLink → MediaSourceInfo
├── tests/Jellyfin.Plugin.Mako.Tests/
│   ├── Jellyfin.Plugin.Mako.Tests.csproj
│   ├── FolderIdTests.cs, StaticCatalogTests.cs, ChannelItemMapperTests.cs,
│   ├── MediaSourceFactoryTests.cs, MakoChannelTests.cs, PluginPagesTests.cs
└── scripts/build.sh, install.sh, uninstall.sh
```

---

### Task 1: Toolchain and empty solution

**Files:**
- Create: `.gitignore`, `Jellyfin.Plugin.Mako.sln`, `src/Jellyfin.Plugin.Mako/Jellyfin.Plugin.Mako.csproj`, `tests/Jellyfin.Plugin.Mako.Tests/Jellyfin.Plugin.Mako.Tests.csproj`, `tests/Jellyfin.Plugin.Mako.Tests/ToolchainTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a solution where `dotnet test` runs. Later tasks add files to the two projects.

- [ ] **Step 1: Create the branch**

Run from the repository root:
```bash
git switch -c claude/jellyfin-plugin-mako
```
Expected: `Switched to a new branch 'claude/jellyfin-plugin-mako'`. The unrelated modified files stay modified and unstaged — that is correct.

- [ ] **Step 2: Install the .NET 10 SDK beside the existing .NET 8**

**Ask the user before running this** — it downloads and runs Microsoft's installer script.
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
"$HOME/.dotnet/dotnet" --list-sdks
```
Expected: the list shows both `8.0.x` and `10.0.x` under `~/.dotnet/sdk`. If `dotnet` is not on `PATH`, use `"$HOME/.dotnet/dotnet"` wherever this plan says `dotnet`.

- [ ] **Step 3: Create the solution and both projects**

```bash
mkdir -p src tests scripts
dotnet new sln -n Jellyfin.Plugin.Mako
dotnet new classlib -n Jellyfin.Plugin.Mako -o src/Jellyfin.Plugin.Mako -f net10.0
dotnet new xunit   -n Jellyfin.Plugin.Mako.Tests -o tests/Jellyfin.Plugin.Mako.Tests -f net10.0
rm src/Jellyfin.Plugin.Mako/Class1.cs tests/Jellyfin.Plugin.Mako.Tests/UnitTest1.cs
dotnet sln add src/Jellyfin.Plugin.Mako tests/Jellyfin.Plugin.Mako.Tests
dotnet add tests/Jellyfin.Plugin.Mako.Tests reference src/Jellyfin.Plugin.Mako
```
Note: on .NET 10 `dotnet new sln` may create `Jellyfin.Plugin.Mako.slnx` instead of `.sln`. Either is fine; use whichever was created in every later `dotnet` command (they all run from this folder and find it automatically).

- [ ] **Step 4: Replace the plugin project file**

Write `src/Jellyfin.Plugin.Mako/Jellyfin.Plugin.Mako.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Jellyfin.Plugin.Mako</RootNamespace>
    <AssemblyVersion>0.1.0.0</AssemblyVersion>
    <FileVersion>0.1.0.0</FileVersion>
    <Version>0.1.0.0</Version>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Jellyfin.Controller" Version="12.1.0">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
    <PackageReference Include="Jellyfin.Model" Version="12.1.0">
      <ExcludeAssets>runtime</ExcludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <None Remove="Configuration\configPage.html" />
    <EmbeddedResource Include="Configuration\configPage.html" />
  </ItemGroup>

</Project>
```
`ExcludeAssets runtime` keeps Jellyfin's own DLLs out of the plugin folder; the server already has them.

- [ ] **Step 5: Give the test project its own Jellyfin references**

The plugin project hides the Jellyfin packages' runtime assets, so the tests need them directly:
```bash
dotnet add tests/Jellyfin.Plugin.Mako.Tests package Jellyfin.Controller --version 12.1.0
dotnet add tests/Jellyfin.Plugin.Mako.Tests package Jellyfin.Model --version 12.1.0
```

- [ ] **Step 6: Write `.gitignore`**

```
bin/
obj/
artifacts/
*.user
```

- [ ] **Step 7: Write a toolchain test**

`tests/Jellyfin.Plugin.Mako.Tests/ToolchainTests.cs`:
```csharp
using MediaBrowser.Controller.Channels;

namespace Jellyfin.Plugin.Mako.Tests;

public class ToolchainTests
{
    [Fact]
    public void JellyfinChannelInterfaceIsAvailable()
    {
        Assert.True(typeof(IChannel).IsInterface);
    }
}
```

- [ ] **Step 8: Run it**

Run: `dotnet test`
Expected: `Passed!  - Failed: 0, Passed: 1`. If the restore fails on `Jellyfin.Controller 12.1.0`, stop and report — the version floor in Global Constraints is wrong and the user must decide.

- [ ] **Step 9: Commit**

```bash
git add .gitignore *.sln* src tests
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: empty solution that builds against Jellyfin 12.1" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"
```

---

### Task 2: Model and folder ids

**Files:**
- Create: `src/Jellyfin.Plugin.Mako/Model/Show.cs`, `Season.cs`, `Episode.cs`, `StreamLink.cs`, `src/Jellyfin.Plugin.Mako/Browsing/FolderId.cs`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/FolderIdTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `record Show(string Id, string Name, string? PosterUrl, string? Overview, IReadOnlyList<string> Genres)`
  - `record Season(string Id, int Number, string Name)`
  - `record Episode(string Id, string ShowName, int SeasonNumber, int Number, string Name, DateTime? AirDate)`
  - `record StreamLink(string Url, string Container)`
  - `enum FolderKind { Root, Show, Season }`
  - `record FolderId(FolderKind Kind, string Value)` with `FolderId.Root`, `FolderId.ForShow(string)`, `FolderId.ForSeason(string)`, `FolderId.Parse(string?)`, `ToString()`.

Why `FolderId` exists: Jellyfin hands back, as `query.FolderId`, whatever string the plugin gave a folder item as its `Id`. That string is the only thing that tells the plugin which folder was opened. `FolderId` is the one place that format lives.

- [ ] **Step 1: Write the failing tests**

`tests/Jellyfin.Plugin.Mako.Tests/FolderIdTests.cs`:
```csharp
using Jellyfin.Plugin.Mako.Browsing;

namespace Jellyfin.Plugin.Mako.Tests;

public class FolderIdTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingIdMeansTheChannelRoot(string? raw)
    {
        Assert.Equal(FolderId.Root, FolderId.Parse(raw));
    }

    [Fact]
    public void ShowIdSurvivesARoundTrip()
    {
        FolderId original = FolderId.ForShow("/mako-vod-keshet/shemesh");

        FolderId parsed = FolderId.Parse(original.ToString());

        Assert.Equal(FolderKind.Show, parsed.Kind);
        Assert.Equal("/mako-vod-keshet/shemesh", parsed.Value);
    }

    [Fact]
    public void SeasonIdSurvivesARoundTrip()
    {
        FolderId parsed = FolderId.Parse(FolderId.ForSeason("shemesh-s1").ToString());

        Assert.Equal(FolderKind.Season, parsed.Kind);
        Assert.Equal("shemesh-s1", parsed.Value);
    }

    [Fact]
    public void RootSerialisesToAnEmptyString()
    {
        Assert.Equal(string.Empty, FolderId.Root.ToString());
    }

    [Theory]
    [InlineData("shemesh")]
    [InlineData("movie|123")]
    [InlineData("|shemesh")]
    public void UnknownFormatIsRejected(string raw)
    {
        Assert.Throws<FormatException>(() => FolderId.Parse(raw));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test`
Expected: build error — `The type or namespace name 'Browsing' does not exist`.

- [ ] **Step 3: Write the model records**

`src/Jellyfin.Plugin.Mako/Model/Show.cs`:
```csharp
namespace Jellyfin.Plugin.Mako.Model;

public sealed record Show(
    string Id,
    string Name,
    string? PosterUrl,
    string? Overview,
    IReadOnlyList<string> Genres);
```

`src/Jellyfin.Plugin.Mako/Model/Season.cs`:
```csharp
namespace Jellyfin.Plugin.Mako.Model;

public sealed record Season(string Id, int Number, string Name);
```

`src/Jellyfin.Plugin.Mako/Model/Episode.cs`:
```csharp
namespace Jellyfin.Plugin.Mako.Model;

public sealed record Episode(
    string Id,
    string ShowName,
    int SeasonNumber,
    int Number,
    string Name,
    DateTime? AirDate);
```

`src/Jellyfin.Plugin.Mako/Model/StreamLink.cs`:
```csharp
namespace Jellyfin.Plugin.Mako.Model;

public sealed record StreamLink(string Url, string Container);
```

- [ ] **Step 4: Write `FolderId`**

`src/Jellyfin.Plugin.Mako/Browsing/FolderId.cs`:
```csharp
namespace Jellyfin.Plugin.Mako.Browsing;

public enum FolderKind
{
    Root,
    Show,
    Season,
}

/// <summary>
/// The id the plugin gives a folder item, which Jellyfin hands back when a user opens that folder.
/// </summary>
public sealed record FolderId(FolderKind Kind, string Value)
{
    private const char Separator = '|';
    private const string ShowPrefix = "show";
    private const string SeasonPrefix = "season";

    public static FolderId Root { get; } = new(FolderKind.Root, string.Empty);

    public static FolderId ForShow(string showId) => new(FolderKind.Show, showId);

    public static FolderId ForSeason(string seasonId) => new(FolderKind.Season, seasonId);

    public static FolderId Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return Root;
        }

        int separatorAt = raw.IndexOf(Separator, StringComparison.Ordinal);
        if (separatorAt <= 0)
        {
            throw new FormatException($"'{raw}' is not a mako folder id.");
        }

        string prefix = raw[..separatorAt];
        string value = raw[(separatorAt + 1)..];

        return prefix switch
        {
            ShowPrefix => ForShow(value),
            SeasonPrefix => ForSeason(value),
            _ => throw new FormatException($"'{raw}' is not a mako folder id."),
        };
    }

    public override string ToString() => Kind switch
    {
        FolderKind.Show => $"{ShowPrefix}{Separator}{Value}",
        FolderKind.Season => $"{SeasonPrefix}{Separator}{Value}",
        _ => string.Empty,
    };
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test`
Expected: all pass (1 + 8 cases).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: model records and folder ids" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"
```

---

### Task 3: Catalogue interface and the static catalogue

**Files:**
- Create: `src/Jellyfin.Plugin.Mako/Catalog/ICatalog.cs`, `StaticCatalog.cs`, `StreamUnavailableException.cs`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/StaticCatalogTests.cs`

**Interfaces:**
- Consumes: `Show`, `Season`, `Episode`, `StreamLink` from Task 2.
- Produces:
  ```csharp
  public interface ICatalog
  {
      Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken);
      Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken);
      Task<IReadOnlyList<Episode>> GetEpisodesAsync(string seasonId, CancellationToken cancellationToken);
      Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken);
  }
  ```
  - `StaticCatalog(Func<string> readTestStreamUrl)` implements it.
  - `StaticCatalog.PlayableShowId` = `"shemesh"`, `StaticCatalog.CardOnlyShowId` = `"eretz_nehederet"`.
  - `StreamUnavailableException(string message)` — thrown when there is no URL to play.

Content (spec §4.1): two shows with different genres. The first has two seasons with three episodes each. The second is a card with no seasons. Every episode plays the one URL the user pasted into the settings page.

- [ ] **Step 1: Write the failing tests**

`tests/Jellyfin.Plugin.Mako.Tests/StaticCatalogTests.cs`:
```csharp
using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Tests;

public class StaticCatalogTests
{
    private const string PastedUrl = "https://example.test/master.m3u8?hdnea=token";

    private static StaticCatalog CatalogWithPastedUrl(string url = PastedUrl) => new(() => url);

    [Fact]
    public async Task OffersTwoShowsWithDifferentGenres()
    {
        IReadOnlyList<Show> shows = await CatalogWithPastedUrl().GetShowsAsync(CancellationToken.None);

        Assert.Equal(2, shows.Count);
        Assert.NotEqual(shows[0].Genres[0], shows[1].Genres[0]);
        Assert.All(shows, show => Assert.False(string.IsNullOrEmpty(show.PosterUrl)));
    }

    [Fact]
    public async Task PlayableShowHasTwoSeasonsNumberedOneAndTwo()
    {
        IReadOnlyList<Season> seasons = await CatalogWithPastedUrl()
            .GetSeasonsAsync(StaticCatalog.PlayableShowId, CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, seasons.Select(season => season.Number));
    }

    [Fact]
    public async Task CardOnlyShowHasNoSeasons()
    {
        IReadOnlyList<Season> seasons = await CatalogWithPastedUrl()
            .GetSeasonsAsync(StaticCatalog.CardOnlyShowId, CancellationToken.None);

        Assert.Empty(seasons);
    }

    [Fact]
    public async Task EachSeasonListsItsOwnEpisodesInOrder()
    {
        StaticCatalog catalog = CatalogWithPastedUrl();
        IReadOnlyList<Season> seasons = await catalog.GetSeasonsAsync(StaticCatalog.PlayableShowId, CancellationToken.None);

        IReadOnlyList<Episode> secondSeason = await catalog.GetEpisodesAsync(seasons[1].Id, CancellationToken.None);

        Assert.Equal(3, secondSeason.Count);
        Assert.All(secondSeason, episode => Assert.Equal(2, episode.SeasonNumber));
        Assert.Equal(new[] { 1, 2, 3 }, secondSeason.Select(episode => episode.Number));
    }

    [Fact]
    public async Task UnknownSeasonHasNoEpisodes()
    {
        IReadOnlyList<Episode> episodes = await CatalogWithPastedUrl()
            .GetEpisodesAsync("no-such-season", CancellationToken.None);

        Assert.Empty(episodes);
    }

    [Fact]
    public async Task EveryEpisodePlaysThePastedUrlAsHls()
    {
        StreamLink link = await CatalogWithPastedUrl().GetStreamLinkAsync("any-episode", CancellationToken.None);

        Assert.Equal(PastedUrl, link.Url);
        Assert.Equal("hls", link.Container);
    }

    [Fact]
    public async Task UrlIsReadAtPlayTimeNotAtConstruction()
    {
        string current = "https://example.test/first.m3u8";
        var catalog = new StaticCatalog(() => current);

        current = "https://example.test/second.m3u8";
        StreamLink link = await catalog.GetStreamLinkAsync("any-episode", CancellationToken.None);

        Assert.Equal("https://example.test/second.m3u8", link.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PlayingWithNoPastedUrlFailsWithAReason(string emptyUrl)
    {
        StaticCatalog catalog = CatalogWithPastedUrl(emptyUrl);

        StreamUnavailableException error = await Assert.ThrowsAsync<StreamUnavailableException>(
            () => catalog.GetStreamLinkAsync("any-episode", CancellationToken.None));

        Assert.Contains("Test stream URL", error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test`
Expected: build error — `The type or namespace name 'Catalog' does not exist`.

- [ ] **Step 3: Write the interface and the exception**

`src/Jellyfin.Plugin.Mako/Catalog/ICatalog.cs`:
```csharp
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Catalog;

/// <summary>
/// What a content source can answer. Each method is called only because a user opened a folder or pressed play.
/// </summary>
public interface ICatalog
{
    Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Episode>> GetEpisodesAsync(string seasonId, CancellationToken cancellationToken);

    Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken);
}
```

`src/Jellyfin.Plugin.Mako/Catalog/StreamUnavailableException.cs`:
```csharp
namespace Jellyfin.Plugin.Mako.Catalog;

public sealed class StreamUnavailableException : Exception
{
    public StreamUnavailableException(string message)
        : base(message)
    {
    }
}
```

- [ ] **Step 4: Write `StaticCatalog`**

`src/Jellyfin.Plugin.Mako/Catalog/StaticCatalog.cs`:
```csharp
using Jellyfin.Plugin.Mako.Model;

namespace Jellyfin.Plugin.Mako.Catalog;

/// <summary>
/// Step A content: fixed data and no network, so the Jellyfin side can be judged on its own.
/// </summary>
public sealed class StaticCatalog : ICatalog
{
    public const string PlayableShowId = "shemesh";
    public const string CardOnlyShowId = "eretz_nehederet";

    private const string PlayableShowName = "שמש";
    private const string HlsContainer = "hls";
    private const int EpisodesPerSeason = 3;

    private static readonly IReadOnlyList<Show> Shows =
    [
        new Show(
            PlayableShowId,
            PlayableShowName,
            "https://img.mako.co.il/2025/06/09/Shemesh_e.jpg",
            "תוכן בדיקה: שתי עונות קבועות. כל פרק מנגן את כתובת הבדיקה מהגדרות התוסף.",
            ["קומדיה"]),
        new Show(
            CardOnlyShowId,
            "ארץ נהדרת",
            "https://img.mako.co.il/2025/10/21/ERETZ2026_1_e_re.jpg",
            "תוכן בדיקה: כרטיס בלבד, כדי לבדוק סינון לפי ז'אנר.",
            ["סאטירה"]),
    ];

    private static readonly IReadOnlyList<Season> PlayableShowSeasons =
    [
        new Season("shemesh-s1", 1, "עונה 1"),
        new Season("shemesh-s2", 2, "עונה 2"),
    ];

    private readonly Func<string> _readTestStreamUrl;

    public StaticCatalog(Func<string> readTestStreamUrl)
    {
        _readTestStreamUrl = readTestStreamUrl;
    }

    public Task<IReadOnlyList<Show>> GetShowsAsync(CancellationToken cancellationToken)
        => Task.FromResult(Shows);

    public Task<IReadOnlyList<Season>> GetSeasonsAsync(string showId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Season> seasons = showId == PlayableShowId ? PlayableShowSeasons : [];
        return Task.FromResult(seasons);
    }

    public Task<IReadOnlyList<Episode>> GetEpisodesAsync(string seasonId, CancellationToken cancellationToken)
    {
        Season? season = PlayableShowSeasons.FirstOrDefault(candidate => candidate.Id == seasonId);
        IReadOnlyList<Episode> episodes = season is null ? [] : EpisodesOf(season);
        return Task.FromResult(episodes);
    }

    public Task<StreamLink> GetStreamLinkAsync(string episodeId, CancellationToken cancellationToken)
    {
        string url = _readTestStreamUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new StreamUnavailableException(
                "No Test stream URL is set. Paste a freshly signed URL in Dashboard > Plugins > Mako.");
        }

        return Task.FromResult(new StreamLink(url.Trim(), HlsContainer));
    }

    private static IReadOnlyList<Episode> EpisodesOf(Season season)
        => Enumerable.Range(1, EpisodesPerSeason)
            .Select(number => new Episode(
                $"{season.Id}-e{number}",
                PlayableShowName,
                season.Number,
                number,
                $"פרק בדיקה {number}",
                AirDate: null))
            .ToList();
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: catalogue interface and the static step A catalogue" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"
```

---

### Task 4: Mapping to Jellyfin's types

**Files:**
- Create: `src/Jellyfin.Plugin.Mako/Browsing/ChannelItemMapper.cs`, `src/Jellyfin.Plugin.Mako/Browsing/MediaSourceFactory.cs`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/ChannelItemMapperTests.cs`, `MediaSourceFactoryTests.cs`

**Interfaces:**
- Consumes: `Show`, `Season`, `Episode`, `StreamLink`, `FolderId`.
- Produces:
  - `static ChannelItemInfo ChannelItemMapper.ToFolder(Show show)`
  - `static ChannelItemInfo ChannelItemMapper.ToFolder(Season season, Show? show = null)` — `show` is not available in step A; pass nothing.
  - `static ChannelItemInfo ChannelItemMapper.ToPlayable(Episode episode)`
  - `static MediaSourceInfo MediaSourceFactory.Create(string episodeId, StreamLink link)`

Jellyfin types used (all verified in the v12.1 source): `MediaBrowser.Controller.Channels.ChannelItemInfo`, `ChannelItemType { Media, Folder }`, `MediaBrowser.Model.Channels.ChannelFolderType.Container`, `ChannelMediaType.Video`, `ChannelMediaContentType.Episode`, `MediaBrowser.Model.Dto.MediaSourceInfo`, `MediaBrowser.Model.MediaInfo.MediaProtocol.Http`.

- [ ] **Step 1: Write the failing tests**

`tests/Jellyfin.Plugin.Mako.Tests/ChannelItemMapperTests.cs`:
```csharp
using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace Jellyfin.Plugin.Mako.Tests;

public class ChannelItemMapperTests
{
    [Fact]
    public void ShowBecomesAFolderThatCarriesItsOwnMetadata()
    {
        var show = new Show("shemesh", "שמש", "https://img.test/p.jpg", "תיאור", ["קומדיה"]);

        ChannelItemInfo item = ChannelItemMapper.ToFolder(show);

        Assert.Equal(ChannelItemType.Folder, item.Type);
        Assert.Equal(ChannelFolderType.Container, item.FolderType);
        Assert.Equal("show|shemesh", item.Id);
        Assert.Equal("שמש", item.Name);
        Assert.Equal("https://img.test/p.jpg", item.ImageUrl);
        Assert.Equal("תיאור", item.Overview);
        Assert.Equal(new[] { "קומדיה" }, item.Genres);
    }

    [Fact]
    public void SeasonBecomesAFolderNumberedByItsSeason()
    {
        ChannelItemInfo item = ChannelItemMapper.ToFolder(new Season("shemesh-s2", 2, "עונה 2"));

        Assert.Equal(ChannelItemType.Folder, item.Type);
        Assert.Equal(ChannelFolderType.Container, item.FolderType);
        Assert.Equal("season|shemesh-s2", item.Id);
        Assert.Equal("עונה 2", item.Name);
        Assert.Equal(2, item.IndexNumber);
    }

    [Fact]
    public void EpisodeBecomesAPlayableVideoWithSeasonAndEpisodeNumbers()
    {
        var airDate = new DateTime(1997, 12, 25, 0, 0, 0, DateTimeKind.Utc);
        var episode = new Episode("shemesh-s1-e1", "שמש", 1, 1, "שמש בעל הנס", airDate);

        ChannelItemInfo item = ChannelItemMapper.ToPlayable(episode);

        Assert.Equal(ChannelItemType.Media, item.Type);
        Assert.Equal(ChannelMediaType.Video, item.MediaType);
        Assert.Equal(ChannelMediaContentType.Episode, item.ContentType);
        Assert.Equal("shemesh-s1-e1", item.Id);
        Assert.Equal("שמש בעל הנס", item.Name);
        Assert.Equal("שמש", item.SeriesName);
        Assert.Equal(1, item.ParentIndexNumber);
        Assert.Equal(1, item.IndexNumber);
        Assert.Equal(airDate, item.PremiereDate);
    }
}
```

`tests/Jellyfin.Plugin.Mako.Tests/MediaSourceFactoryTests.cs`:
```csharp
using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Mako.Tests;

public class MediaSourceFactoryTests
{
    [Fact]
    public void DescribesARemoteHttpStreamThatCanBePlayedDirectly()
    {
        var link = new StreamLink("https://cdn.test/master.m3u8?hdnea=t", "hls");

        MediaSourceInfo source = MediaSourceFactory.Create("shemesh-s1-e1", link);

        Assert.Equal("shemesh-s1-e1", source.Id);
        Assert.Equal("https://cdn.test/master.m3u8?hdnea=t", source.Path);
        Assert.Equal(MediaProtocol.Http, source.Protocol);
        Assert.Equal("hls", source.Container);
        Assert.True(source.IsRemote);
        Assert.True(source.SupportsDirectPlay);
        Assert.True(source.SupportsDirectStream);
        Assert.False(source.IsInfiniteStream);
        Assert.False(source.RequiresOpening);
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test`
Expected: build error — `'ChannelItemMapper' does not exist`.

- [ ] **Step 3: Write the mapper**

`src/Jellyfin.Plugin.Mako/Browsing/ChannelItemMapper.cs`:
```csharp
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;

namespace Jellyfin.Plugin.Mako.Browsing;

/// <summary>
/// Turns the plugin's own content records into the items Jellyfin shows in a channel.
/// </summary>
public static class ChannelItemMapper
{
    // Container, not Series/Season: a Series folder makes Jellyfin run its metadata
    // providers, which would rename mako's Hebrew titles to TMDb matches.
    private const ChannelFolderType PlainFolder = ChannelFolderType.Container;

    public static ChannelItemInfo ToFolder(Show show) => new()
    {
        Id = FolderId.ForShow(show.Id).ToString(),
        Name = show.Name,
        Type = ChannelItemType.Folder,
        FolderType = PlainFolder,
        ImageUrl = show.PosterUrl,
        Overview = show.Overview,
        Genres = show.Genres.ToList(),
    };

    public static ChannelItemInfo ToFolder(Season season, Show? show = null) => new()
    {
        Id = FolderId.ForSeason(season.Id).ToString(),
        Name = season.Name,
        Type = ChannelItemType.Folder,
        FolderType = PlainFolder,
        IndexNumber = season.Number,
        ImageUrl = show?.PosterUrl,
        SeriesName = show?.Name,
    };

    public static ChannelItemInfo ToPlayable(Episode episode) => new()
    {
        Id = episode.Id,
        Name = episode.Name,
        Type = ChannelItemType.Media,
        MediaType = ChannelMediaType.Video,
        ContentType = ChannelMediaContentType.Episode,
        SeriesName = episode.ShowName,
        ParentIndexNumber = episode.SeasonNumber,
        IndexNumber = episode.Number,
        PremiereDate = episode.AirDate,
    };
}
```

- [ ] **Step 4: Write the media source factory**

`src/Jellyfin.Plugin.Mako/Browsing/MediaSourceFactory.cs`:
```csharp
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Mako.Browsing;

/// <summary>
/// Describes a resolved stream to Jellyfin. These flags decide whether Jellyfin plays
/// the stream directly or transcodes it, so they live in one place.
/// </summary>
public static class MediaSourceFactory
{
    private const string SourceName = "mako";

    public static MediaSourceInfo Create(string episodeId, StreamLink link) => new()
    {
        Id = episodeId,
        Name = SourceName,
        Path = link.Url,
        Protocol = MediaProtocol.Http,
        Container = link.Container,
        IsRemote = true,
        IsInfiniteStream = false,
        SupportsDirectPlay = true,
        SupportsDirectStream = true,
        SupportsTranscoding = true,
        SupportsProbing = true,
        RequiresOpening = false,
    };
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test`
Expected: all pass. If a property named above does not exist on the 12.1 type, the build error names it — remove that one line, note it in the commit message, and carry on; do not guess a replacement.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: map content records to Jellyfin channel items and media sources" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"
```

---

### Task 5: The channel

**Files:**
- Create: `src/Jellyfin.Plugin.Mako/MakoChannel.cs`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/MakoChannelTests.cs`

**Interfaces:**
- Consumes: `ICatalog`, `FolderId`, `ChannelItemMapper`, `MediaSourceFactory`.
- Produces: `MakoChannel(ICatalog catalog, ILogger<MakoChannel> logger)` implementing `IChannel` and `IRequiresMediaInfoCallback`. It deliberately does **not** implement `ISupportsLatestMedia` (spec N1).

`IChannel` members in 12.1 (verified): `Name`, `Description`, `DataVersion`, `HomePageUrl`, `ParentalRating`, `GetChannelFeatures()`, `IsEnabledFor(string userId)`, `GetChannelItems(InternalChannelItemQuery, CancellationToken)`, `GetChannelImage(ImageType, CancellationToken)`, `GetSupportedChannelImages()`. `IRequiresMediaInfoCallback` has `GetChannelItemMediaInfo(string id, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

`tests/Jellyfin.Plugin.Mako.Tests/MakoChannelTests.cs`:
```csharp
using Jellyfin.Plugin.Mako.Catalog;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.Mako.Tests;

public class MakoChannelTests
{
    private const string PastedUrl = "https://example.test/master.m3u8";

    private static MakoChannel ChannelOverStaticCatalog()
        => new(new StaticCatalog(() => PastedUrl), NullLogger<MakoChannel>.Instance);

    private static Task<ChannelItemResult> Open(MakoChannel channel, string? folderId)
        => channel.GetChannelItems(new InternalChannelItemQuery { FolderId = folderId }, CancellationToken.None);

    [Fact]
    public async Task OpeningTheChannelListsTheShowsAsFolders()
    {
        ChannelItemResult result = await Open(ChannelOverStaticCatalog(), folderId: null);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalRecordCount);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Folder, item.Type));
        Assert.Equal("show|shemesh", result.Items[0].Id);
    }

    [Fact]
    public async Task OpeningAShowListsItsSeasons()
    {
        ChannelItemResult result = await Open(ChannelOverStaticCatalog(), "show|shemesh");

        Assert.Equal(new[] { "season|shemesh-s1", "season|shemesh-s2" }, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task OpeningASeasonListsItsPlayableEpisodes()
    {
        ChannelItemResult result = await Open(ChannelOverStaticCatalog(), "season|shemesh-s1");

        Assert.Equal(3, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Media, item.Type));
    }

    [Fact]
    public async Task PressingPlayResolvesTheStreamAtThatMoment()
    {
        IEnumerable<MediaSourceInfo> sources = await ChannelOverStaticCatalog()
            .GetChannelItemMediaInfo("shemesh-s1-e1", CancellationToken.None);

        MediaSourceInfo source = Assert.Single(sources);
        Assert.Equal(PastedUrl, source.Path);
        Assert.Equal("shemesh-s1-e1", source.Id);
    }

    [Fact]
    public void DoesNotAskJellyfinToCrawlItForLatestItems()
    {
        Assert.False(typeof(ISupportsLatestMedia).IsAssignableFrom(typeof(MakoChannel)));
    }
}
```

- [ ] **Step 2: Run to see them fail**

Run: `dotnet test`
Expected: build error — `'MakoChannel' could not be found`.

- [ ] **Step 3: Write the channel**

`src/Jellyfin.Plugin.Mako/MakoChannel.cs`:
```csharp
using Jellyfin.Plugin.Mako.Browsing;
using Jellyfin.Plugin.Mako.Catalog;
using Jellyfin.Plugin.Mako.Model;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Mako;

/// <summary>
/// The mako channel as Jellyfin sees it. Jellyfin calls in only when a user opens a folder
/// or presses play; this class never does work on its own.
/// </summary>
public sealed class MakoChannel : IChannel, IRequiresMediaInfoCallback
{
    private const string ChannelName = "mako";
    private const string ContentVersion = "1";

    private readonly ICatalog _catalog;
    private readonly ILogger<MakoChannel> _logger;

    public MakoChannel(ICatalog catalog, ILogger<MakoChannel> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public string Name => ChannelName;

    public string Description => "mako 12+ VOD, fetched on demand.";

    // Jellyfin discards its cached channel items when this value changes.
    public string DataVersion => ContentVersion;

    public string HomePageUrl => "https://www.mako.co.il/mako-vod";

    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures() => new()
    {
        MediaTypes = [ChannelMediaType.Video],
        ContentTypes = [ChannelMediaContentType.Episode],
    };

    public bool IsEnabledFor(string userId) => true;

    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        FolderId folder = FolderId.Parse(query.FolderId);
        _logger.LogInformation("mako: folder opened: {FolderKind} '{FolderValue}'", folder.Kind, folder.Value);

        List<ChannelItemInfo> items = await ItemsInside(folder, cancellationToken).ConfigureAwait(false);
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("mako: play pressed: '{EpisodeId}'", id);

        StreamLink link = await _catalog.GetStreamLinkAsync(id, cancellationToken).ConfigureAwait(false);
        return [MediaSourceFactory.Create(id, link)];
    }

    private async Task<List<ChannelItemInfo>> ItemsInside(FolderId folder, CancellationToken cancellationToken)
    {
        switch (folder.Kind)
        {
            case FolderKind.Show:
                var seasons = await _catalog.GetSeasonsAsync(folder.Value, cancellationToken).ConfigureAwait(false);
                return seasons.Select(season => ChannelItemMapper.ToFolder(season)).ToList();

            case FolderKind.Season:
                var episodes = await _catalog.GetEpisodesAsync(folder.Value, cancellationToken).ConfigureAwait(false);
                return episodes.Select(ChannelItemMapper.ToPlayable).ToList();

            default:
                var shows = await _catalog.GetShowsAsync(cancellationToken).ConfigureAwait(false);
                return shows.Select(ChannelItemMapper.ToFolder).ToList();
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test`
Expected: all pass. If `Microsoft.Extensions.Logging.Abstractions` is not found in the test project, add it: `dotnet add tests/Jellyfin.Plugin.Mako.Tests package Microsoft.Extensions.Logging.Abstractions`.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: the channel — folders on open, stream on play, nothing on its own" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"
```

---

### Task 6: Plugin identity, the one setting, and wiring

**Files:**
- Create: `src/Jellyfin.Plugin.Mako/Plugin.cs`, `PluginServiceRegistrator.cs`, `Configuration/PluginConfiguration.cs`, `Configuration/configPage.html`, `build.yaml`
- Test: `tests/Jellyfin.Plugin.Mako.Tests/PluginPagesTests.cs`

**Interfaces:**
- Consumes: `StaticCatalog`, `MakoChannel`, `ICatalog`.
- Produces:
  - `Plugin : BasePlugin<PluginConfiguration>, IHasWebPages` with `Plugin.PluginGuid`, `Plugin.Instance`, `Plugin.SettingsPageResource`.
  - `PluginConfiguration.TestStreamUrl` (string, default empty).
  - `PluginServiceRegistrator : IPluginServiceRegistrator` registering `ICatalog` → `StaticCatalog` and `IChannel` → `MakoChannel`. Jellyfin 12.1 builds its channel list from `IEnumerable<IChannel>` in DI, so without this registration the channel never appears.

- [ ] **Step 1: Write the failing test**

The settings page is an embedded resource looked up by name at runtime; a typo gives a blank page and no error. This test catches that.

`tests/Jellyfin.Plugin.Mako.Tests/PluginPagesTests.cs`:
```csharp
namespace Jellyfin.Plugin.Mako.Tests;

public class PluginPagesTests
{
    [Fact]
    public void SettingsPageIsEmbeddedUnderTheNameThePluginAdvertises()
    {
        string[] embedded = typeof(Plugin).Assembly.GetManifestResourceNames();

        Assert.Contains(Plugin.SettingsPageResource, embedded);
    }

    [Fact]
    public void PluginGuidMatchesTheBuildMetadata()
    {
        Assert.Equal(Guid.Parse("1b35ed7d-7558-4592-aa1a-681b9d67d305"), Plugin.PluginGuid);
    }
}
```

- [ ] **Step 2: Run to see it fail**

Run: `dotnet test`
Expected: build error — `'Plugin' could not be found`.

- [ ] **Step 3: Write the configuration class**

`src/Jellyfin.Plugin.Mako/Configuration/PluginConfiguration.cs`:
```csharp
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Mako.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Step A only: the signed stream URL every test episode plays. It is pasted by hand
    /// because mako's token expires 15 minutes after it is issued.
    /// </summary>
    public string TestStreamUrl { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Write the plugin class**

`src/Jellyfin.Plugin.Mako/Plugin.cs`:
```csharp
using Jellyfin.Plugin.Mako.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Mako;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string SettingsPageResource = "Jellyfin.Plugin.Mako.Configuration.configPage.html";

    public static readonly Guid PluginGuid = Guid.Parse("1b35ed7d-7558-4592-aa1a-681b9d67d305");

    private static Plugin? _instance;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        _instance = this;
    }

    public static Plugin Instance
        => _instance ?? throw new InvalidOperationException("The Mako plugin has not been loaded yet.");

    public override string Name => "Mako";

    public override Guid Id => PluginGuid;

    public override string Description => "mako 12+ VOD as a Jellyfin channel, fetched on demand.";

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = SettingsPageResource,
        },
    ];
}
```

- [ ] **Step 5: Write the registrator**

`src/Jellyfin.Plugin.Mako/PluginServiceRegistrator.cs`:
```csharp
using Jellyfin.Plugin.Mako.Catalog;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Mako;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // The URL is read through a delegate so a change on the settings page
        // takes effect on the next play, with no restart.
        serviceCollection.AddSingleton<ICatalog>(
            _ => new StaticCatalog(() => Plugin.Instance.Configuration.TestStreamUrl));
        serviceCollection.AddSingleton<IChannel, MakoChannel>();
    }
}
```

- [ ] **Step 6: Write the settings page**

`src/Jellyfin.Plugin.Mako/Configuration/configPage.html`:
```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <title>Mako</title>
</head>
<body>
<div id="MakoConfigPage" data-role="page" class="page type-interior pluginConfigurationPage"
     data-require="emby-input,emby-button">
    <div data-role="content">
        <div class="content-primary">
            <form id="MakoConfigForm">
                <div class="verticalSection">
                    <h2 class="sectionTitle">Mako — step A prototype</h2>
                    <div class="inputContainer">
                        <label class="inputLabel inputLabelUnfocused" for="TestStreamUrl">Test stream URL</label>
                        <input id="TestStreamUrl" name="TestStreamUrl" type="text" is="emby-input" />
                        <div class="fieldDescription">
                            Every test episode plays this URL. Paste a freshly signed
                            <code>masterM3U8</code> from <code>mako12_vod.py --resolve</code> and press play
                            within a few minutes: the token lasts 15 minutes.
                        </div>
                    </div>
                </div>
                <div>
                    <button is="emby-button" type="submit" class="raised button-submit block emby-button">
                        <span>Save</span>
                    </button>
                </div>
            </form>
        </div>
    </div>
    <script type="text/javascript">
        var MakoConfig = { pluginUniqueId: '1b35ed7d-7558-4592-aa1a-681b9d67d305' };

        document.querySelector('#MakoConfigPage').addEventListener('pageshow', function () {
            Dashboard.showLoadingMsg();
            ApiClient.getPluginConfiguration(MakoConfig.pluginUniqueId).then(function (config) {
                document.querySelector('#TestStreamUrl').value = config.TestStreamUrl || '';
                Dashboard.hideLoadingMsg();
            });
        });

        document.querySelector('#MakoConfigForm').addEventListener('submit', function (event) {
            event.preventDefault();
            Dashboard.showLoadingMsg();
            ApiClient.getPluginConfiguration(MakoConfig.pluginUniqueId).then(function (config) {
                config.TestStreamUrl = document.querySelector('#TestStreamUrl').value.trim();
                ApiClient.updatePluginConfiguration(MakoConfig.pluginUniqueId, config).then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
            });
            return false;
        });
    </script>
</div>
</body>
</html>
```

- [ ] **Step 7: Write `build.yaml`**

`build.yaml`:
```yaml
name: "Mako"
guid: "1b35ed7d-7558-4592-aa1a-681b9d67d305"
version: "0.1.0.0"
targetAbi: "12.1.0.0"
framework: "net10.0"
owner: "ofek"
category: "Channels"
overview: "mako 12+ VOD as a Jellyfin channel"
description: "Prototype. Shows mako VOD in Jellyfin, fetching only what a user opens or plays."
artifacts:
  - "Jellyfin.Plugin.Mako.dll"
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add build.yaml src tests
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: plugin identity, the test-URL setting, and DI wiring" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"
```

---

### Task 7: Build, install and uninstall scripts, and the README

**Files:**
- Create: `scripts/build.sh`, `scripts/install.sh`, `scripts/uninstall.sh`, `README.md`

**Interfaces:**
- Consumes: the built project; on the phone, `$HOME/stack.env` (for `JELLYFIN_KEY`), `$HOME/stack/lib/api.sh` (for `jellyfin_auth`) and `$HOME/stack/mediactl`. These are used at run time on the phone only; nothing in this project's source depends on `stack/`.
- Produces: `artifacts/Mako_0.1.0.0/` containing `Jellyfin.Plugin.Mako.dll` and `meta.json`; the plugin installed at `$HOME/config/jellyfin/plugins/Mako_0.1.0.0/` on the phone (Jellyfin's `ProgramDataPath` there is `$HOME/config/jellyfin`, read from `/System/Info`).

Connection settings match `stack/deploy.sh`: `SSH_HOST`, `SSH_USER`, optional `SSH_PORT` (default 8022) and `SSH_KEY` (default `~/.ssh/oneplus_media`). The user's values are in the untracked `oneplus-ssh.personal.md`.

- [ ] **Step 1: Write `scripts/build.sh`**

```bash
#!/usr/bin/env bash
# Builds the plugin and assembles the folder Jellyfin loads.
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN_NAME="Mako"
PLUGIN_VERSION="0.1.0.0"
PLUGIN_GUID="1b35ed7d-7558-4592-aa1a-681b9d67d305"
TARGET_ABI="12.1.0.0"
PUBLISH_DIR="$PROJECT_ROOT/artifacts/publish"
PLUGIN_DIR="$PROJECT_ROOT/artifacts/${PLUGIN_NAME}_${PLUGIN_VERSION}"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"

rm -rf "$PUBLISH_DIR" "$PLUGIN_DIR"
"$DOTNET" publish "$PROJECT_ROOT/src/Jellyfin.Plugin.Mako" -c Release -o "$PUBLISH_DIR" --nologo

mkdir -p "$PLUGIN_DIR"
cp "$PUBLISH_DIR/Jellyfin.Plugin.Mako.dll" "$PLUGIN_DIR/"

cat > "$PLUGIN_DIR/meta.json" <<EOF
{
  "category": "Channels",
  "changelog": "Step A prototype",
  "description": "Prototype. Shows mako VOD in Jellyfin, fetching only what a user opens or plays.",
  "guid": "$PLUGIN_GUID",
  "name": "$PLUGIN_NAME",
  "overview": "mako 12+ VOD as a Jellyfin channel",
  "owner": "ofek",
  "targetAbi": "$TARGET_ABI",
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "version": "$PLUGIN_VERSION",
  "status": "Active",
  "autoUpdate": false,
  "assemblies": []
}
EOF

echo "Built: $PLUGIN_DIR"
ls -la "$PLUGIN_DIR"
```

- [ ] **Step 2: Write `scripts/install.sh`**

```bash
#!/usr/bin/env bash
# Copies the built plugin to the phone, restarts Jellyfin (after asking), and reports the plugin's status.
#   SSH_HOST=<ip> SSH_USER=<user> ./scripts/install.sh
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN_FOLDER="Mako_0.1.0.0"
LOCAL_DIR="$PROJECT_ROOT/artifacts/$PLUGIN_FOLDER"
REMOTE_PLUGINS_DIR='$HOME/config/jellyfin/plugins'
STARTUP_TIMEOUT_SECONDS=180

HOST="${SSH_HOST:?set SSH_HOST (see oneplus-ssh.personal.md)}"
USER_="${SSH_USER:?set SSH_USER (see oneplus-ssh.personal.md)}"
PORT="${SSH_PORT:-8022}"
KEY="${SSH_KEY:-$HOME/.ssh/oneplus_media}"
SSH=(ssh -i "$KEY" -p "$PORT" -o ConnectTimeout=10 "$USER_@$HOST")

[ -f "$LOCAL_DIR/Jellyfin.Plugin.Mako.dll" ] || { echo "Nothing to install: run scripts/build.sh first." >&2; exit 1; }

echo "Copying $PLUGIN_FOLDER to the phone..."
"${SSH[@]}" "mkdir -p $REMOTE_PLUGINS_DIR/$PLUGIN_FOLDER"
rsync -a --delete -e "ssh -i $KEY -p $PORT" "$LOCAL_DIR/" "$USER_@$HOST:config/jellyfin/plugins/$PLUGIN_FOLDER/"

read -r -p "Restart Jellyfin now? Playback in progress will stop. [y/N] " answer
if [[ ! "$answer" =~ ^[Yy]$ ]]; then
    echo "Copied but not loaded. The plugin loads at the next Jellyfin restart."
    exit 0
fi

"${SSH[@]}" '$HOME/stack/mediactl restart jellyfin'

echo "Waiting for Jellyfin to answer..."
"${SSH[@]}" "
    deadline=\$(( \$(date +%s) + $STARTUP_TIMEOUT_SECONDS ))
    until curl -sf -m 5 http://localhost:8096/System/Info/Public >/dev/null; do
        [ \$(date +%s) -lt \$deadline ] || { echo 'Jellyfin did not come back in time.' >&2; exit 1; }
        sleep 5
    done
"

echo "Plugin status:"
"${SSH[@]}" '
    . "$HOME/stack.env"; . "$HOME/stack/lib/api.sh"
    curl -sS -m 20 -H "$(jellyfin_auth "$JELLYFIN_KEY")" http://localhost:8096/Plugins \
        | jq -r ".[] | select(.Name == \"Mako\") | \"\(.Name) \(.Version): \(.Status)\""
'
```

- [ ] **Step 3: Write `scripts/uninstall.sh`**

```bash
#!/usr/bin/env bash
# Removes the plugin folder from the phone and restarts Jellyfin (after asking).
#   SSH_HOST=<ip> SSH_USER=<user> ./scripts/uninstall.sh
set -euo pipefail

PLUGIN_FOLDER_GLOB='Mako_*'

HOST="${SSH_HOST:?set SSH_HOST (see oneplus-ssh.personal.md)}"
USER_="${SSH_USER:?set SSH_USER (see oneplus-ssh.personal.md)}"
PORT="${SSH_PORT:-8022}"
KEY="${SSH_KEY:-$HOME/.ssh/oneplus_media}"
SSH=(ssh -i "$KEY" -p "$PORT" -o ConnectTimeout=10 "$USER_@$HOST")

echo "Plugin folders on the phone:"
"${SSH[@]}" "ls -d \$HOME/config/jellyfin/plugins/$PLUGIN_FOLDER_GLOB 2>/dev/null || echo '(none)'"

read -r -p "Delete them and restart Jellyfin? [y/N] " answer
[[ "$answer" =~ ^[Yy]$ ]] || { echo "Nothing changed."; exit 0; }

"${SSH[@]}" "rm -rf \$HOME/config/jellyfin/plugins/$PLUGIN_FOLDER_GLOB && \$HOME/stack/mediactl restart jellyfin"
echo "Removed. The plugin's settings file under plugins/configurations/ is left in place."
```

- [ ] **Step 4: Make them executable and build**

```bash
chmod +x scripts/*.sh
./scripts/build.sh
```
Expected: ends with `Built: .../artifacts/Mako_0.1.0.0` and lists `Jellyfin.Plugin.Mako.dll` and `meta.json`. The folder must contain **no** `MediaBrowser.*.dll` or `Jellyfin.*.dll` other than the plugin's own — if it does, `ExcludeAssets` in the csproj is wrong.

- [ ] **Step 5: Write `README.md`**

```markdown
# jellyfin-plugin-mako

A Jellyfin Channel plugin that shows mako 12+ VOD inside Jellyfin. It fetches only what a
user opens or plays — nothing is downloaded, synced or crawled. Separate from `stack/`.

Status: **step A prototype** — two hard-coded shows, no mako calls. See `docs/spec.md` for
the design and `docs/plan.md` for the tasks.

## Build

Needs the .NET 10 SDK.

    ./scripts/build.sh          # → artifacts/Mako_0.1.0.0/
    dotnet test

## Install on the phone

    SSH_HOST=<ip> SSH_USER=<user> ./scripts/install.sh

Copies the plugin into `~/config/jellyfin/plugins/Mako_0.1.0.0/`, asks before restarting
Jellyfin, and prints the plugin's status (Active / NotSupported / Malfunctioned).

## Test a stream (step A)

1. Sign a URL: `python3 mako12_vod.py "שמש" --episode 1 --resolve` and copy `masterM3U8`.
2. Dashboard → Plugins → Mako → paste it into **Test stream URL** → Save.
3. Press play on any test episode within a few minutes.

## Remove

    SSH_HOST=<ip> SSH_USER=<user> ./scripts/uninstall.sh
```

- [ ] **Step 6: Commit**

```bash
git add scripts README.md
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: build, install and uninstall scripts" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"
```

---

### Task 8: Install on the phone, run the step A checks, decide the gate

**Files:**
- Create: `docs/step-a-results.md`

**Interfaces:**
- Consumes: everything above.
- Produces: a filled-in results file and a go / no-go for step B.

This task changes the running server (one plugin folder, one Jellyfin restart). **Get the user's go-ahead before Step 1.** The user performs the on-screen checks; the agent performs the server-side ones.

- [ ] **Step 1: Install**

```bash
export SSH_HOST=<from oneplus-ssh.personal.md> SSH_USER=<from oneplus-ssh.personal.md>
./scripts/install.sh
```
Expected last line: `Mako 0.1.0.0: Active` → **check 1 passes**.
If it prints `NotSupported` or `Malfunctioned`, or nothing: read the reason from the newest file in `~/config/jellyfin/log/` on the phone (`grep -i mako`), record it in the results file, run `./scripts/uninstall.sh`, and stop — this is spec risk 2 and needs the user's decision.

- [ ] **Step 2: Create the results file**

`docs/step-a-results.md`:
```markdown
# Step A results

Date: ____  Jellyfin: 12.1.0  Plugin: 0.1.0.0

| # | Check | Web | Android TV | Fladder | Notes |
|---|---|---|---|---|---|
| 1 | Plugin is Active after a restart | — | — | — | |
| 2 | The mako channel is visible and opens | | | | where in the app it appears |
| 3 | Show → seasons → episodes browse; names and poster show | | | | |
| 4 | Log shows a plugin call only when a folder is opened; none while idle | — | — | — | |
| 5 | Play works with the pasted URL | | | | direct play or transcode? |
| 6 | Seek works; stop and resume continues from the same place | | | | |
| 7 | Reopening a folder within 3 hours makes no plugin call | — | — | — | |
| 8 | A genre filter is offered on the channel's show list | | | | |

## Gate

Decision: ____ (go to step B / stop and fall back to the Xtream route)
Reason: ____
Channel layout for the full plugin (from check 8): ____
```

- [ ] **Step 3: Check 4 and 7 from the log (agent)**

On the phone, follow the log while the user opens the channel, then a show, then a season, in the web app:
```bash
ssh -i ~/.ssh/oneplus_media -p 8022 "$SSH_USER@$SSH_HOST" 'tail -n 0 -f "$(ls -t $HOME/config/jellyfin/log/log_*.log | head -1)" | grep --line-buffered "mako:"'
```
Expected: exactly one `mako: folder opened` line per folder the user opens, in the order they open them — `Root ''`, `Show 'shemesh'`, `Season 'shemesh-s1'`. Then ask the user to reopen the same season: **no new line** appears (check 7 — Jellyfin's 3-hour folder cache). Leave it running for 10 minutes with nobody browsing: **no line** appears (check 4). Record all three observations.

- [ ] **Step 4: Checks 2, 3, 8 on the three apps (user)**

Ask the user to find and browse the channel on web, Android TV and Fladder, and to look for a filter button offering genres on the channel's show list. Record where the channel appears in each app, and any app where it does not.

- [ ] **Step 5: Checks 5 and 6 on the three apps (user, agent watches)**

The user signs a URL and pastes it (README "Test a stream"), then plays an episode on each app, seeks, stops, and resumes. While an episode plays, the agent reads how Jellyfin is delivering it:
```bash
ssh -i ~/.ssh/oneplus_media -p 8022 "$SSH_USER@$SSH_HOST" '. $HOME/stack.env; . $HOME/stack/lib/api.sh; curl -sS -m 20 -H "$(jellyfin_auth "$JELLYFIN_KEY")" http://localhost:8096/Sessions | jq -r ".[] | select(.NowPlayingItem) | \"\(.Client): \(.PlayState.PlayMethod) \(.TranscodingInfo.TranscodeReasons // [])\""'
```
Record `DirectPlay`, `DirectStream` or `Transcode` per app. A transcode is spec risk 4: note the reasons Jellyfin gives; it is input for step B, not a failure of step A.

- [ ] **Step 6: Decide the gate with the user**

Apply the stop rule (spec §3): if check 2 or 3 fails on an app that matters, stop and fall back to the Xtream route. If 5 or 6 fails, investigate `MediaSourceFactory`'s flags and the `hls` container value before deciding. Write the decision, the reason, and the channel layout from check 8 into the results file.

- [ ] **Step 7: Commit the results**

```bash
git add docs/step-a-results.md
git commit --author="Claude Code <claude@anthropic.com>" -m "mako plugin: step A results and the gate decision" -m "Co-Authored-By: Claude Code <claude@anthropic.com>"
```

- [ ] **Step 8: If the gate is "go", plan step B**

Invoke the writing-plans skill for spec §5, using the step A results: the `MediaSourceInfo` flags that produced direct play, and the channel layout check 8 selected. Do not start step B code before that plan exists and is approved.
