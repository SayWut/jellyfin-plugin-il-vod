# Jellyfin mako plugin — prototype design

Date: 2026-09-20. Status: approved in conversation, awaiting written review.

## 1. What this is

A Jellyfin **Channel** plugin, written in C#, that shows mako 12+ VOD inside
Jellyfin without downloading or pre-indexing anything. One plugin serves one
provider; this one serves mako. It is a separate project in this repository,
under `jellyfin-plugin-mako/`. It is not part of `stack/`: `stack/deploy.sh`
does not deploy it, and nothing in `stack/` refers to it.

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
| Open a show | 1 (the show page: season list + current season's episodes) |
| Open a season | 1, or 0 if it arrived with the show page |
| Press play | 1 playlist request, plus 1 token request if the item is protected |

Opening a show never fetches all of its seasons. The plugin has no scheduled
task and no background work, and does not implement `ISupportsLatestMedia`
(that feature makes Jellyfin walk the root folder and every folder in it by
itself — verified in `ChannelManager.RefreshLatestChannelItems`, v12.1).

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

**N6 — Clean code.** All code follows the `clean-code` skill (§7).

## 3. Approach: two steps, with a stop rule

**Step A — static plugin.** No mako calls. Proves that a home-built plugin
loads, that the Channels view is acceptable on the three apps, and that Jellyfin
calls the plugin on click.

**Step B — live mako under one pinned show.** Starts only if step A passes.
Proves on-demand fetching, caching, the C# port of the AES and token flow, and
play-time URL resolution.

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
- Play returns the URL stored in the plugin's one setting, "Test stream URL"
  (Dashboard → Plugins → Mako → Settings). The user pastes a freshly signed URL
  from `mako12_vod.py --resolve` before testing. It cannot be compiled in,
  because the token expires. Every episode plays that one URL.
- Every call from Jellyfin writes one log line: which folder was opened, or
  which item was played.

### 4.2 Checks

| # | Check | Web | Android TV | Fladder |
|---|---|---|---|---|
| 1 | Plugin is Active on 12.1 after a restart | server, once | | |
| 2 | The mako channel is visible and opens | ☐ | ☐ | ☐ |
| 3 | Show → seasons → episodes browse; names and poster show | ☐ | ☐ | ☐ |
| 4 | Log shows a plugin call only when a folder is opened; none while idle | log, once | | |
| 5 | Play works with the pasted URL; note direct play vs transcode | ☐ | ☐ | ☐ |
| 6 | Seek works; stop and resume continues from the same place | ☐ | ☐ | ☐ |
| 7 | Reopening a folder within 3 hours makes no plugin call | log, once | | |
| 8 | A genre filter is offered on the channel's show list | ☐ | ☐ | ☐ |

Check 8 does not gate step B. Its result decides the full plugin's channel
layout: a flat list with a genre filter if the filter exists, otherwise genre
folders and shows together on the channel page.

## 5. Step B — live mako under one pinned show

The pinned show is configured by its mako page path (default
`/mako-vod-keshet/shemesh`). There is no catalogue, genre list or search. The
"Test stream URL" setting is removed.

### 5.1 Structure

```
jellyfin-plugin-mako/
├── README.md
├── docs/spec.md, docs/plan.md
├── build.yaml
├── Jellyfin.Plugin.Mako.sln
├── src/Jellyfin.Plugin.Mako/
│   ├── Plugin.cs                 plugin identity and settings
│   ├── MakoChannel.cs            Jellyfin-facing: folder opened → items; play → URL
│   ├── Catalog/
│   │   ├── ICatalog.cs           contents of a folder; stream link of an episode
│   │   ├── StaticCatalog.cs      step A
│   │   ├── MakoCatalog.cs        step B: mako answers → Show / Season / Episode
│   │   └── CachedCatalog.cs      wraps an ICatalog; answers from cache when fresh
│   ├── Mako/
│   │   ├── MakoHttpClient.cs     the only class that talks to mako: headers, pacing, challenge detection
│   │   ├── MakoPages.cs          page JSON → seasons, episodes (parsing only)
│   │   ├── MakoConfig.cs         the three config files: AES keys, app id, endpoints
│   │   ├── PayloadCipher.cs      AES-192-CBC encrypt / decrypt
│   │   └── StreamResolver.cs     playlist → decrypt → token if protected → final URL
│   ├── Cache/TimedCache.cs       key → value with expiry; knows nothing about mako
│   └── Model/                    Show, Season, Episode, StreamLink
├── tests/Jellyfin.Plugin.Mako.Tests/
│   └── fixtures/                 saved mako responses
└── scripts/build.sh, install.sh, uninstall.sh
```

`MakoChannel` reaches content only through `ICatalog`. Step B swaps the
catalogue behind that interface; the Jellyfin-facing code does not change.
`ICatalog` is shaped so that both channel layouts in §4.2 check 8 fit it.

### 5.2 Behaviour taken from the API notes

- Browse URLs carry `platform=mobileApp`; headers are a mobile `User-Agent` and
  `Accept-Language: he-IL`.
- Season number is parsed from `seasonTitle`, never from the URL slug.
- Episodes are the `allEpisodes` tab only. Blank placeholder entries are
  skipped. Episode number is the 1-based position in the list. The air date is
  parsed from `extraInfo`.
- The playlist body is Base64 AES-192-CBC, IV `theExact16Chars=`, key `note1_n`
  with `note1_o` as fallback. The token body uses key `note3`.
- The media entry with the highest `cdnLB` is used (`cdnLB` is a string).
- If `protectedContent` is false no token is requested. Otherwise the decoded
  ticket is appended with `?` or `&`.
- The device id is synthesized once and kept in the plugin's data folder.
- App version (`43.0`) is one setting.

### 5.3 Caching

Layer 1 is Jellyfin's own: it remembers an opened folder for 3 hours and does
not call the plugin (`ChannelManager.CacheLength`, v12.1). Layer 2 is the
plugin's:

| What | Kept for |
|---|---|
| Config files (keys, app id) | 24 h; refetched once if a decrypt fails |
| A show's season list | 12 h |
| Episodes of a past season | 7 days |
| Episodes of the current season | 6 h |
| A resolved stream URL | 5 min |

Durations live in one settings class. The cache is persisted to a file in the
plugin's data folder so a Jellyfin restart does not cause a wave of refetches.
Resolved stream URLs are held in memory only.

### 5.4 Failure handling

- **Bot challenge** (redirect to `validate.perfdrive.com`): serve the stale
  cached copy if there is one, otherwise an empty folder; log it; do not call
  mako again for a few minutes.
- **Entitlement denied:** playback fails; the log names the item and the
  `caseId`.
- **Decrypt failure:** try the fallback key, then refetch the config once, then
  fail with a typed error.
- Errors are typed (`BotChallengeException`, `EntitlementDeniedException`,
  `PayloadDecryptException`) and never swallowed.

### 5.5 Checks

1. Opening the pinned show lists real seasons and episodes with correct numbers
   and Hebrew titles, on all three apps.
2. The log shows exactly the requests in N1's table, and none overnight.
3. Reopening the show causes no request to mako.
4. A protected episode plays on all three apps; seek and resume work.
5. Pause 20 minutes, then resume. If this fails, the full plugin needs a
   re-sign-on-reopen path.

## 6. Testing

Only the manual checks above call mako. Everything else runs offline:

| Unit | Test |
|---|---|
| `PayloadCipher` | decrypt a saved real playlist body; round-trip encrypt/decrypt |
| `MakoPages` | saved show and season pages: season numbers, placeholders, shorts tab, air date |
| `StreamResolver` | fake HTTP: protected, unprotected, denied, key fallback |
| `TimedCache`, `CachedCatalog` | fake clock; expiry; stale-on-challenge |
| On-demand rule (N1) | fake HTTP that counts requests per user action |
| `MakoChannel` | folder id in → items out, over `StaticCatalog` |

Fixtures are copied from `_analysis` into `tests/fixtures/` after checking they
contain no device or account identifier. `.device_id` is never copied.

## 7. Code standard

The `clean-code` skill applies to every task: one job per class; names that say
what a thing does; small functions; no magic values (URLs, IV, headers and
durations each live in one named place); typed errors with a reason; tests
written first and readable as a description of the behaviour. A task is not done
until it meets this.

## 8. Build and install

- `scripts/build.sh` — `dotnet publish`, then assembles the plugin folder with
  its `meta.json`.
- `scripts/install.sh` — takes the phone's address the way `stack/deploy.sh`
  does, copies the folder into Jellyfin's plugins directory, asks before
  restarting Jellyfin, then reads the plugin's status from Jellyfin's API and
  prints Active, Not Supported or Malfunctioned.
- `scripts/uninstall.sh` — removes the folder and restarts Jellyfin.

The scripts touch nothing on the phone except that one plugin folder.

## 9. Risks

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

## 10. Out of scope for the prototype

The full show list, genres, search, movies, other providers, a plugin
repository for catalogue install, and any setting beyond the cache durations
and the pinned show. Each is designed after step B passes.
