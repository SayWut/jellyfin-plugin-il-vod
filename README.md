# jellyfin-plugin-mako

A Jellyfin Channel plugin that shows mako 12+ VOD inside Jellyfin. It fetches only what a
user opens or plays — nothing is downloaded, synced or crawled. Separate from `stack/`.

Status: **step C** — a live catalogue of every show under mako's real genres, fetched on
demand. See `docs/spec.md` for the design and `docs/plan-step-c.md` for the tasks.

## Build

Needs the .NET 10 SDK.

    ./scripts/build.sh          # → artifacts/Mako_0.1.0.0/
    dotnet test

## Install on the phone

    SSH_HOST=<ip> SSH_USER=<user> ./scripts/install.sh

Copies the plugin into `~/config/jellyfin/plugins/Mako_0.1.0.0/`, asks before restarting
Jellyfin, and prints the plugin's status (Active / NotSupported / Malfunctioned).

## What the channel lists

The channel lists every show mako files under one of its five real genres — ריאליטי,
דוקומנטרי, דרמה, קומדיה, בישול — one card per distinct show, carrying every genre that
names it. Opening the channel with a cold cache costs six paced requests (the genre menu,
then each of the five genre lists); a warm cache costs nothing.

Dashboard → Plugins → Mako has two settings:

- **Catalogue cache (hours)** — how long the genre menu and lists are kept before mako is
  asked again. Default 24; a cold open costs six paced requests.
- **App version** — the app version reported to mako, matching what mako's own Android app
  sends.

The four other cache lifetimes (show page, past season, current season, resolved stream
link) are config-file-only (spec §5.3) — there is no settings-page control for them, since
changing them is rare enough not to need one.

mako's page text (and its own config files) are cached to `mako-cache.json` in the plugin's
data folder — the genre menu and lists (24 h) alongside the show and season pages — so a
Jellyfin restart does not cause a wave of refetches; delete that file to force everything to
be refetched. A resolved stream link is never written to it, or anywhere else on disk (N3) —
it lives in memory for 5 minutes only.

## Add a library tile (manual, spec §6.5)

The plugin does not create its own library tile yet. To browse mako as an ordinary Jellyfin
library, with posters and full metadata pages, add one by hand:

1. Find the channel's metadata folder: `GET /Users/{userId}/Items/{channelId}` and read its
   `Path` field — on this server that is
   `<jellyfin config>/metadata/channels/<channelId>/metadata`.
2. Dashboard → Libraries → Add → content type **Shows** → point the folder at that path.
3. Disable every metadata fetcher and every image fetcher for the new library.
4. Save **without** scanning.
5. In the user's own Display settings, hide the "mako" channel tile so the same shows don't
   appear twice.

Jellyfin caches channel folders for 3 hours on its own; after redeploying a change that
alters what an episode item carries, bump `ContentVersion` in `MakoChannel.cs` (and, if the
item's own shape changed, `ChannelItemMapper.EpisodeShapeRevision`) or wait.

## Remove

    SSH_HOST=<ip> SSH_USER=<user> ./scripts/uninstall.sh

## ffmpeg probe size

Jellyfin's global `-probesize 1G` makes ffmpeg buffer mako's entire episode in RAM before
Android aborts it, because mako's streams carry a data track ffmpeg can never identify. The
plugin does not change the server-wide setting; instead it wraps Jellyfin's internal
transcode manager and rewrites `-probesize` to `20M` only for the command lines it builds for
its own streams (`Transcoding/ProbeSizeLimitingTranscodeManager.cs`). Every other stream is
untouched, and if the wrapped seam ever disappears in a future Jellyfin release the plugin
simply stops limiting it rather than failing to load — see `docs/spec.md` §4.3.

## After the first open: run a library scan once

Jellyfin lists a genre in its Genres views only once a `Genre` entity exists for the
name, and those are created by a post-scan task. So after the channel's first open the
"mako test" tile shows the shows but an empty Genres view until the next library scan
(Dashboard → Scan All Libraries, or wait for the scheduled one). The scan sends nothing to
mako.

## Known limitation

The first time a season is ever opened, it may list only some of its episodes.
Reload the page after a few seconds and the full list is there; it does not
happen again for that season. This is Jellyfin linking the new episodes in the
background, and it is deliberately not worked around (see `docs/spec.md` §10.1).
