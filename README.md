# IL-VOD (jellyfin-plugin-il-vod)

A Jellyfin Channel plugin that shows Israeli VOD providers inside Jellyfin as **one channel**,
"IL-VOD": every enabled provider's shows on one tile, under one shared set of categories. It
fetches only what a user opens or plays — nothing is downloaded, synced or crawled.

Providers today: **mako 12+**, **רשת 13** (whose streams play from an Israeli IP only, like
mako's) and **כאן 11** (the Kan BOX shows of Kan 11 and its current-affairs strip; Kan's archive,
kids, digital and radio content is left out). Adding another one is a matter of one folder under
`src/Jellyfin.Plugin.IlVod/Providers/` — see `docs/adding-a-provider.md`.

History: this began as the mako-only plugin "Mako" (moved out of oneplus-media-server on
2026-09-28, with its history). It became IL-VOD, a new plugin with its own GUID, on
2026-10-15. `docs/spec.md` and `docs/plan*.md` describe the mako work as it was done then.

## Build

Needs the .NET 10 SDK.

    ./scripts/build.sh          # → artifacts/IlVod_0.1.0.0/
    dotnet test

## Install on the phone

    SSH_HOST=<ip> SSH_USER=<user> ./scripts/install.sh

The phone's SSH host and user are in `oneplus-ssh.personal.md` at the root of the
oneplus-media-server repository.

Copies the plugin into `~/config/jellyfin/plugins/IlVod_0.1.0.0/`, asks before restarting
Jellyfin, and prints the plugin's status (Active / NotSupported / Malfunctioned).

### Moving from the old "Mako" plugin

IL-VOD is a different plugin (new GUID), so it does not replace "Mako" on its own:

1. Remove the old plugin folder `~/config/jellyfin/plugins/Mako_0.1.0.0/` (or uninstall "Mako"
   from Dashboard → Plugins) and install IL-VOD.
2. Settings start from their defaults, and mako's page cache starts cold.
3. Optional: to keep the device id mako's entitlement service already knows, copy the old
   plugin's `device.id` from its data folder to IL-VOD's data folder as `mako-device.id`.
4. Every show and episode is a new item (ids are now `mako:…`), so watched marks do not carry
   over. Redo the library tile below against the new channel.

## How it is built

```
IlVodChannel (Jellyfin's IChannel)
  └─ AggregateCatalog              one catalogue over every enabled provider
       ├─ ProviderScopedId         ids are "<provider>:<provider's own id>"
       ├─ CategoryMapper           provider categories → shared categories
       ├─ ShowListSnapshots        each provider's last good show list
       └─ IContentProvider × N
            └─ SourceCatalog       joins a provider's two halves:
                 ├─ IContentSource       fetches → the provider's own records
                 └─ IContentTransformer  provider's records → shared model (pure)
```

The source and the transformer never reference each other; only `SourceCatalog` joins them.
For mako: `Providers/Mako/Source/MakoSource` (and every HTTP, cache and parsing class) and
`Providers/Mako/Transform/MakoTransformer`, sharing only the records in
`Providers/Mako/MakoContent.cs`.

**One provider failing never empties the channel.** When the channel opens, every enabled
provider is asked at once. A provider that fails answers with its last good show list,
persisted in `show-snapshots.json`. If it has never loaded, it is left out. The channel's root
fails only when every enabled provider fails with nothing to fall back on, because an empty
answer would make Jellyfin delete every show.

## Settings

Dashboard → Plugins → IL-VOD:

- **Providers**: a checkbox per provider. A provider that is turned off is never asked
  anything, and its shows leave the channel on the next refresh. A provider added by a plugin
  update starts out on.
- **Categories**: one row per category a provider names by default, showing the shared
  category it is listed under. Type a different shared name to override that row, or leave it
  empty to keep the default. "Add a category" maps a provider category that the defaults
  don't name. The channel's categories are one closed set for every provider — ריאליטי,
  דוקומנטרי, דרמה, קומדיה, בישול, בידור, אירוח, אקטואליה, לייפסטייל — plus **אחר**: a show whose
  categories no row names, or that has none (most of mako's shows outside its genre lists), is
  listed under אחר. Jellyfin copies a show's categories only when it first creates the show, so
  a changed row reaches only shows that appear after the change.
- **mako**:
  - **Catalogue cache (hours)**: how long the genre menu and lists are kept before mako is
    asked again. The default is 24 hours; a cold open costs eight paced requests (the full show
    list, the genre menu, five genre lists and the news list).
  - **App version**: the app version reported to mako, matching what mako's own Android app
    sends.

- **רשת 13**:
  - **Catalogue cache (hours)**: how long the series list is kept before Reshet 13 is asked
    again. The default is 24 hours; a cold open costs one request (plus a login about once a
    week). A show's episode list is kept for 6 hours (config-file-only: `Reshet13.ShowCacheHours`).

- **כאן 11**:
  - **Catalogue cache (hours)**: how long the show list is kept before Kan is asked again. The
    default is 24 hours; a cold open reads the Kan BOX lobby and every genre page (about 40
    requests, four at a time). A show's episode list is kept for 6 hours (config-file-only:
    `Kan.ShowCacheHours`). Kan's show feed lists at most a show's 200 most recent episodes, so a
    daily show (or one of the few long-running series) shows its latest 200.

Changes apply on the next request; no restart is needed. mako's other cache lifetimes (show
page, past season, current season) and the stream-link lifetime are config-file-only.

## Files in the plugin's data folder

| File | What | Delete to |
|---|---|---|
| `mako-cache.json` | mako's page text and config files (genre lists 24 h, show 12 h, seasons 6 h / 7 d) | force mako to be refetched |
| `mako-device.id` | the device id mako's entitlement service sees | get a new device id |
| `reshet13-cache.json` | Reshet 13's series list and each opened show's episode list, parsed (24 h / 6 h) | force Reshet 13 to be refetched |
| `kan-cache.json` | Kan's show list and each opened show's episode list, parsed (24 h / 6 h) | force Kan to be refetched |
| `show-snapshots.json` | each provider's last good show list, used only when a provider fails | forget the fallbacks |

A resolved stream link is never written to any of these, or anywhere else on disk (N3). It
lives in memory for 5 minutes only.

## Add a library tile (manual, spec §6.5)

The plugin does not create its own library tile yet. To browse IL-VOD as an ordinary Jellyfin
library, with posters and full metadata pages, add one by hand:

1. Find the channel's metadata folder: `GET /Users/{userId}/Items/{channelId}` and read its
   `Path` field — on this server that is
   `<jellyfin config>/metadata/channels/<channelId>/metadata`.
2. Dashboard → Libraries → Add → content type **Shows** → point the folder at that path.
3. Disable every metadata fetcher and every image fetcher for the new library.
4. Save **without** scanning.
5. In the user's own Display settings, hide the "IL-VOD" channel tile so the same shows don't
   appear twice.

Jellyfin caches channel folders for 3 hours on its own. After redeploying a change that
alters what an episode item carries, bump `ContentVersion` in `IlVodChannel.cs` (and, if the
item's own shape changed, `ChannelItemMapper.EpisodeShapeRevision`), or wait.

## Remove

    SSH_HOST=<ip> SSH_USER=<user> ./scripts/uninstall.sh

## ffmpeg probe size

Jellyfin's global `-probesize 1G` makes ffmpeg buffer a whole mako episode in RAM before
Android aborts it, because mako's streams carry a data track ffmpeg can never identify (Reshet 13's
carry a `timed_id3` track, and ffmpeg kept buffering those too). The
plugin does not change the server-wide setting. Instead it wraps Jellyfin's internal transcode
manager and rewrites `-probesize` only for the command lines Jellyfin builds for streams of
providers that ask for it (`ProviderInfo.ProbeSize`: `20M` for mako and Reshet 13).
`Transcoding/ProbeSizeLimitingTranscodeManager.cs` recognises the plugin's streams by their
media source name, `il-vod/<provider>`. Every other stream is untouched. If the wrapped seam
ever disappears in a future Jellyfin release, the plugin simply stops limiting the probe size
rather than failing to load — see `docs/spec.md` §4.3.

## After the first open: run a library scan once

Jellyfin lists a genre in its Genres views only once a `Genre` entity exists for the name,
and those entities are created by a post-scan task. So after the channel's first open, the
IL-VOD tile shows the shows but the Genres view stays empty until the next library scan
(Dashboard → Scan All Libraries, or wait for the scheduled one). The scan sends nothing to any
provider.

## Known limitation

The first time a season is ever opened, it may list only some of its episodes. Reload the
page after a few seconds and the full list is there; it does not happen again for that season.
This is Jellyfin linking the new episodes in the background, and it is deliberately not worked
around (see `docs/spec.md` §10.1).
