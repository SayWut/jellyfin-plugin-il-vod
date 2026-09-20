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
| A2 | Model and folder ids | agent | `todo` | |
| A3 | Catalogue interface and the static catalogue | agent | `todo` | |
| A4 | Mapping to Jellyfin's types | agent | `todo` | |
| A5 | The channel | agent | `todo` | |
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

## Open items

- The plan's C# has not been compiled yet; task A4 says what to do if a Jellyfin
  12.1 property named there does not exist.
- mako's show index carries no genre per show (checked 2026-09-20, 654 shows). A
  genre filter in the full plugin needs each genre's list fetched separately.
