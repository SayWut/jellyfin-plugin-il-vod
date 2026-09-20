# Step A results

Date: 2026-09-20/21  Jellyfin: 12.1.0  Plugin: 0.1.0.0 (commit 2aac586 + results)

| # | Check | Web | Android TV (official app) | Fladder (Android) | Notes |
|---|---|---|---|---|---|
| 1 | Plugin is Active after a restart | — | — | — | **PASS.** `Mako 0.1.0.0: Active`; a .NET 10 plugin built on the laptop loads under Termux. |
| 2 | The mako channel is visible and opens | PASS | PASS | PASS (channel opens) | |
| 3 | Show → seasons → episodes browse | PASS | PASS, but "buggy": after playing and going back, the show list is split into two pages | **FAIL** — opening the show gives "type of (Jelly, ChannelFolderItem) has not been implemented yet" | Fladder has no screen for Jellyfin's `ChannelFolderItem` type (confirmed in its source: only the enum and an "empty item" fallback mention it). Client limitation, not a plugin bug. |
| 4 | Plugin called only when a folder is opened; none while idle | — | — | — | **PASS.** One log line per folder opened, none in between. |
| 5 | Play works | PASS | PASS | not reachable | Container `hls`. Always a transcode: Jellyfin 12.1 never direct-plays an HLS manifest. Video re-encoded (libx264, ~17x realtime at 576x432), audio copied. Container empty was not run on the apps once `hls` worked. |
| 6 | Seek; stop and resume | not reported yet | not reported yet | not reachable | |
| 7 | Reopening a folder within 3 hours makes no plugin call | — | — | — | Consistent with the log (no repeat lines); not separately timed. |
| 8 | A genre filter is offered on the show list | not reported yet | not reported yet | — | |

## Found by running inside Jellyfin (none of it visible to unit tests)

1. `MediaSourceInfo.Id` must be empty — Jellyfin's HLS endpoint `Guid.Parse`s it. Fixed.
2. With no `MediaStreams`, ffmpeg analysed for up to 200 s. The plugin now describes H.264/AAC and sets `AnalyzeDurationMs`. Fixed.
3. Jellyfin's global `-probesize 1G` makes ffmpeg buffer mako's whole episode in RAM (unidentifiable data PID in the segments) until Android aborts it (exit 134). Not settable per stream in 12.1. Fixed plugin-side: `ITranscodeManager` is wrapped and `-probesize` is rewritten to 20M for mako streams only (spec §4.3).
4. mako's segment URLs carry their own 24-hour token minted from the 15-minute one.

## Gate

Decision: ____
Reason: ____
Channel layout for the full plugin (from check 8): ____
