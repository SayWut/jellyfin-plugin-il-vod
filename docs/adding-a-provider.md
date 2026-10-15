# Adding a provider

A provider is one folder under `src/Jellyfin.Plugin.IlVod/Providers/<Name>/`. Nothing outside
that folder changes except one line in `PluginServiceRegistrator.cs`, plus an optional section
in the settings. mako (`Providers/Mako/`) is the worked example throughout this guide.

## The contracts (in `Core/`)

| Type | Who writes it | What it does |
|---|---|---|
| `IContentSource<TIndex, TShowPage, TSeasonPage, TStream>` | the provider | Fetches. Answers in the provider's **own** records; knows nothing of the shared model. Owns pacing, raw-page caching, stale copies. |
| `IContentTransformer<…same four…>` | the provider | Converts the provider's records into `Show` / `Season` / `Episode` / `StreamLink`. Pure: no network, cache or clock. |
| `SourceCatalog<…>` | already written | Joins the two halves into an `ICatalog` and caches stream links in memory (never on disk). |
| `IContentProvider` | the provider | `Info` + `Catalog`. |
| `ProviderInfo` | the provider | Id, display name, home page, probe size, default categories. |
| `ProviderUnavailableException` | the provider derives from it | "Try again later": refused, timed out, challenged. |

The source and the transformer must not reference each other. They share only the records,
which live in the provider's root namespace. For mako those are `Providers/Mako/MakoContent.cs`,
read by `Source/` and converted by `Transform/`.

## Steps

1. **Records.** Create `Providers/<Name>/<Name>Content.cs` with the provider's own shapes: what
   its show list, show page, season page and play answer contain. Name things the way the
   provider does.
2. **Source.** Create `Providers/<Name>/Source/<Name>Source.cs` implementing
   `IContentSource<…>`, plus the provider's HTTP client.
   - Reuse `Cache/TimedCache`: persisted for pages, memory-only for anything signed.
   - Reuse `RequestPacer` if the provider rate-limits.
   - Fetch only what the user's action needs (N1). Never fetch ahead.
   - Derive the provider's transient failures from `ProviderUnavailableException`. Throw
     `FormatException` for an answer that is not the provider's page.
   - **Never cache a page that fails to parse**, and never answer a show or season list that is
     shorter because something failed. Jellyfin deletes whatever an answer lacks; throw instead.
   - An episode id that cannot be played: throw `StreamUnavailableException`.
3. **Transformer.** Create `Providers/<Name>/Transform/<Name>Transformer.cs` implementing
   `IContentTransformer<…>`.
   - `Show.Genres`: the provider's category names **as the provider spells them**. Mapping
     happens later, in `Aggregation/CategoryMapper`.
   - `StreamLink.Description.Format`: the stream's codecs (`StreamFormat`), so Jellyfin can copy
     instead of transcoding. Leave it `null` if they are unknown; Jellyfin then probes.
   - For an HLS stream, describe the master playlist with `Hls/MasterPlaylist` (and measure its
     length with `Hls/MediaPlaylist` if the provider states none) rather than parsing it again.
   - Ids must be stable across restarts. A changed id makes Jellyfin recreate the item and lose
     its watched state.
4. **Categories.** Create `Providers/<Name>/<Name>Categories.cs` mapping **every** category name
   the provider uses onto `Core/SharedCategories`. The shared categories are one closed set: a
   name the mapping leaves out, and a show with no category, land in `SharedCategories.Fallback`
   ("אחר") rather than becoming a category of their own. Add a shared category only when no
   existing one fits and the category is a real genre other providers will have too; map a
   provider's brand names (e.g. `דיגיטל 13`) to what their shows are. `<Name>CategoriesTests`
   should check every value is in `SharedCategories.All`. Reshet 13 (`Providers/Reshet13/`) is
   the second worked example, over a JSON API rather than mako's pages.
5. **Provider.** Create `Providers/<Name>/<Name>Provider.cs` implementing `IContentProvider`, with
   a static `ProviderInfo`:
   - `Id`: lower-case `[a-z0-9]+`, **never changed afterwards** (it prefixes every item id).
   - `ProbeSize`: `null` unless the streams need ffmpeg's probe size limited, as mako's do.
6. **Registration.** Create `Providers/<Name>/<Name>ServiceCollectionExtensions.cs` with
   `Add<Name>Provider(services, dataFolder, configuration)`. Register the graph, then
   `services.AddSingleton(<Name>Provider.Description)` (the `ProviderInfo`) and one
   `IContentProvider` built over `new SourceCatalog<…>(source, new <Name>Transformer(),
   new TimedCache(clock), …)`. Name the provider's files `<id>-….json` in the shared data
   folder. Then add one line to `IlVodServiceCollectionExtensions.AddIlVodServices`:
   `services.Add<Name>Provider(dataFolder, configuration);`.
   Every factory must stay lazy. Jellyfin builds the channel before the plugin's data folder or
   configuration exist, so nothing may read them at registration.
7. **Settings (optional).** If the provider needs its own settings, add a `<Name>Settings` class
   and a property for it on `PluginConfiguration`, then add a section to `configPage.html`. The
   provider toggle and category rows appear on their own, from `GET /IlVod/Providers`.
8. **Tests** (`tests/Jellyfin.Plugin.IlVod.Tests/Providers/<Name>/`):
   - transformer tests from saved pages under `Fixtures/`, with no HTTP at all (see
     `MakoTransformerTests`);
   - source tests over a fake HTTP client, including the failure and never-cache cases;
   - a request-budget test that drives a whole user session through the composed
     `SourceCatalog` and counts requests (see `OnDemandRequestBudgetTests`);
   - `PluginServiceRegistratorTests`: the provider resolves as an `IContentProvider` and its
     `ProviderInfo` is registered.

## What you get for free

- One tile: the provider's shows appear in the IL-VOD channel next to every other provider's.
- One set of categories: its shows sit under the same shared categories as every other
  provider's, with "אחר" for whatever its mapping does not cover.
- Failure isolation: if the provider is down, the channel serves its last good show list
  (`show-snapshots.json`) or leaves it out. The other providers are unaffected.
- A toggle and a category table on the settings page.
- Ids scoped as `<id>:<provider's id>` and routed back to the provider automatically.
- Stream-link caching (memory only), and probe-size limiting if `ProbeSize` is set.
