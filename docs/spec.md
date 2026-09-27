# Jellyfin mako plugin — prototype design

Date: 2026-09-20; step B section revised 2026-09-21 after step A; step C added 2026-09-23. Status: steps A and B done; step C built, awaiting install (C6).

## 1. What this is

A Jellyfin **Channel** plugin, written in C#, that shows mako 12+ VOD inside
Jellyfin without downloading or pre-indexing anything. One plugin serves one
provider; this one serves mako. It is its own repository. It began under
`jellyfin-plugin-mako/` in oneplus-media-server and moved out on 2026-09-28, with its history.
It is not part of that repository's `stack/`: `stack/deploy.sh` does not deploy it,
and nothing in `stack/` refers to it.

This document covers the **prototype only**. The prototype exists to answer one
question before any larger build: does a Channel plugin do what is needed, on
the three apps that matter, on this server?

Server: Jellyfin 12.1 under Termux on the phone. Plugin target: `targetAbi`
12.1, .NET 10 (verified: the `Jellyfin.Controller` 12.1.0 package targets
`net10.0`). The laptop has only the .NET 8 SDK, so the .NET 10 SDK is installed
first.

mako's API is documented in `~/Downloads/12+/_analysis/mako_api.md`, and a
working Python client exists beside it (`mako12_vod.py`). The plugin re-implements
that client's behaviour in C#; it does not call the Python code.

## 2. The needs

These are the requirements the design is judged against.

**N1 — On demand.** Data is fetched because a user did something, never
otherwise.

| User action | Requests to mako |
|---|---|
| Nothing: server idle, Jellyfin scheduled tasks, library scans | 0 |
| Open the channel | 0 while the catalogue cache is fresh; else 6 (genre menu + 5 genre lists), paced — step C, §6.2 |
| Open a show | 1 (the show page: season list + current season's episodes) |
| Open a season | 1, or 0 if it arrived with the show page |
| Press play | 1 playlist request, plus 1 token request if the item is protected (and 2 to the CDN: the master playlist and its best variant, which is the only place an episode's length can be measured — without a length Jellyfin treats the stream as live) |

Opening a show never fetches all of its seasons. The plugin has no scheduled
task and no background work, and does not implement `ISupportsLatestMedia`
(that feature makes Jellyfin walk the root folder and every folder in it by
itself — verified in `ChannelManager.RefreshLatestChannelItems`, v12.1).

Jellyfin also opens folders by itself once shows are `Series`/`Season`: every
new season is queued for a metadata refresh, which asks the channel for that
season's episodes (`SeasonMetadataService` → `Season.GetEpisodes`, v12.1). Those
queries carry no user id; a person's click always does. The channel refuses any
query without a user before it touches the catalogue — by throwing, never with
an empty list, because Jellyfin deletes the items an answer lacks.

**N2 — Cached.** Opening the same show, season or channel again does not call
mako while the cached copy is fresh. Shows change rarely, so the copies live
for hours to days (§5.3).

**N3 — Play-time URLs.** A stream URL is resolved when play is pressed. mako's
token lasts 15 minutes, so a resolved URL is never stored beyond a few minutes.

**N4 — Gentle.** `www.mako.co.il` sits behind Radware bot protection. At most
one request to mako is in flight, requests are spaced, and a bot challenge
causes a back-off, not a retry loop.

**N5 — Honest failure.** When mako refuses an item ("subscription required"),
playback fails with a clear log line. The plugin uses the normal `media`
stream the app uses and does not work around refusals.

**N6 — Clean code.** All code follows the `clean-code` skill (§8).

## 3. Approach: three steps, with a stop rule

**Step A — static plugin.** No mako calls. Proves that a home-built plugin
loads, that the Channels view is acceptable on the three apps, and that Jellyfin
calls the plugin on click.

**Step B — live mako under one pinned show.** Starts only if step A passes.
Proves on-demand fetching, caching, the C# port of the AES and token flow, and
play-time URL resolution.

**Step C — the catalogue by genre.** Starts only if step B passes. Replaces the
one pinned show with every show mako files under a real genre, and gives the
channel a genre view in the apps (§6).

**Stop rule.** If the channel cannot be found or browsed (step A checks 2–3) on
an app that matters, work stops before any mako code is written, and the
fallback is the Xtream adapter with the `Jellyfin-Xtream-Library` plugin.
Playback failures (checks 5–6) are investigated first; they may be fixable in
how the plugin describes the stream.

The three apps: Jellyfin web, the official Jellyfin app on Android TV, and
Fladder on Android.

## 4. Step A — static plugin

### 4.1 Content

- One channel, "mako".
- Two hard-coded shows with **different genres**. The first has two seasons
  with a few episodes each, all playable. The second is a card only; it exists
  to test whether the apps offer a genre filter on a channel's list.
- Shows carry name, poster URL, description and genres. Episodes carry season
  number, episode number and title.
- Play returns the URL and container stored in the plugin's two settings,
  "Test stream URL" and "Test stream container" (Dashboard → Plugins → Mako →
  Settings). The user pastes a freshly signed URL from `mako12_vod.py
  --resolve` before testing. It cannot be compiled in, because the token
  expires. Every episode plays that one URL. The container setting exists
  because Jellyfin 12.1 refuses direct play for the `hls` container, so a
  hard-coded container would pre-decide check 5's answer; leaving it empty
  lets Jellyfin decide, and setting it to `hls` forces the manifest path.
- Every call from Jellyfin writes one log line: which folder was opened, or
  which item was played.

### 4.2 Checks

| # | Check | Web | Android TV | Fladder |
|---|---|---|---|---|
| 1 | Plugin is Active on 12.1 after a restart | server, once | | |
| 2 | The mako channel is visible and opens | ☐ | ☐ | ☐ |
| 3 | Show → seasons → episodes browse; names and poster show | ☐ | ☐ | ☐ |
| 4 | Log shows a plugin call only when a folder is opened; none while idle | log, once | | |
| 5 | Play works with the pasted URL; note direct play vs transcode — once with the container empty, once with `hls` | ☐ | ☐ | ☐ |
| 6 | Seek works; stop and resume continues from the same place | ☐ | ☐ | ☐ |
| 7 | Reopening a folder within 3 hours makes no plugin call | log, once | | |
| 8 | A genre filter is offered on the channel's show list | ☐ | ☐ | ☐ |

Check 8 does not gate step B. Its result decides the full plugin's channel
layout: a flat list with a genre filter if the filter exists, otherwise genre
folders and shows together on the channel page.

### 4.3 Probe-size limiter

Jellyfin 12.1 adds `-probesize 1G` (the global `FFmpeg:probesize` setting) to
every ffmpeg command it runs; `EncodingHelper` reads only that global value, so
a plugin cannot set a per-stream probe size through any public API. mako's HLS
segments carry an unidentifiable sparse data PID, and while it stays
unidentified ffmpeg keeps buffering everything it reads, bounded only by
probesize — at 1G that means the whole episode ends up in RAM and Android's
allocator aborts the process. The plugin instead wraps Jellyfin's
`ITranscodeManager` (`Transcoding/ProbeSizeLimitingTranscodeManager.cs`,
installed by `Transcoding/TranscodeManagerDecoration.cs`) and rewrites the
`-probesize` value in the ffmpeg command line to `20M`, but only when the
`MediaSourceInfo` being started is one this plugin created
(`MediaSourceFactory.IsMakoSource`); every other stream's command line passes
through unchanged. Recognition checks more than the name: the source must
also be a remote HTTP source, so a local library file that happens to be
named "mako" (Jellyfin names a file's media source after its file name) is
never mistaken for one of the plugin's own streams. Every path in the
decorator fails open: if recognising or rewriting the stream throws, or if
Core's `ITranscodeManager` registration does not look the way the plugin
expects, the original arguments and the original registration are used
respectively — a mistake here must never keep a non-mako stream from playing.
This covers the transcode command only: Jellyfin's ffprobe path
(`MediaEncoder.GetMediaInfo`) builds its own `-probesize` from the same
global setting and is not covered by this decorator; it is not reached today
because the plugin already supplies `MediaStreams` and sets
`RequiresOpening = false`, so Jellyfin never needs to probe a mako source.

This hooks a Jellyfin implementation detail, not a published extension point,
so it costs future stability: a Jellyfin update can change or remove the seam
without warning. If that happens `TranscodeManagerDecoration.Apply` finds
nothing to wrap, returns `false`, and the plugin loads exactly as before —
`ProbeSizeLimiterStatus.IsInstalled` stays `false` and `MakoChannel` logs a
warning on every play so the gap is visible in Jellyfin's log instead of
surfacing only as unexplained playback crashes.

## 5. Step B — live mako under one pinned show

Step A passed its gate on 2026-09-21 (`docs/step-a-results.md`). Step B replaces
the static catalogue with live mako calls for **one pinned show**, configured by
its mako page path (default `/mako-vod-keshet/shemesh`). There is no catalogue,
genre list or search yet. The two step A test settings ("Test stream URL", "Test
stream container") are removed.

### 5.0 What step A settled, and step B keeps

- Shows are `ChannelFolderType.Series`, seasons `ChannelFolderType.Season`. Jellyfin's
  TV endpoints (`/Shows/{id}/Seasons`, `/Shows/{id}/Episodes?seasonId=`) reach the
  plugin on demand for channel items, and remote metadata fetchers are disabled for
  channel items, so mako's Hebrew titles stay.
- Every episode carries a `MediaSourceType.Placeholder` source with a non-existent
  file path, so Jellyfin reports `LocationType: FileSystem` (Fladder plays nothing
  else). Jellyfin drops placeholder sources at play time.
- The real media source leaves `Id` empty, names itself `mako`, is remote HTTP with
  container `hls` (Jellyfin only adds the `aac_adtstoasc` filter its fMP4 output
  needs when the source container is `ts`/`aac`/`hls`), and sets `AnalyzeDurationMs`.
- The probe-size limiter (§4.3) stays.
- Jellyfin 12.1 never direct-plays an HLS manifest; every play goes through ffmpeg
  on the phone. The goal is a stream **copy**, not a re-encode (§5.2).

### 5.1 Structure

```
src/Jellyfin.Plugin.Mako/
├── Plugin.cs, PluginServiceRegistrator.cs, MakoChannel.cs
├── Configuration/                PinnedShowPath, cache durations, app version
├── Model/                        Show, Season, Episode, StreamLink (+ StreamDescription)
├── Browsing/                     FolderId, ChannelItemMapper, MediaSourceFactory
├── Transcoding/                  probe-size limiter (unchanged)
├── Catalog/
│   ├── ICatalog.cs
│   └── MakoCatalog.cs            mako answers → Show / Season / Episode / StreamLink
├── Mako/
│   ├── IMakoHttp.cs / MakoHttpClient.cs   the only class that talks to mako: headers, pacing, challenge detection
│   ├── MakoUrls.cs               the page-URL template rule and the playlist URL template
│   ├── MakoConfig.cs / MakoConfigLoader.cs  the three config files: endpoints, app id, AES keys
│   ├── MakoPageSource.cs         a page's JSON text, from the cache when fresh, else from mako; stale copy on a bot challenge
│   ├── MakoPages.cs              page JSON → seasons, episodes, show overview (parsing only)
│   ├── EpisodeKey.cs             the three playback ids packed into one episode id
│   ├── PayloadCipher.cs          AES-192-CBC encrypt / decrypt
│   ├── StreamResolver.cs         playlist → decrypt → token if protected → final URL
│   ├── MasterPlaylist.cs         HLS master text → resolution, bandwidth, codecs
│   └── DeviceIdentity.cs         the synthesized device id, created once and kept
└── Cache/
    ├── IClock.cs / SystemClock.cs
    └── TimedCache.cs             key → value with expiry and stale reads; knows nothing about mako
```

`StaticCatalog` and its two settings are deleted. `MakoChannel` does not change
shape: it still reaches content only through `ICatalog`.

### 5.2 Behaviour

Browse:
- Browse URLs use the config's `baseUrl` template (`https://www.mako.co.il%PAGE_URL%&platform=mobileApp`;
  if the result has no `?`, the first `&` becomes `?`). Headers: a mobile
  `User-Agent` and `Accept-Language: he-IL,he;q=0.9,en;q=0.8`.
- The channel root lists one show — the pinned one. Its name comes from a setting
  (default `שמש`): mako's show page carries no reliable title (`channelName` is the
  current season's name). Its overview is `hero.description`, its poster `hero.pics[0].picUrl`.
- Opening the show fetches the show page once: `seasons[]` gives every season
  (`seasonTitle`, `pageUrl`), and the page's own `menu` holds the **current** season's
  episodes. The show page has the same shape as a season page, so it is also stored in
  the page cache under the current season's path: opening that season costs nothing.
- Season number is the integer in `seasonTitle` ("עונה 6" → 6), never the URL slug.
- Episodes are the `vods[]` of the tab whose `id` is `allEpisodes`. Entries with
  `componentLayout == "ad"` (mako interleaves ad slots) or without the three playback
  ids are skipped. Episode number is the integer after "פרק" in `extraInfo`
  ("פרק 3@08.01.98" → 3); when absent, the 1-based position among real episodes. The
  air date is the `dd.MM.yy` after `@` (two-digit years 70–99 → 19xx, otherwise 20xx).
  The thumbnail is `pics[0].picUrl`.
- An episode's id packs what playback needs — `itemVcmId`, `channelId`,
  `galleryChannelId` — so play needs no lookup and survives a restart.

Play:
- Playlist URL from the core config's template (`%VCM_ID%`, `%CHANNEL_ID%`,
  `%GALLERY_ID%`, `%CONSUMER%` = `mobileApp`). The body is Base64 of AES-192-CBC,
  IV `theExact16Chars=`, key `note1_n`, falling back to `note1_o`.
- Media entry with the highest `cdnLB` (a string in mako's JSON). The normal `media`
  list is used, as the app does — not `mediaClean`.
- `videoDetails.extraParams.protectedContent` false → the URL is final. True → POST
  the encrypted token body (`da` app id, `du` device id, `et` "ngt", `na` app version,
  `rv` cdn, `lp` url, `dv` vcm id) to the token URL with key `note3`; the decoded
  `tickets[0].ticket` is appended with `?` or `&`. An empty `tickets` means mako
  refused: playback fails, the log carries the `caseId`, nothing is worked around.
- **Stream description.** The master playlist is fetched from the CDN (not
  `www.mako.co.il`, so it is outside the bot protection) and parsed: the highest
  bandwidth variant's resolution and bitrate, and the H.264 profile and level from
  its `CODECS` attribute when present. With a full description Jellyfin can copy
  the video instead of re-encoding it. When `CODECS` is absent (it is on Shemesh),
  the description falls back to H.264 Main — what mako's streams were measured to
  be — and the step B check records whether Jellyfin copies.
- mako's segment URLs carry their own 24-hour token minted from the 15-minute one,
  so playback and long pauses outlive the master token.

Identity: the device id is `A1` + 23 hex digits, synthesized once and kept in the
plugin's data folder. The app version (`43.0`) is a setting.

### 5.3 Caching

Layer 1 is Jellyfin's own: it remembers an opened folder for 3 hours and does
not call the plugin (`ChannelManager.CacheLength`, v12.1). Layer 2 is the
plugin's, and it caches mako's **page text** by page path (parsing is cheap and pure):

| What | Kept for |
|---|---|
| Config files (keys, app id) | 24 h; refetched once if a decrypt fails |
| A show's page (season list, overview) | 12 h |
| Episodes of a past season | 7 days |
| Episodes of the current season | 6 h |
| A resolved stream link | 5 min, in memory only |

Durations live in one settings class. The browse cache is persisted to a file in
the plugin's data folder so a Jellyfin restart does not cause a wave of refetches.
Resolved stream links never reach disk. (Jellyfin itself also caches the resolved
source per episode for 5 minutes.)

### 5.4 Failure handling

- **Bot challenge** (redirect to `validate.perfdrive.com`): serve the stale
  cached copy if there is one, otherwise fail the folder; log it; do not call
  `www.mako.co.il` again for a few minutes.
- **A failed browse is never answered with an empty list.** Jellyfin 12.1 takes
  any answer as the folder's full contents: it caches it for 3 hours and deletes
  the items it holds that the answer lacks. The failure is rethrown instead, so
  nothing is cached or deleted and the next open asks again.
- **Entitlement denied:** playback fails; the log names the item and the `caseId`.
- **Decrypt failure:** try the fallback key, then refetch the config once, then
  fail with a typed error.
- Errors are typed (`BotChallengeException`, `EntitlementDeniedException`,
  `PayloadDecryptException`, `MakoRequestException`) and never swallowed silently.
- At most one request to `www.mako.co.il` is in flight, and consecutive requests
  are spaced (N4).
- **A page or config that is not valid JSON is a browse failure and is never
  cached.** mako answers an HTML error page with status 200; opening the folder
  fails (see above), and nothing that failed to parse is
  written to the browse cache — so the next open asks mako again instead of
  replaying the same rubbish for the rest of the lifetime.
- **Constructing the channel touches nothing.** Jellyfin builds every `IChannel`
  during `ApplicationHost.InitializeServices`, before `CreatePlugins` has set
  `Plugin.Instance` — so the constructor may not read the data folder, the
  configuration, or anything built from them. An exception there does not disable
  the plugin; it stops the server from starting.

### 5.5 Checks

1. Opening the pinned show lists real seasons and episodes with correct numbers,
   Hebrew titles, thumbnails and air dates, on all three apps.
2. The log shows exactly the requests in N1's table, and none while idle.
3. Reopening the show or a season causes no request to mako.
4. A protected episode plays on all three apps; seek and stop/resume work
   (carried over from step A, where they were never reported).
5. Pause 20 minutes, then resume.
6. Whether Jellyfin copies the video stream or re-encodes it (ffmpeg log), and
   ffmpeg's memory while playing.
7. First open of a never-opened season: how long until episodes appear (step A saw
   an empty list for a few seconds while Jellyfin linked new items).
8. Carried over: a genre filter on the show list; the Android TV "two pages" quirk.

## 6. Step C — the catalogue by genre

Step B passed its checks on 2026-09-22 (`PROGRESS.md`, "Step B live findings").
Step C replaces the one pinned show with **every show mako files under a real
genre**, each carrying its genres, so the apps can show mako as a Shows library
with one row per genre. The pinned-show settings are removed.

### 6.0 What the research settled (2026-09-22/23)

- A channel plugin controls nothing about layout. Inside one channel the only
  folders Fladder can open are Series and Season; plain channel folders arrive as
  `ChannelFolderItem`, which Fladder has no screen for. Genre folders inside the
  channel are therefore out. One channel per genre works everywhere but puts a
  tile per genre on the home screen, which the user rejected. Collections
  (BoxSets) can hold channel shows but no app renders them as rows.
- **A real `tvshows` library whose single media path is the channel's own
  metadata folder** makes every app render the channel as a Shows library, with
  the web's Genres tab, Android TV's Genres button and Fladder's genre rows.
  Jellyfin resolves that path to the Channel entity, so every library query lands
  on the channel's items; the scanner ignores folders named `metadata`, so it
  never touches them. Verified on the server on 2026-09-22 (`/UserViews`,
  `/Items?parentId=<lib>`, `/Genres?parentId=<lib>`, `/Users/{id}/Items/Latest`,
  `/Shows/{id}/Seasons` all answer from the channel) and by the user in the apps.
  The library was created by hand through the API with every metadata and image
  fetcher disabled and no scan; the plugin does not create it yet (§6.5).
- Jellyfin copies a show's genres, description and poster URL **only when it
  first creates the item**; a poster is added later only if the item has none.
  `DataVersion` only invalidates Jellyfin's 3-hour answer cache — items live on by
  their ids. To give an existing show genres, its id must change.
- mako's sources: the show index (654 shows: name, page path, poster; no genre, no
  description); the genre menu (five real genres — ריאליטי, דוקומנטרי, דרמה,
  קומדיה, בישול — plus three provider groups, an A–B list and playlists, which are
  dropped); one list per genre, same item shape as the index. The show and season
  pages carry no show name. The paced burst — index, menu and every menu entry,
  12 requests at 750 ms — passed twice in a row from the phone with the plugin's
  HTTP stack, no challenge.
- Jellyfin downloads each poster once, in the item's queued metadata refresh, and
  serves it from disk afterwards; there is no proxied mode.

### 6.1 Structure

```
src/Jellyfin.Plugin.Mako/
├── Mako/MakoIndex.cs             genre menu JSON → the real genres; a genre list JSON → (path, name, poster) entries (parsing only)
├── Mako/MakoIndexPaths.cs        the menu's page path, and the rule that tells a real genre's list from the rest
├── Catalog/MakoCatalog.cs        GetShowsAsync answers from the genre lists; GetEpisodesAsync names the show from them
├── Catalog/CatalogueLifetimes.cs replaces CacheLifetimes; adds the catalogue's lifetime (PinnedShow.cs deleted)
├── Browsing/FolderId.cs          new prefixes; a season id carries its show's path
├── Configuration/                PinnedShowPath/PinnedShowName removed; CatalogueCacheHours added
└── Model/Show.cs                 unchanged shape: Id, Name, PosterUrl, Overview, Genres
```

The menu and the lists are ordinary mako pages (`/mako-vod-index?filter=…`
passes `MakoUrls.Page`'s rules), so `MakoPageSource` fetches, caches, coalesces
and stale-serves them unchanged — there is no new source class. `MakoChannel`
(apart from the season folder's two-part id), `MakoPageSource`, `MakoPages`,
`StreamResolver` and everything under `Transcoding/` do not change.

### 6.2 Behaviour

- **Opening the channel** (root folder, by a user): the catalogue loads the genre
  menu, keeps the entries whose URL says `filter=genre`, loads each genre's list,
  and answers one `Show` per distinct page path with the name and poster from
  the first list that named it and the genres of every list that named it. Order:
  as mako lists them, first genre first. No description (§6.4).
- **A season id** is `<showPath>|<seasonPath>`, so `GetEpisodesAsync` can name the
  show for its episodes by looking the path up in the cached lists; an unknown
  path gives episodes with no series name rather than a failure.
- **Folder id prefixes** change (`series|` → `mako-series|`, `series-season|` →
  `mako-season|`) so Jellyfin creates every show afresh, with its genres, and
  deletes the old pinned-show items itself as "no longer in the answer";
  `DataVersion` bumps so Jellyfin's answer cache is not consulted.
- **N1 becomes:** opening the channel costs 1 (menu) + 5 (genres) requests when
  the catalogue cache is cold, paced, ≈ 5 s; 0 while it is fresh. The rest of the
  table is unchanged. Nothing is fetched ahead of a user opening the channel.
- **Failure:** a bot challenge or request failure with a stale copy of every
  needed list serves the stale copies; with any list missing it fails the folder
  (never an empty answer, §5.4). A list that is not valid JSON is a failure and is
  never cached. A genre list that fails while others succeed fails the whole
  answer — a partial catalogue would make Jellyfin delete the missing shows. A
  menu that names no genre, or a genre list that names no show, is a failure too
  and is never cached — mako's real genres are never empty.

### 6.3 Caching

| What | Kept for |
|---|---|
| Genre menu and each genre's list | `CatalogueCacheHours`, default 24 h, persisted with the browse cache |

Everything in §5.3 stays. After the first open, a day costs mako one burst.

### 6.4 Descriptions — deliberately not fetched

The lists carry none, the show page does (`hero.description`), but Jellyfin
copies a description only at creation, and fetching 654 show pages to have it
ready at creation would be 654 requests. Shows have no description in step C.

### 6.5 The library tile — manual in step C

The `tvshows` library over the channel's metadata folder is created by hand
(Dashboard or `POST /Library/VirtualFolders`, fetchers off, `refreshLibrary=false`).
Having the plugin create it idempotently at startup, and hide the channel's own
tile per user, is a later step. The channel keeps working without the library.

### 6.6 Jellyfin-side cost, accepted

The first open creates ~430 Series items (five genres, overlapping), queues a
metadata refresh per item (the plugin refuses the folder walk each queues, §2),
and downloads each poster once from `img.mako.co.il` in the background (≈ 50–100
MB, once). Opening the channel the first time is slow once; afterwards Jellyfin
answers from its database.

### 6.7 Checks

1. The "mako test" library tile lists the shows with posters on all three apps;
   the Genres view shows five rows.
2. The log shows the burst of six requests on the first open and none on the
   second; none while idle.
3. A show from each genre opens to its seasons, a season to its episodes, and an
   episode plays.
4. Reopening the channel the next day makes exactly one burst.
5. Jellyfin's log shows no metadata fetcher and no image fetcher running for
   mako items (the library was created with them off).

## 7. Testing

Only the manual checks above call mako. Everything else runs offline:

| Unit | Test |
|---|---|
| `PayloadCipher` | decrypt a saved real playlist body; round-trip encrypt/decrypt |
| `MakoPages` | saved show and season pages: season numbers, placeholders, shorts tab, air date |
| `StreamResolver` | fake HTTP: protected, unprotected, denied, key fallback |
| `TimedCache`, `MakoPageSource` | fake clock; expiry; stale-on-challenge |
| On-demand rule (N1) | fake HTTP that counts requests per user action |
| `MakoChannel` | folder id in → items out, over `StaticCatalog` |

Fixtures are copied from `_analysis` into `tests/fixtures/` after checking they
contain no device or account identifier. `.device_id` is never copied.

## 8. Code standard

The `clean-code` skill applies to every task: one job per class; names that say
what a thing does; small functions; no magic values (URLs, IV, headers and
durations each live in one named place); typed errors with a reason; tests
written first and readable as a description of the behaviour. A task is not done
until it meets this.

## 9. Build and install

- `scripts/build.sh` — `dotnet publish`, then assembles the plugin folder with
  its `meta.json`.
- `scripts/install.sh` — takes the phone's address the way `stack/deploy.sh`
  does, copies the folder into Jellyfin's plugins directory, asks before
  restarting Jellyfin, then reads the plugin's status from Jellyfin's API and
  prints Active, Not Supported or Malfunctioned.
- `scripts/uninstall.sh` — removes the folder and restarts Jellyfin.

The scripts touch nothing on the phone except that one plugin folder.

## 10. Risks

1. Channels look poor or are hidden on one of the three apps — step A finds out.
2. A .NET 10 plugin built on the laptop fails to load under Termux — step A
   check 1.
3. Resume after a long pause fails once the token has expired — step B check 5.
4. Jellyfin transcodes the HLS stream instead of playing it directly, which the
   phone handles badly — watched in step A check 5; the plugin can describe the
   stream's container and codecs to steer this.
5. mako changes its app API or rotates keys — keys come from mako's config at
   runtime and the app version is one setting; a breaking change needs a code
   fix.
6. A Jellyfin update changes the Channel interfaces — rebuild; the
   Jellyfin-facing code is two small classes.
7. A Jellyfin update changes or removes the `ITranscodeManager` registration
   the probe-size limiter (§4.3) decorates — it fails open and simply stops
   limiting the probe size; playback correctness for mako then depends on the
   global setting again until the decorator is updated.

### 10.1 Known limitation, accepted

A season opened for the very first time lists only some of its episodes; a
page reload a few seconds later shows all of them. Jellyfin answers from its own
database, filtered by a series key that a new episode only gets when its queued
metadata refresh has run. The only plugin-side fix is decorating Jellyfin's
`IChannelManager`; that cost does not match a once-per-season, 3-second cosmetic
gap, so it is not fixed (decided 2026-09-22; details in `PROGRESS.md`).

## 11. Out of scope for the prototype

Search (Jellyfin's own search covers show names once the shows exist), movies,
other providers, a plugin repository for catalogue install, show descriptions
(§6.4), the plugin creating its own library tile (§6.5), and any setting beyond
the cache durations and the app version.
