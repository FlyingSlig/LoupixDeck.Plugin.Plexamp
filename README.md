# LoupixDeck.Plugin.Plexamp

Browse your Plex music library on a LoupixDeck touch screen and play albums, singles,
EPs and compilations on a local [Plexamp](https://plexamp.com/) player.

Built against [LoupixDeck.PluginSdk](https://github.com/RadiatorTwo/LoupixDeck.PluginSdk) 1.36.

Source: <https://github.com/FlyingSlig/LoupixDeck.Plugin.Plexamp>

> [!NOTE]
> **AI disclaimer:** this project was written with the help of an AI coding assistant. It has been
> built and tested against a live Plex Media Server and Plexamp player, but it comes with no
> warranty. Review the code before relying on it.

## What it does

- Connects to a Plex Media Server with your Plex token.
- Lists every artist in a music library (paged, with the first rotary encoder), with
  artist artwork on each tile.
- Opening an artist shows **Albums**, **Singles & EPs** and **Compilations**, taken from
  Plex's release-type metadata. Empty groups are omitted.
- Release tiles show album artwork with the title and year. Artwork is fetched page by
  page in the background, so lists open immediately and fill in as covers arrive.
- Tapping a release creates a server-side play queue and starts it on Plexamp, so the
  whole album plays through instead of stopping after one track.

## Setup

1. Get a Plex token: [Finding an authentication token](https://support.plex.tv/articles/204059436-finding-an-authentication-token-x-plex-token/).
2. Install the plugin folder (see [Install](#install)) and restart LoupixDeck.
3. Open **Settings → Plugins → Plexamp** and set:
   - **Server URL** — e.g. `http://192.168.1.10:32400` (or `http://127.0.0.1:32400` when
     Plex runs on the same machine).
   - **Plex token** — your `X-Plex-Token`.
   - **Music library** — optional, only needed when the server has several music
     libraries. Use the library name or its numeric ID.
   - **Plexamp URL** — defaults to `http://127.0.0.1:32500`, which is what Plexamp
     desktop and headless Plexamp use.
   - **Return to workspace after playing** — enabled by default. When an album starts,
     the browser closes and you land back on the workspace. Disable it to stay in the
     release list.
4. Press **Test Plex server** and **Test Plexamp player**.
5. Assign **Plexamp → Browse music** to a touch button.

### Album type grouping

Plex stores the primary release type in `<Format>` (`Album`, `Single`, `EP`, …) and
secondary types in `<Subformat>` (`Compilation`, `Live`, …). The plugin reads both, so
a single is grouped under **Singles & EPs** even when its format is only exposed there,
and a `Format="Album"` with `Subformat="Compilation"` lands under **Compilations**.

The types themselves come from the files' metadata (MusicBrainz release type, or local
tags such as `ALBUMTYPE=album;compilation`). Enabling **Group albums by type**
(Settings → Library in Plex Web) makes Plex expose them consistently. If Plex reports
no release type for a release, it is listed under **Albums**.

## Usage

```
Plexamp → Browse music
└── Music library (only when several exist)
    └── Artists                    any knob: page back/forward
        └── Artist
            ├── Albums (12)
            ├── Singles & EPs (4)
            └── Compilations (2)
                └── Release        tap: play on Plexamp
```

With **Return to workspace after playing** on (the default), selecting a release starts
playback and closes the browser so you are back on the workspace. With it off, the tapped
tile shows a small **Starting… / Playing / Play failed** badge, then returns to the
artwork and year. Failures are also written to the LoupixDeck log with the plugin id
`plexamp`.

All six side knobs turn pages (top/middle/bottom left and right). Only the bottom-right
knob shows the current page on its side-strip segment. The automatic Back tile is drawn
and reserved by LoupixDeck; a plugin cannot remove it from the grid or reuse that slot.

## Install

Copy the build output into a folder named after the plugin id:

| OS | Plugin folder |
|---|---|
| Linux | `~/.config/LoupixDeck/plugins/plexamp/` |
| Windows | `%AppData%\LoupixDeck\plugins\plexamp\` |
| macOS | `~/Library/Application Support/LoupixDeck/plugins/plexamp/` |

Ship `LoupixDeck.Plugin.Plexamp.dll`, `LoupixDeck.Plugin.Plexamp.deps.json` and
`LoupixDeck.Plugin.Plexamp.runtimeconfig.json`. Do **not** copy
`LoupixDeck.PluginSdk.dll` — the host provides it.

## Build

Requires the .NET 10 SDK.

```bash
dotnet build -c Release
```

The output lands in `bin/Release/`.

## How it works

- The **Plex server** side uses the regular Plex Media Server HTTP API:
  `/library/sections`, `/library/sections/{id}/all?type=8` for artists,
  `/library/sections/{id}/all?type=9&artist.id=…` for releases, and
  `/library/metadata/{id}` to read `<Format>` / `<Subformat>` release types.
- Artwork comes from Plex's photo transcoder
  (`/photo/:/transcode?width=256&height=256&format=png&url=…`) and is cached in memory,
  with a small cap so a long browsing session does not grow without bound.
- The **Plexamp** side uses the Plex Companion player API at the player's local HTTP
  port: `/player/playback/playMedia`, with a play queue created on the server through
  `POST /playQueues`. Plexamp needs every command to carry an incrementing `commandID`.
- Lists are cached for a few minutes. Saving the settings throws the cache away.

## Notes

- The Plex token is stored in the plugin's `settings.json`, like any other plugin
  setting. The settings editor masks the field, but the file is plain JSON.
- Plexamp must be reachable from the machine running LoupixDeck, and the Plex server
  address configured above must be reachable from the Plexamp machine.
- Plex Companion is an older protocol. It works with current Plexamp releases, but Plex
  may change or retire it in the future.

## AI-assisted development

This plugin was developed with the help of an AI coding agent. It has been built and tested
against a live Plex Media Server and Plexamp player, but AI-generated code can still contain
mistakes or miss edge cases. Review the source before deploying it, and treat it as provided
"as is", without warranty — see [LICENSE](LICENSE).

## License

MIT — see [LICENSE](LICENSE).
