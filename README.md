# Jellyfin.Xtream

> [!NOTE]
> This is not the official Jellyfin.Xtream plugin, but a custom fork adapted to my personal usage.
> Its changes might not fit everyone's needs: for the official plugin, see
> [Kevinjil/Jellyfin.Xtream](https://github.com/Kevinjil/Jellyfin.Xtream).
>
> Many thanks to Kevin Jilissen for creating and maintaining this plugin, and for the great work
> this fork is built on.

![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/Kevinjil/Jellyfin.Xtream/total)
![GitHub Downloads (all assets, latest release)](https://img.shields.io/github/downloads/Kevinjil/Jellyfin.Xtream/latest/total)
![GitHub commits since latest release](https://img.shields.io/github/commits-since/Kevinjil/Jellyfin.Xtream/latest)
![Dynamic YAML Badge](https://img.shields.io/badge/dynamic/yaml?url=https%3A%2F%2Fraw.githubusercontent.com%2FKevinjil%2FJellyfin.Xtream%2Frefs%2Fheads%2Fmaster%2Fbuild.yaml&query=targetAbi&label=Jellyfin%20ABI)
![Dynamic YAML Badge](https://img.shields.io/badge/dynamic/yaml?url=https%3A%2F%2Fraw.githubusercontent.com%2FKevinjil%2FJellyfin.Xtream%2Frefs%2Fheads%2Fmaster%2Fbuild.yaml&query=framework&label=.NET%20framework)

The Jellyfin.Xtream plugin can be used to integrate the content provided by an [Xtream-compatible API](https://xtream-ui.org/api-xtreamui-xtreamcode/) in your [Jellyfin](https://jellyfin.org/) instance.

## Installation

The plugin can be installed using a custom plugin repository.
To add the repository, follow these steps:

1. Open your admin dashboard and navigate to `Plugins`.
1. Select the `Repositories` tab on the top of the page.
1. Click the `+` symbol to add a repository.
1. Enter `Jellyfin.Xtream` as the repository name.
1. Enter [`https://kevinjil.github.io/Jellyfin.Xtream/repository.json`](https://kevinjil.github.io/Jellyfin.Xtream/repository.json) as the repository url.
1. Click save.

To install or update the plugin, follow these steps:

1. Open your admin dashboard and navigate to `Plugins`.
1. Select the `Catalog` tab on the top of the page.
1. Under `Live TV`, select `Jellyfin Xtream`.
1. (Optional) Select the desired plugin version.
1. Click `Install`.
1. Restart your Jellyfin server to complete the installation.

## Configuration

The plugin requires connection information for an [Xtream-compatible API](https://xtream-ui.org/api-xtreamui-xtreamcode/).
The following credentials should be set correctly in the `Credentials` plugin configuration tab on the admin dashboard.

| Property                  | Description                                                                               |
| ------------------------- | ----------------------------------------------------------------------------------------- |
| Base URL                  | The URL of the API endpoint excluding the trailing slash, including protocol (http/https) |
| Username                  | The username used to authenticate to the API                                              |
| Password                  | The password used to authenticate to the API                                              |
| Max API requests per second | Optional. Limits how many API requests per second the plugin sends, to avoid provider rate limiting (HTTP 429). Set to `0` (default) to disable. See [API rate limiting](#api-rate-limiting). |

### Live TV

1. Open the `Live TV` configuration tab.
1. Select the categories, or individual channels within categories, you want to be available.
1. Click `Save` on the bottom of the page.
1. Open the `TV Overrides` configuration tab.
1. Modify the channel numbers, names, and icons if desired.
1. Click `Save` on the bottom of the page.

### Video On-Demand

1. Open the `Video On-Demand` configuration tab.
1. Enable `Show this channel to users`.
1. Select the categories, or individual videos within categories, you want to be available.
1. Click `Save` on the bottom of the page.

### Series

1. Open the `Series` configuration tab.
1. Enable `Show this channel to users`.
1. Select the categories, or individual series within categories, you want to be available.
1. Click `Save` on the bottom of the page.

### TV Catchup
1. Open the `Live TV` configuration tab.
1. Enable `Show the catch-up channel to users`.
1. Click `Save` on the bottom of the page.

#### API rate limiting

The unit is API requests per second: the plugin waits at least `1 / value` seconds between two requests to the `player_api.php` endpoint.
These are the calls that fetch categories, streams, series, VOD and EPG information.
Video playback is not affected, as streams are fetched from their own URL.

| Value          | Effect                                   |
| -------------- | ---------------------------------------- |
| `0` (default)  | No limit                                 |
| `2`            | At most two requests per second          |
| `1`            | At most one request per second           |
| `0.5`          | At most one request every 2 seconds      |
| `0.1`          | At most one request every 10 seconds     |

- Prefer the highest value your provider accepts, as every request waits its turn.
- The minimum is `0.1`, as the configuration page accepts steps of `0.1`.
- There is no maximum, but a large value such as `100` barely limits anything.
- The wait between two requests is capped at five minutes, however low the limit is set.

## Known problems

### Loss of confidentiality

Jellyfin publishes the remote paths in the API and in the default user interface.
As the Xtream format for remote paths includes the username and password, anyone that can access the library will have access to your credentials.
Use this plugin with caution on shared servers.

## Troubleshooting

### HTTP 429 Too Many Requests errors

Some providers enforce strict API rate limits.
If you see repeated `429 Too Many Requests` errors in the Jellyfin logs during guide or metadata refresh, enable rate limiting in the `Credentials` configuration tab by setting `Max API requests per second` to a value such as `1` or `0.5`.

Requests are served first come, first served, and browsing the library uses the same budget as a background refresh.
Setting the limit very low therefore means that browsing can be slow while a refresh is running, because those requests queue behind it.
Prefer the highest value your provider accepts, and note that the wait between two requests is capped at five minutes regardless of how low the limit is set.

### Networking

Make sure you have correctly configured your [Jellyfin networking](https://jellyfin.org/docs/general/networking/):

1. Open your admin dashboard and navigate to `Networking`.
2. Correctly configure your `Published server URIs`.
   For example: `all=https://jellyfin.example.com`
