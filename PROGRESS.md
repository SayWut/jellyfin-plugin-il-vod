# Progress

Shared state across sessions. **Updated after every task** — status, plus a note
whenever reality differed from the plan. Checkbox ticks inside `docs/plan.md` are
for a human reading along. *This* file is the record of truth.

Read `docs/spec.md` for what is being built and why, and `docs/plan.md` for the
tasks.

## Status legend

| Value | Meaning |
|---|---|
| `todo` | Not started |
| `wip` | In progress |
| `blocked` | Stopped, waiting on the user or an unresolved discrepancy |
| `done` | Every verification step in the task passed |
| `skipped` | Deliberately not done — the note must say why |

## Where things stand

Design approved 2026-09-20. Step A (static plugin) is being built. Step B (live
mako calls) has no plan yet — it is written after the step A gate in task A8.

## Step A — static plugin

| Task | Title | Driver | Status | Notes |
|---|---|---|---|---|
| A1 | Toolchain and empty solution | agent | `done` | Empty solution builds and `dotnet test` passes 1/1 against Jellyfin.Controller/Model 12.1.0 on net10.0. |
| A2 | Model and folder ids | agent | `done` | Model records (Show, Season, Episode, StreamLink) and FolderId with round-trip parsing and 8 tests passing. |
| A3 | Catalogue interface and the static catalogue | agent | `done` | ICatalog interface, StaticCatalog with two shows and their seasons, and StreamUnavailableException. All 17 tests passing. |
| A4 | Mapping to Jellyfin's types | agent | `done` | ChannelItemMapper (show/season/episode) and MediaSourceFactory built against real Jellyfin.Controller/Model 12.1.0 assemblies. All 21 tests passing. |
| A5 | The channel | agent | `done` | MakoChannel implements IChannel + IRequiresMediaInfoCallback (not ISupportsLatestMedia); folders on open, stream on play. All 26 tests passing. |
| A6 | Plugin identity, the one setting, and wiring | agent | `done` | Plugin, PluginConfiguration, PluginServiceRegistrator, configPage.html and build.yaml added; settings page embedded and DI wiring registers ICatalog/IChannel. All 28 tests passing. |
| A7 | Build, install and uninstall scripts, and the README | agent | `done` | `scripts/build.sh` verified: `artifacts/Mako_0.1.0.0/` contains exactly `Jellyfin.Plugin.Mako.dll` and a valid `meta.json`, no stray `MediaBrowser.*`/`Jellyfin.*` dlls; `artifacts/` confirmed git-ignored. `install.sh`/`uninstall.sh` only syntax-checked (`bash -n`), never run — they touch the phone, which is task A8. `dotnet test` still 28/28. |
| A8 | Install on the phone, run the step A checks, decide the gate | agent + user | `todo` | Changes the running server: one plugin folder, one Jellyfin restart. Needs the user's go-ahead and the user at the three apps. |

## Step B — live mako under one pinned show

Not planned yet. Gated on A8.

## Whole-branch review fixes (2026-09-20)

- Added a second setting, `TestStreamContainer`, so step A check 5 can be run once with the
  container empty (Jellyfin decides) and once with `hls` (forces the manifest path, which
  Jellyfin 12.1 never plays directly) — the hard-coded `"hls"` container was pre-deciding the
  check.
- `StaticCatalog` now reads the container through a delegate at play time, the same way it
  already read the URL; `MediaSourceFactory` maps an empty container to `null` so Jellyfin
  decides instead of matching against an empty string.
- `Episode` gained a `Duration` (fixed at 25 minutes in `StaticCatalog`) so `RunTimeTicks` is
  set on playable items and seek/resume (check 6) has something to work against.
- `FolderId.ToString()` and `MakoChannel.ItemsInside` now throw `InvalidOperationException` on
  an unhandled `FolderKind` instead of silently returning an empty string/list, so a future
  enum member is a loud failure, not a quiet wrong answer.
- `MakoChannel.GetChannelItems` now catches a `FormatException` from `FolderId.Parse`, logs a
  warning naming the raw folder id, and returns an empty result instead of letting the
  exception surface as an HTTP 500 for the whole Channels view.
- Added tests pinning three invariants: `MakoChannel` does not implement
  `ISupportsMediaProbe` (would make Jellyfin ffprobe the URL at browse time),
  `GetChannelItemMediaInfo` throws `StreamUnavailableException` when no URL is set, and the
  embedded settings page contains the live `Plugin.PluginGuid`.
- `Description` and `HomePageUrl` on `MakoChannel` became named constants
  (`ChannelDescription`, `ChannelHomePageUrl`), matching `ChannelName`/`ContentVersion`.
- `scripts/install.sh`: the rsync destination and the `mkdir` path are now both derived from
  one `REMOTE_PLUGINS_RELATIVE` constant so they cannot drift; the plugin-status check now
  fails loudly (`set -o pipefail`, `curl -f`, captured output, explicit error messages) instead
  of silently printing nothing; the Jellyfin URL is one named constant used in both places it
  appears.
- `scripts/uninstall.sh`: every remote command now guards `$HOME` first
  (`: "${HOME:?remote HOME is empty}"`); if the earlier `ls` finds nothing, the script says so
  and exits before asking to delete anything.
- `scripts/build.sh`: now fails fast if the .NET 10 SDK is missing, and validates the written
  `meta.json` is parseable JSON before reporting success.
- Deleted `build.yaml` — nothing read it; `build.sh` writes `meta.json` from its own constants,
  so the file could only drift out of sync.
- `README.md` and `docs/spec.md` updated for the second setting, the two-pass check 5, the
  5-minute per-episode stream cache, and the 3-hour folder cache.

## Deviations log

Anything that differed from the spec or the plan, with the reason.

| Date | Task | What differed | Why |
|---|---|---|---|
| 2026-09-20 | A1 | `dotnet new sln` created `Jellyfin.Plugin.Mako.slnx`, not `.sln`. | This is .NET 10's new default; the brief said either is fine. |
| 2026-09-20 | A1 | Omitted the `<None Remove>`/`<EmbeddedResource Include>` item group for `Configuration\configPage.html` from the plugin `.csproj` (Step 4). | That file doesn't exist yet — Task 1's file list doesn't create it, and it's only added in Task 6 (`docs/plan.md`, `task-6-brief.md`). Embedding a resource that isn't on disk fails the build with CS1566. Will restore this item group in Task 6 alongside the real file. |
| 2026-09-20 | A2 | Deleted `tests/Jellyfin.Plugin.Mako.Tests/ToolchainTests.cs`. | Per controller ruling: ToolchainTests was a smoke test; real tests now make it redundant. |
| 2026-09-20 | A4 | `ChannelItemMapper.ToFolder(Season season)` takes only the season, not the brief's `ToFolder(Season season, Show? show = null)`. | Controller ruling: nothing in step A calls it with a show, so the optional parameter (and the `ImageUrl`/`SeriesName` assignments it would feed) would be speculative. |
| 2026-09-20 | A4 | None — every type, namespace, enum member and property named in the brief (`ChannelItemInfo`, `ChannelFolderType.Container`, `ChannelMediaType.Video`, `ChannelMediaContentType.Episode`, `MediaSourceInfo`, `MediaProtocol.Http`, and every property assigned on them) exists exactly as written in Jellyfin.Controller/Model 12.1.0. Verified by reflecting over the real NuGet assemblies before writing code; no substitutions were needed. | Recorded per the task's requirement to note this check even when nothing differed. |
| 2026-09-20 | A5 | `DynamicImageResponse` (returned by `IChannel.GetChannelImage`) lives in namespace `MediaBrowser.Controller.Providers`, not `MediaBrowser.Model.Dto`. | Confirmed by reflecting over `MediaBrowser.Controller.dll` (12.1.0): the type's `FullName` is `MediaBrowser.Controller.Providers.DynamicImageResponse`. The brief's own `using` list already included `MediaBrowser.Controller.Providers`, so only a `using MediaBrowser.Model.Dto` import was extraneous for that call — no code change beyond keeping the brief's using directives as written. All other `IChannel`/`IRequiresMediaInfoCallback`/`ISupportsLatestMedia` members matched the brief exactly (verified by reflection). |
| 2026-09-20 | A5 | Did not add a `Microsoft.Extensions.Logging.Abstractions` package reference to the test project. | `NullLogger<MakoChannel>` resolved and ran without it — the type is already available transitively through the `Jellyfin.Controller`/`Jellyfin.Model` package references the test project already has. The brief said to add it only "if not found"; it was found. |
| 2026-09-20 | A6 | Restored the `<None Remove>`/`<EmbeddedResource Include>` item group for `Configuration\configPage.html` in the plugin `.csproj`, deferred from Task 1. | `Configuration/configPage.html` now exists, so the resource can be embedded; the `SettingsPageIsEmbeddedUnderTheNameThePluginAdvertises` test fails without it. This closes the A1 deviation. |
| 2026-09-20 | A6 | None — every type, namespace, and member the brief names for `Plugin`, `PluginConfiguration`, and `PluginServiceRegistrator` (`BasePlugin<T>` in `MediaBrowser.Common.Plugins`, `IHasWebPages`/`PluginPageInfo`/`BasePluginConfiguration` in `MediaBrowser.Model.Plugins`, `IApplicationPaths` in `MediaBrowser.Common.Configuration`, `IXmlSerializer` in `MediaBrowser.Model.Serialization`, `IPluginServiceRegistrator`/`IServerApplicationHost` in `MediaBrowser.Controller`/`MediaBrowser.Controller.Plugins`, and the `BasePlugin<T>` and `RegisterServices` signatures) exists exactly as written in Jellyfin.Controller/Common/Model 12.1.0. Verified by reflecting over the real NuGet assemblies before writing code; no substitutions were needed. | Recorded per the task's requirement to note this check even when nothing differed. |
| 2026-09-20 | A7 | `shellcheck` was not run on the three scripts — only `bash -n` syntax checks. | `command -v shellcheck` found nothing on this machine; the controller ruling made shellcheck conditional on it being installed. `install.sh`/`uninstall.sh` were also never executed, per the controller ruling that they touch the live phone (task A8). |
| 2026-09-20 | review fixes | Added a second plugin setting, `TestStreamContainer` (spec §4.1 amended). | Jellyfin 12.1's StreamBuilder refuses direct play for the `hls` container; a fixed container pre-decided step A check 5 instead of letting it be observed both ways. |
| 2026-09-20 | review fixes | `Episode` gained a `Duration` field, fixed at 25 minutes for every static episode. | Without a duration, `RunTimeTicks` was always null, so step A check 6 (seek/resume) had nothing to seek within. It is a fixture approximation; the real duration comes from mako in step B. |
| 2026-09-20 | review fixes | Deleted `build.yaml` (and its one line in `docs/plan.md`'s file-structure block). | Nothing read it — `scripts/build.sh` writes `meta.json` from its own constants — so it was a second, unread source of the same metadata that could only drift. `docs/plan.md`'s file-structure block had only one `build.yaml` line, not the two the fix brief expected; the rest of `docs/plan.md` (Task 6's steps) was left as written history, since it describes what was actually done at the time. |
| 2026-09-20 | review fixes | A folder id `FolderId.Parse` cannot parse now returns an empty `ChannelItemResult` from `GetChannelItems` instead of throwing/crashing the view. | Jellyfin 12.1 does not catch exceptions thrown from `IChannel.GetChannelItems`; an uncaught `FormatException` from a stale or corrupted folder id would take down the entire Channels view with an HTTP 500 instead of just that one folder. |

## Open items

- mako's show index carries no genre per show (checked 2026-09-20, 654 shows). A
  genre filter in the full plugin needs each genre's list fetched separately.
