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
