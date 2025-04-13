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

* Disable notifications for individual streams, popular streams or everything
* Custom command line option for launching livestream chats for the selected stream (presets include Chrome/Firefox/Edge).
* Define your preferred stream qualities per api in a priority order
* Change your colour theme using the theme button in settings
* Copy or Open monitored stream URLs directly to your browser.

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

#### My <platform> auth token is invalid, how can I fix this?

Open `settings.json` (located in the app’s directory), delete your token, and relaunch the app to reauthorize.
