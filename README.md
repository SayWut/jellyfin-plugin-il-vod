# jellyfin-plugin-mako

A Jellyfin Channel plugin that shows mako 12+ VOD inside Jellyfin. It fetches only what a
user opens or plays — nothing is downloaded, synced or crawled. Separate from `stack/`.

Status: **step B** — a live catalogue for one pinned mako show, fetched on demand. See
`docs/spec.md` for the design and `docs/plan-step-b.md` for the tasks.

## Build

Needs the .NET 10 SDK.

    ./scripts/build.sh          # → artifacts/Mako_0.1.0.0/
    dotnet test

## Install on the phone

    SSH_HOST=<ip> SSH_USER=<user> ./scripts/install.sh

Copies the plugin into `~/config/jellyfin/plugins/Mako_0.1.0.0/`, asks before restarting
Jellyfin, and prints the plugin's status (Active / NotSupported / Malfunctioned).

## Set the pinned show

Dashboard → Plugins → Mako has three settings:

- **Pinned show path** — mako's own page path for the show this channel shows, e.g.
  `/mako-vod-keshet/shemesh` (the default). Find it from the show's URL on
  `www.mako.co.il/mako-vod-...`.
- **Pinned show name** — the name shown for the show. mako's own show page carries no
  reliable title of its own.
- **App version** — the app version reported to mako, matching what mako's own Android app
  sends.

The four cache lifetimes (show page, past season, current season, resolved stream link) are
config-file-only (spec §5.3) — there is no settings-page control for them, since changing
them is rare enough not to need one.

mako's page text (and its own config files) are cached to `mako-cache.json` in the plugin's
data folder, so a Jellyfin restart does not cause a wave of refetches; delete that file to
force everything to be refetched. A resolved stream link is never written to it, or anywhere
else on disk (N3) — it lives in memory for 5 minutes only.

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

## Known limitation

The first time a season is ever opened, it may list only some of its episodes.
Reload the page after a few seconds and the full list is there; it does not
happen again for that season. This is Jellyfin linking the new episodes in the
background, and it is deliberately not worked around (see `docs/spec.md` §10.1).
