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
| A6 | Plugin identity, the one setting, and wiring | agent | `todo` | |
| A7 | Build, install and uninstall scripts, and the README | agent | `todo` | |
| A8 | Install on the phone, run the step A checks, decide the gate | agent + user | `todo` | Changes the running server: one plugin folder, one Jellyfin restart. Needs the user's go-ahead and the user at the three apps. |

## Step B — live mako under one pinned show

Not planned yet. Gated on A8.

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

## Open items

- The plan's C# has not been compiled yet; task A4 says what to do if a Jellyfin
  12.1 property named there does not exist.
- mako's show index carries no genre per show (checked 2026-09-20, 654 shows). A
  genre filter in the full plugin needs each genre's list fetched separately.
