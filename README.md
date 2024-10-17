# Livestream Monitor
A windows GUI for [streamlink](https://streamlink.github.io/) / [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) written in .NET

[Latest Release](https://github.com/laurencee/Livestream.Monitor/releases/latest)

![image](https://cloud.githubusercontent.com/assets/3850553/12476536/b701f96c-c075-11e5-8bdd-45237f94f812.png)

The UI layout was influenced by [Skadi](https://github.com/s1mpl3x/skadi) which is a java GUI for livestreamer.

## Pre-requisites
* [.NET 4.7.2](https://dotnet.microsoft.com/download/dotnet-framework/net472) installed
* [streamlink](https://github.com/streamlink/streamlink) or [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) installed and configured

## Usage
1. Run Livestream.Monitor.exe
2. Import Channels you have already followed using your stream site username (not all stream sites supported)
3. Add other channels manually
4. Double click on live channels to open the stream or click on the quality button while having a stream row selected

## General Features
* Add your favourite livestreams to be monitored from twitch or youtube
* Custom command line option for launching twitch/youtube chat for the selected stream (presets include Chrome/Firefox/Edge)
* Stream quality favorites stored per api in a priority order
* Toast notifications for streams coming online
* Toast notifications for popular streams (so you never miss a special event)
* Filter your channel list to find and manage streams quickly
* Change your colour theme using the theme button in settings


TIP: You can configure what is considered a "popular stream" in the settings menu (cog icon on right of titlebar)

## Twitch features
* Import your already followed streams for live monitoring
* Search for top streams and filter top streams by game (add for monitoring with 1 click)
* Search for VODS by streamer (you can also just paste any valid vod url and launch that instead)

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
I didn't like the large amount of memory used by java/web based GUIs for livestreamer and there were some features I wanted that the others didn't provide.

### FAQ
**Q. How do I add YouTube streams?**  
A. You can either use the username or the channelid.  
`For usernames the URL looks like this: https://www.youtube.com/user/{username}`  
`For channelIds the URL looks like this: https://www.youtube.com/channel/{channelId}`  

You can always get the channel id by clicking on the channel from the description of a video you are currently watching.

If the username happens to start with "UC" or "HC" then it will fail to add the channel. In this instance please use the channelid approach instead.

**Q. Why do some youtube stream chats show an error page when launched from Livestream Monitor?**  
A. Some YouTube streams have chat disabled (you can see this by visiting the stream page in your browser).  
I don't believe the Youtube API provides that information right now... hopefully it's something google can add in the future.

**Q. My twitch auth token is invalid, how can I fix this?**

The easiest thing to do is open `settings.json` and delete your token.   
Relaunch the app and you'll be asked to authenticate again which will generate a fresh auth token.