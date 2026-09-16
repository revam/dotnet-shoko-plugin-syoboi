# Shoko Syoboi Calendar Plugin

A [Shoko](https://shokoanime.com/) plugin that provides Japanese TV and streaming broadcast schedules from [cal.syoboi.jp](https://cal.syoboi.jp/) (Syoboi Calendar), through the airing schedule contract (`Shoko.Abstractions.Metadata.Airing`).

## Status

This plugin builds against an **in-progress** revision of `Shoko.Abstractions` (the airing schedule contract described in the `airing-schedule-plan.md` design document), referenced by project path rather than a published NuGet package — see [Building from Source](#building-from-source). It exists to check that the design fits a real source and cannot be run against a released Shoko server yet: `IAiringScheduleService` has no server-side implementation to register the provider with, so `SyoboiAiringScheduleProvider` is never actually invoked outside of its own unit tests today. See [Known Gaps](#known-gaps) below.

## Features

- **Broadcast schedules** — Registers one airing schedule per AniDB anime and Syoboi channel (TV station or streaming service), with one episode airing per broadcast slot, converted from Japan Standard Time to UTC.
- **Channel classification** — Splits Syoboi's channel groups into television and streaming, and drops radio entirely (there is no audio-only `AiringKind`).
- **Regional channel names** — International streaming brands (Netflix, Amazon, …) are registered as Japanese regional channels (e.g. `Netflix (JP)`) instead of a bare, ambiguous name, so they line up with the same brand reported by another provider.
- **Rerun and deletion handling** — Reruns (Syoboi's rerun flag) and retracted entries (`Deleted=1`) are dropped before anything is written.
- **Rate limiting** — Every request goes through a shared limiter enforcing cal.syoboi.jp's one-request-per-second policy, with a custom `AppName (+Url)` User-Agent so the site doesn't throttle it harder.
- **Batched sweeps** — A recurring job fetches every tracked anime's schedule in a single request (Syoboi supports looking up many title IDs at once), jittered across a five-minute window so many installs restarting together don't all hit the site at the same second.
- **Configurable scope** — Optionally restrict the sweep to anime that are airing now, about to air, or recently ended, and/or to specific Syoboi channel groups, to stay a good citizen of a small community-run site.

## How it's keyed

AniDB caches a Syoboi title ID (`AniDB_Anime.SyoboiID`) for anime it knows about, but the abstractions don't expose it as a typed field yet. This plugin recovers it by parsing `IAnidbAnime.Resources` for the cross-reference Shoko already publishes there (`https://cal.syoboi.jp/tid/{id}/time`) — see `SyoboiTitleIdResolver`. An anime with no such resource is skipped (`RefreshAsync` returns `false`).

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
| **Allowed Channel Groups** | *(empty = all)* | Restrict tracking to specific Syoboi channel groups (`ChGName`), e.g. to skip niche streaming services. Radio is always excluded regardless of this list. |
| **User-Agent App Name** | `Shoko.Plugin.Syoboi` | Sent as the product token of the `AppName (+Url)` User-Agent cal.syoboi.jp asks clients to identify themselves with. |
| **User-Agent Contact URL** | *(Shoko's repository)* | Sent as the comment part of the User-Agent. |

## Architecture

| Piece | Responsibility |
|---|---|
| `Plugin` | `IPlugin` entry point; registers services, the `HttpClient`, and the recurring sweep job. |
| `Configuration` | Settings, as described above. |
| `Http.SyoboiRateLimiter` | Enforces the one-request-per-second policy across every caller. |
| `Http.SyoboiApiClient` | Issues `db.php?Command=ProgLookup` requests and hands the raw JSON to the parser. |
| `Http.SyoboiResponseParser` | Converts the (string-typed) wire format into typed records, tolerant of a missing or malformed section. |
| `Mapping.SyoboiTitleIdResolver` | Recovers an anime's Syoboi title ID from its resources. |
| `Mapping.SyoboiTimeConverter` | Converts Syoboi's JST, no-zone timestamps to UTC. |
| `Mapping.SyoboiProgramFilter` | Decides which broadcast entries are worth keeping (not a rerun, not deleted, has an episode number). |
| `Mapping.SyoboiChannelGroupClassifier` | Classifies a channel group as television, streaming, or radio (dropped). |
| `Mapping.SyoboiChannelNaming` | Resolves the display name to register a channel under, regionalising a handful of international streaming brands. |
| `Mapping.SyoboiScheduleMapper` | Pure grouping/mapping logic turning a lookup result into per-channel bundles and per-episode drafts, free of any `IAiringScheduleService` or AniDB entity dependency so it's cheap to unit test. |
| `SyoboiAiringScheduleProvider` | The `IAiringScheduleProvider<Configuration>` implementation; turns a lookup result into `FindOrRegisterChannel`/`AddOrUpdateSchedule`/`SetAirings`/`LinkAirings` calls. |
| `Jobs.SyoboiSweepJob` | Recurring job that finds every locally known, currently relevant AniDB anime with a Syoboi title ID and sweeps them all in one request. |

## Known Gaps

- **No server-side `IAiringScheduleService` yet.** The airing schedule contract in `Shoko.Abstractions.Metadata.Airing` exists, but Shoko.Server doesn't implement or register it yet (there's no `AiringScheduleService`, and `PluginManager` doesn't discover `IAiringScheduleProvider`s the way it does `IReleaseInfoProvider`s). This plugin can't be exercised end-to-end until that lands; only the pure mapping and rate-limiting logic is unit tested today.
- **The Syoboi JSON envelope is unverified.** `Http.SyoboiLookupResponseDto` is transcribed from the airing schedule plan's description of the API (`ProgLookup`/`ChLookup`/`ChGroupLookup`, string-typed fields) rather than a live response — this sandbox has no outbound network access. `SyoboiResponseParser` is deliberately tolerant of a missing or renamed section (it degrades to an empty result instead of throwing), but the exact top-level key names, the `Range` keyword accepted by the API, and whether `ChLookup`/`ChGroupLookup` are actually returned alongside a plain `ProgLookup` request should be checked against the real API before relying on this in production.
- **No typed Syoboi title ID.** `IAnidbAnime` doesn't expose `SyoboiID` directly; `SyoboiTitleIdResolver` parses it back out of `Resources`. A typed field on the abstraction, mentioned as a possible follow-up in the airing schedule plan, would remove this entirely.
- **Channel group classification is a heuristic.** `SyoboiChannelGroupClassifier` matches on substrings of the group name (`ラジオ`, `ネット`/`配信`, defaulting to television) rather than known `ChGID` values, since those weren't verified either. It's unit tested against the group names quoted in the airing schedule plan and a few plausible variants.

## Building from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and a checkout of [ShokoServer](https://github.com/ShokoAnime/ShokoServer) alongside this repository (i.e. `../Shoko` relative to this repository's parent directory), since `Shoko.Plugin.Syoboi.csproj` references `Shoko.Abstractions` by project path rather than by package while the airing schedule contract is still in progress. Switch that reference back to a `PackageReference` once a release carrying it ships.

```bash
dotnet build Shoko.Plugin.Syoboi.slnx --configuration Release
dotnet test tests/Shoko.Plugin.Syoboi.Tests.csproj
```

The compiled assembly will be located at `source/bin/Release/net10.0/Shoko.Plugin.Syoboi.dll`.

## License

This project is licensed under the MIT License.
