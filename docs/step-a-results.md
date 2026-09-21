# Step A results

Date: 2026-09-20/21  Jellyfin: 12.1.0  Plugin: 0.1.0.0 (commit 2aac586 + results)

| # | Check | Web | Android TV (official app) | Fladder (Android) | Notes |
|---|---|---|---|---|---|
| 1 | Plugin is Active after a restart | — | — | — | **PASS.** `Mako 0.1.0.0: Active`; a .NET 10 plugin built on the laptop loads under Termux. |
| 2 | The mako channel is visible and opens | PASS | PASS | PASS (channel opens) | |
| 3 | Show → seasons → episodes browse | PASS | PASS, but "buggy": after playing and going back, the show list is split into two pages | **FAIL** — opening the show gives "type of (Jelly, ChannelFolderItem) has not been implemented yet" | Fladder has no screen for Jellyfin's `ChannelFolderItem` type (confirmed in its source: only the enum and an "empty item" fallback mention it). Client limitation, not a plugin bug. |
| 4 | Plugin called only when a folder is opened; none while idle | — | — | — | **PASS.** One log line per folder opened, none in between. |
| 5 | Play works | PASS | PASS | **PASS** (2026-09-21, after the placeholder-path change; before it every episode was badged "missing" and could not be played) | Container `hls`. Always a transcode: Jellyfin 12.1 never direct-plays an HLS manifest. Video re-encoded (libx264, ~17x realtime at 576x432), audio copied. Container empty was not run on the apps once `hls` worked. |
| 6 | Seek; stop and resume | not reported yet | not reported yet | not reachable | |
| 7 | Reopening a folder within 3 hours makes no plugin call | — | — | — | Consistent with the log (no repeat lines); not separately timed. |
| 8 | A genre filter is offered on the show list | not reported yet | not reported yet | — | |

## Found by running inside Jellyfin (none of it visible to unit tests)

1. `MediaSourceInfo.Id` must be empty — Jellyfin's HLS endpoint `Guid.Parse`s it. Fixed.
2. With no `MediaStreams`, ffmpeg analysed for up to 200 s. The plugin now describes H.264/AAC and sets `AnalyzeDurationMs`. Fixed.
3. Jellyfin's global `-probesize 1G` makes ffmpeg buffer mako's whole episode in RAM (unidentifiable data PID in the segments) until Android aborts it (exit 134). Not settable per stream in 12.1. Fixed plugin-side: `ITranscodeManager` is wrapped and `-probesize` is rewritten to 20M for mako streams only (spec §4.3).
4. mako's segment URLs carry their own 24-hour token minted from the 15-minute one.

## Gate

Decision: **go to step B** (user, 2026-09-21: "it works, it plays, it proves the concept").
Reason: a home-built Channel plugin loads on Jellyfin 12.1 under Termux, fetches only on user action, and browses and plays on all three apps — web, Android TV and Fladder.
Channel layout for the full plugin (from check 8): undecided — the genre-filter check was not reported. Decide when the step B plan is written.

## What made Fladder work (both are client workarounds, keep them in step B)

1. Shows are `ChannelFolderType.Series`, seasons `ChannelFolderType.Season` — Fladder has no screen for a plain channel folder.
2. Every episode carries a `MediaSourceType.Placeholder` source with a non-existent file path, so Jellyfin reports `LocationType: FileSystem` — Fladder treats any other location as "missing" and disables play. Jellyfin drops placeholder sources at play time, so clients still receive only the real stream.

## Carried into step B

- Seek and stop/resume (check 6) and the genre filter (check 8) were not reported on any app.
- Android TV: show list appears split into two pages after returning from playback — cause unknown, not our paging (Jellyfin never passes paging to a channel plugin).
- A show or season opened for the very first time can list no episodes for a few seconds, until Jellyfin links the new items.
- Video is re-encoded, not copied: the stream description has codec but no profile/level. Describe the stream fully (parse mako's master playlist at play time) so Jellyfin can stream-copy.
- The probe-size limiter hooks an internal Jellyfin seam; it logs a warning per play if it could not install.
- `uninstall.sh` still reports "no folder" when the phone is unreachable.
