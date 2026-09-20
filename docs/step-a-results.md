# Step A results

Date: 2026-09-20  Jellyfin: 12.1.0  Plugin: 0.1.0.0 (commit 05fe0a4)

| # | Check | Web | Android TV | Fladder | Notes |
|---|---|---|---|---|---|
| 1 | Plugin is Active after a restart | — | — | — | **PASS.** `/Plugins` reports `Mako 0.1.0.0: Active`. Log: "Loaded assembly Jellyfin.Plugin.Mako" then "Loaded plugin: Mako 0.1.0.0", no ERR/FTL lines since the restart. `/Channels` lists `mako` (type Channel). A .NET 10 plugin built on the laptop loads under Termux — spec risk 2 is closed. |
| 2 | The mako channel is visible and opens | | | | where in the app it appears |
| 3 | Show → seasons → episodes browse; names and poster show | | | | |
| 4 | Log shows a plugin call only when a folder is opened; none while idle | — | — | — | |
| 5 | Play works with the pasted URL — once with the container empty, once with `hls` | | | | direct play or transcode? |
| 6 | Seek works; stop and resume continues from the same place | | | | |
| 7 | Reopening a folder within 3 hours makes no plugin call | — | — | — | |
| 8 | A genre filter is offered on the channel's show list | | | | |

## Gate

Decision: ____ (go to step B / stop and fall back to the Xtream route)
Reason: ____
Channel layout for the full plugin (from check 8): ____
