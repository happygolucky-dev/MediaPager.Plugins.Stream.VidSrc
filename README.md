# MediaPager.Plugins.Stream.VidSrc

Community stream provider for [MediaPager](https://github.com/nobugsgiven). Implements the
plugin SDK (`MediaPager.App.PluginContracts` → `IStreamProviderPlugin`). This is **not an
official plugin** — it is authored by the community and published under its own account.

## What it does

Resolves a MediaPager `ResolveAsync` request (IMDb or TMDB id, TV season/episode optional)
into an HLS playback URL by driving the embed page headlessly:

1. Queries the provider data API (`status_code` must be `200`).
2. Requests the host's versioned browser-automation capability to open the embed, block
   tracker/anti-automation URLs, hide `navigator.webdriver`, and click the player's
   big-play button until the `/master.m3u8` request is captured. The plugin receives only
   the matching URL, never a Playwright object or browser process handle.
3. Fetches the master playlist through the host broker and selects the highest-bandwidth
   standalone variant with an explicit 1080p resolution when one is listed. If the playlist
   cannot be fetched or has no usable standalone 1080p variant, the plugin returns the original
   master URL so the host's existing HLS playback path remains available.

The plugin only ever returns a raw upstream URI. Validation (SSRF guard), session minting,
and HLS proxying for playback are the host's job per the SDK contract.

## Runtime prerequisite

When VidSrc is enabled, the host provisions the Chromium revision matched to its pinned
Playwright package into `Tools:Directory/playwright/<version>` (Docker: `/tools`; desktop:
the user's `.MediaPager/tools` cache). Tool files remain under the app-managed cache and
are not installed into the host's system directories.

Linux installations still need the native Chromium shared libraries used by Playwright.
The Docker image includes these; desktop Linux packages must provide the corresponding
system dependencies. If the browser cannot start, the host reports an actionable error.

The generic Download action is supplied by the separate community Downloads plugin; VidSrc
only provides stream resolution. Its manifest installs the Downloads plugin and the community
download-interface package as dependencies, but VidSrc does not implement transfer code.

## Settings

Defined by `IPluginSettingsSchema` and stored under `plugins.vidsrc.*`:

| Key         | Default      | Meaning                         |
|-------------|--------------|---------------------------------|
| `host`      | `vidsrc.sh`  | Embed host                      |
| `userAgent` | Chrome 154   | Browser user agent for the page |

## Build

```sh
dotnet build
```

References the SDK locally via a relative project reference; swap it for the SDK package once
published.

## Tests

```sh
dotnet test tests/MediaPager.Plugins.Stream.VidSrc.Tests/MediaPager.Plugins.Stream.VidSrc.Tests.csproj
```
