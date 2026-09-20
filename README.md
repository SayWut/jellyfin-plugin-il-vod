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
4. Check 5 is run twice: once with **Test stream container** left empty (Jellyfin decides),
   once with it set to `hls` (forces the HLS-manifest path — Jellyfin 12.1 never plays this
   directly).
5. Jellyfin caches the resolved stream per episode for 5 minutes. After pasting a new URL,
   play a *different* episode (there are six) or wait 5 minutes — replaying the same one
   reuses the old URL.
6. Jellyfin caches channel folders for 3 hours; after redeploying a changed catalogue, bump
   `ContentVersion` in `MakoChannel.cs` or wait.

## Remove

    SSH_HOST=<ip> SSH_USER=<user> ./scripts/uninstall.sh
