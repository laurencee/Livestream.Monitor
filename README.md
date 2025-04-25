# Livestream Monitor
A Windows GUI to monitor livestreams from multiple platforms written in .NET.  
Supports **Twitch**, **YouTube** and **Kick**.

Download: [Latest Release](https://github.com/laurencee/Livestream.Monitor/releases/latest)  
[![v](https://img.shields.io/github/v/release/laurencee/Livestream.Monitor?label=)](https://github.com/laurencee/Livestream.Monitor/releases/latest)

Leverages [streamlink](https://streamlink.github.io/) / [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) to launch livestreams through your own video player (VLC, MPC-HC, etc.). 

![image](https://cloud.githubusercontent.com/assets/3850553/12476536/b701f96c-c075-11e5-8bdd-45237f94f812.png)

The UI layout was influenced by [Skadi](https://github.com/s1mpl3x/skadi) which is a Java GUI for livestreamer.

## Pre-requisites
* [.NET 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472) installed
* [streamlink](https://github.com/streamlink/streamlink) or [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) installed and configured

## Usage
1. Download the latest release and extract anywhere you want.
2. Run `Livestream.Monitor.exe`.
3. If you already follow channels on Twitch, use Import to import any channels you already follow. (*)
4. Add other channels manually. (*)
5. Double-click on live channels to open the stream or click on the quality button while a stream row is selected.

(*) For Twitch, you'll be required to authorize the app the first time you try to import/add/query 

## General Features
* Add your favourite livestreams to be monitored from **Twitch**, **YouTube** or **Kick** by their channel names (end part of the url)
  * Import your existing Twitch follow list via the Import button to get up and running quickly.
* View **Top Streams** for Twitch and Kick. Filter Twitch top streams by category (Kick support coming soon).
* **VOD viewer** to see previously recorded broadcasts/uploads for supported platforms.
  * You don't have to be monitoring channels to view their VODs.
* Filter your channel list to find and manage streams quickly
* Toast notifications for streams coming online
* Toast notifications for popular streams (so you never miss a special event)

**TIP**: You can configure what is considered a "popular stream" in the settings menu (cog icon on right of titlebar)

### Customizations

Tweak the app to your liking with some of the following settings

* Custom FilePath/Args independently for stream/vod/chat for every platform via `settings.json` (launch app 1 time to create file)
* Disable notifications for individual streams, popular streams or everything
* Define your preferred stream qualities per api in a priority order
* Change your colour theme using the theme buttons in settings
* Copy to clipboard or Open stream URLs directly to your browser.

#### settings.json details
If you need to see the full FilePath + Args after replacement being launched, set `DebugMode` to true.

The only other settings you may need to manually edit are the various [Platform configurations](#platform-configurations).

##### Platform configurations
All support platforms will have their own configuration sections for configuring their stream/vod/chat commands and any platform specific settings.

The `FilePaths` entry allows both full paths and shortened paths, e.g. `C:\Program Files\Streamlink\bin\streamlink.exe` and `streamlink`, and should work with anything you can launch via Win+R (Run).

FilePaths and Args support replacement tokens, all of them have `{url}`. Twitch specifically supports `{auth_token}`.

## Suggested streamlink/livestreamer configuration
1. Edit your streamlink [configuration file](https://streamlink.github.io/cli.html#configuration-file) and set your preferred video player
2. Change the number of threads when streaming HLS/HDS to 4
```
# Number of threads to use when streaming HLS streams
hls-segment-threads=4

twitch-low-latency
twitch-disable-ads
hls-live-edge=1
```

### Why make another livestreamer GUI?
I wanted a lighter weight GUI with additional features that supported more platforms.

### FAQ

#### How do I add YouTube channels for livestream monitoring?

You can either use the handle or the channel id, handle is preferred.  
- For handles, the URL looks like: `https://www.youtube.com/@handle` (e.g. `@MrBeast`)  
- For channel IDs, the URL looks like: `https://www.youtube.com/channel/channel_id` (e.g. `UCX6OQ3DkcsbYNE6H8uQQuVA`)  

#### How can I watch YouTube vods?

As of `2025-04-25` you can use MPC-HC (such as from KLite Codec Pack) and [yt-dlp](https://github.com/yt-dlp/yt-dlp/releases). 

`yt-dlp` just needs to be available from your `PATH` or dropped in the MPC-HC directory.

VLC use to be able to play YouTube vods out of the box but does not as of `2025-04-25`.

#### My <platform> auth token is invalid, how can I fix this?

Open `settings.json` (located in the app’s directory), delete your token, and relaunch the app to reauthorize.
