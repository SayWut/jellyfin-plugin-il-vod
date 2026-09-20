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
| A1 | Toolchain and empty solution | agent | `todo` | |
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

## Open items

- The plan's C# has not been compiled yet; task A4 says what to do if a Jellyfin
  12.1 property named there does not exist.
- mako's show index carries no genre per show (checked 2026-09-20, 654 shows). A
  genre filter in the full plugin needs each genre's list fetched separately.
