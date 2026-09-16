# Shoko Syoboi Calendar Plugin

A [Shoko](https://shokoanime.com/) plugin that provides Japanese TV and streaming broadcast schedules from [cal.syoboi.jp](https://cal.syoboi.jp/) (Syoboi Calendar), through the airing schedule contract (`Shoko.Abstractions.Metadata.Airing`).

## Status

This plugin builds against an **in-progress** revision of `Shoko.Abstractions` (the airing schedule contract described in the `airing-schedule-plan.md` design document), referenced by project path rather than a published NuGet package — see [Building from Source](#building-from-source). It exists to check that the design fits a real source and cannot be run against a released Shoko server yet: `IAiringScheduleService` has no server-side implementation to register the provider with, so `SyoboiAiringScheduleProvider` is never actually invoked outside of its own unit tests today. See [Known Gaps](#known-gaps) below.

## Features

- **Broadcast schedules** — Registers one airing schedule per AniDB anime and Syoboi channel (TV station or streaming service), with one episode airing per broadcast slot, converted from Japan Standard Time to UTC.
- **Channel classification** — Splits Syoboi's channel groups into television and streaming, and drops radio entirely (there is no audio-only `AiringKind`).
- **Regional channel names** — International streaming brands (Netflix, Amazon, …) are registered as Japanese regional channels (e.g. `Netflix (JP)`) instead of a bare, ambiguous name, so they line up with the same brand reported by another provider.
- **Rerun and deletion handling** — Reruns (`Flag & 0x08`, 再) and retracted entries (`Deleted=1`) are dropped before anything is written. The final-episode flag (`0x04`, 終) is kept: it is still that episode's original broadcast.
- **One-off broadcasts** — Syoboi leaves `Count` empty on a film or a TV special, since there is no episode number to state. Such a slot is pinned onto the anime's only episode when it has exactly one, and skipped otherwise.
- **Observable skips** — Every reason a refresh writes nothing — no Syoboi ID, no slots in the window, every slot retracted, a rerun, unnumbered or on a channel outside the allowed groups — is logged at Debug with the anime and title it applies to, so a legitimately empty result is never mistaken for a broken provider.
- **Delays** — Syoboi records a pushed-back slot both in `StOffset` (seconds) and in `StTime` itself, so the delayed time is submitted as the airing's `AiredAt`, the run's usual slot as its `OriginalAiredAt`, and the airing is marked delayed.
- **Rate limiting** — Every request goes through a shared limiter enforcing cal.syoboi.jp's one-request-per-second policy, with a custom `AppName (+Url)` User-Agent so the site doesn't throttle it harder.
- **Batched, bounded sweeps** — A recurring job fetches tracked anime a hundred title IDs at a time (`ProgLookup` takes a comma-separated `TID` list), five requests per execution, and then enqueues itself for the next slice instead of walking a whole library inside one job. The first slice is jittered across a five-minute window so many installs restarting together don't all hit the site at the same second, and the channel list is fetched once a day and shared across every slice.
- **Configurable scope** — Optionally restrict the sweep to anime that are airing now, about to air, or recently ended, and/or to specific Syoboi channel groups, to stay a good citizen of a small community-run site.

## How it's keyed

AniDB caches a Syoboi title ID (`AniDB_Anime.SyoboiID`) for anime it knows about, but the abstractions don't expose it as a typed field yet. This plugin recovers it by parsing `IAnidbAnime.Resources` for the cross-reference Shoko already publishes there (`https://cal.syoboi.jp/tid/{id}/time`) — see `SyoboiTitleIdResolver`. An anime with no such resource is skipped (`RefreshAsync` returns `false`).

## The Syoboi API

Everything below was checked against the live service; the unit test fixtures are responses captured from it rather than hand-written, because the first cut of this plugin was written against an assumed JSON API that does not exist.

- **Everything is XML.** `db.php` answers `text/xml` for every command. The `&JSON` query flag some clients append is silently ignored, and the separate `json.php` endpoint only serves title and channel metadata (`Req=TitleLarge`, `Req=TitleMedium`, …) — never broadcast slots — so this plugin doesn't use it.
- **One command per request.** Broadcast slots come from `Command=ProgLookup`, and the channels they refer to from `Command=ChLookup` plus `Command=ChGroupLookup`. They are separate requests; the channel directory is site-wide and cached for a day.
- **`Range` is mandatory and is a pair of timestamps**, `20210301_000000-20210401_000000`, in Japanese local time. Keyword ranges are rejected with `<Code>400</Code>`.
- **`TID` takes a comma-separated list**, for `ProgLookup` and `TitleLookup` alike, and `Fields` trims the response to the columns actually used.
- **The envelope is `Result/Code`, always under HTTP 200.** `200` is success, `404` ("条件に一致するデータは存在しません") means nothing matched, and anything else is a failure. A *successful* `ProgLookup` omits the envelope entirely, and a request with no `Command` answers with a bare `<Result>` document.
- **`StTime`/`EdTime` are `yyyy-MM-dd HH:mm:ss` in JST with no offset**, and `StTime` already includes the delay recorded in `StOffset`.
- **A refresh covers the anime's run**, its air date to its end date plus a month either side, capped at the two years up to now so a series running since 1999 doesn't ask for a quarter century at once; `ProgLookup` answers at most 5,000 rows regardless.
- **`Count` is the episode number** and `Flag` is a bit field: `0x01` 注 (notice), `0x02` 新 (first episode), `0x04` 終 (final episode), `0x08` 再 (rerun).
- **Rate limits.** One request per second for `db`, `rss`, `rss2` and `json`; clients without a custom User-Agent, or over 500 requests an hour or 10,000 a day, are slowed to one per ten seconds.

## Installation

### GUI (Recommended)

1. Open the Shoko Web UI and navigate to **Settings → Plugins → Repositories**.
2. Add the manifest URL:
   ```
   https://raw.githubusercontent.com/revam/dotnet-shoko-plugin-syoboi/stable/manifest.json
   ```
3. Go to **Settings → Plugins → Browse** and find **Syoboi Calendar**.
4. Click **Install** on the desired version.
5. Restart Shoko.

### Manual

1. Download the latest `Shoko.Plugin.Syoboi-<version>-any.zip` from the [Releases](../../releases) page.
2. Extract the ZIP and place `Shoko.Plugin.Syoboi.dll` into your Shoko **Plugins** folder.
3. Restart Shoko.

## Configuration

The plugin exposes the following settings in the Shoko UI:

| Setting | Default | Description |
|---|---|---|
| **Only Sweep Currently Relevant Anime** | `true` | Restrict the recurring sweep to anime that are airing now, about to air, or recently ended. |
| **Upcoming Window (Days)** | `30` | How many days before an anime's known air date it starts being swept. |
| **Recently Ended Window (Days)** | `90` | How many days after an anime's end date it keeps being swept. |
| **Sweep Interval (Hours)** | `168` (weekly) | How often the recurring sweep job runs. |
| **Allowed Channel Groups** | *(empty = all)* | Restrict tracking to specific Syoboi channel groups (`ChGroupName`), e.g. `テレビ 関東`, `BSデジタル`, `インターネット` or `AbemaTV`. Radio is always excluded regardless of this list. |
| **User-Agent App Name** | `Shoko.Plugin.Syoboi` | Sent as the product token of the `AppName/version (+url)` User-Agent cal.syoboi.jp asks clients to identify themselves with. Anything that isn't valid in an HTTP token is stripped. |
| **User-Agent Contact URL** | *(Shoko's repository)* | Sent as the comment part of the User-Agent. |

## Architecture

| Piece | Responsibility |
|---|---|
| `Plugin` | `IPlugin` entry point; registers services, the `HttpClient`, and the recurring sweep job. |
| `Configuration` | Settings, as described above. |
| `Http.SyoboiRateLimiter` | Enforces the one-request-per-second policy across every caller. |
| `Http.SyoboiUserAgent` | Normalises the configured app name and contact URL into a well-formed `AppName/version (+url)` header. |
| `Http.SyoboiApiClient` | Issues the `db.php` requests — `ProgLookup` per batch of titles, `ChLookup`/`ChGroupLookup` for the channel directory, `TitleLookup` on demand. |
| `Http.SyoboiChannelDirectoryCache` | Keeps the site-wide channel directory between requests, so a sweep doesn't re-fetch it per title. |
| `Http.SyoboiResponseParser` | Converts the XML wire format into typed records, honouring the `Result/Code` envelope (404 is "no data", anything else non-200 is a failure). |
| `Mapping.SyoboiTitleIdResolver` | Recovers an anime's Syoboi title ID from its resources. |
| `Mapping.SyoboiTimeConverter` | Converts Syoboi's JST, no-zone timestamps to UTC, and UTC windows back into a `Range` parameter. |
| `Mapping.SyoboiProgramFilter` | Decides which broadcast entries are worth keeping (not a rerun, not deleted, and numbered unless the anime has a single episode). |
| `Mapping.SyoboiChannelGroupClassifier` | Classifies a channel group as television, streaming, or radio (dropped). |
| `Mapping.SyoboiChannelNaming` | Resolves the display name to register a channel under, regionalising a handful of international streaming brands. |
| `Mapping.SyoboiScheduleMapper` | Pure grouping/mapping logic turning a lookup result into per-channel bundles and per-episode drafts, free of any `IAiringScheduleService` or AniDB entity dependency so it's cheap to unit test. |
| `SyoboiAiringScheduleProvider` | The `IAiringScheduleProvider<Configuration>` implementation; turns a lookup result into `FindOrRegisterChannel`/`AddOrUpdateSchedule`/`SetAirings`/`LinkAirings` calls. |
| `Jobs.SyoboiSweepJob` | Recurring job that sweeps locally known, currently relevant AniDB anime with a Syoboi title ID, 500 anime per execution, continuing from the last AniDB anime ID it covered. |

## Known Gaps

- **The airing schedule contract is still in progress.** `Shoko.Abstractions.Metadata.Airing` and its server-side implementation are being built alongside this plugin, so the contract can still change under it. Only the pure parsing, mapping and rate-limiting logic is unit tested here; the write path is exercised by running the plugin against a server.
- **Channel group classification is a heuristic.** `SyoboiChannelGroupClassifier` matches on markers in the group name (`ラジオ` → dropped, `インターネット`/`配信`/`Abema`/`ニコニコ` → streaming, everything else → television) rather than on the 28 known `ChGID` values, so a group Syoboi adds later still classifies sensibly. The marker list is tested against the real group names.
- **No typed Syoboi title ID.** `IAnidbAnime` doesn't expose `SyoboiID` directly; `SyoboiTitleIdResolver` parses it back out of `Resources`. A typed field on the abstraction, mentioned as a possible follow-up in the airing schedule plan, would remove this entirely.

## Building from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and a checkout of [ShokoServer](https://github.com/ShokoAnime/ShokoServer) alongside this repository (i.e. `../Shoko` relative to this repository's parent directory), since `Shoko.Plugin.Syoboi.csproj` references `Shoko.Abstractions` by project path rather than by package while the airing schedule contract is still in progress. Switch that reference back to a `PackageReference` once a release carrying it ships.

```bash
dotnet build Shoko.Plugin.Syoboi.slnx --configuration Release
dotnet test tests/Shoko.Plugin.Syoboi.Tests.csproj
```

The compiled assembly will be located at `source/bin/Release/net10.0/Shoko.Plugin.Syoboi.dll`.

## License

This project is licensed under the MIT License.
