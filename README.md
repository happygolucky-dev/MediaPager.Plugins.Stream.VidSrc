# MediaPager.Plugins.Stream.VidSrc

Community stream provider for [MediaPager](https://github.com/nobugsgiven). Implements the
plugin SDK (`MediaPager.App.PluginContracts` → `IStreamProviderPlugin`). This is **not an
official plugin** — it is authored by the community and published under its own account.

## What it does

Resolves a MediaPager `ResolveAsync` request (IMDb or TMDB id, TV season/episode optional)
into the provider's HLS master playlist URL, by driving the embed page headlessly:

1. Queries the provider data API (`status_code` must be `200`).
2. Opens the embed in Playwright Chromium, blocks tracker/anti-automation scripts,
   fakes `navigator.webdriver`, and clicks the player's big-play button until the
   `/master.m3u8` request is captured.

The plugin only ever returns the raw upstream URI. Validation (SSRF guard), session minting,
and HLS proxying for playback are the host's job per the SDK contract.

Its Download action delegates to the community `IDownloadProviderPlugin` contract. That
download provider is a separate plugin and must be loaded first; VidSrc does not contain or
own download-transfer code.

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
