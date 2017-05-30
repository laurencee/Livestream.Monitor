# Livestream Monitor
A windows GUI for [streamlink](https://streamlink.github.io/) / [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) written in .NET

[Latest Release](https://github.com/laurencee/Livestream.Monitor/releases/latest)

![image](https://cloud.githubusercontent.com/assets/3850553/12476536/b701f96c-c075-11e5-8bdd-45237f94f812.png)

The UI layout was influenced by [Skadi](https://github.com/s1mpl3x/skadi) which is a java GUI for livestreamer.

## Pre-requisites
* [.NET 4.6](https://www.microsoft.com/en-us/download/details.aspx?id=48130) installed
* [streamlink](https://github.com/streamlink/streamlink) or [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) installed and configured

## Usage
1. Run Livestream.Monitor.exe
2. Import Channels you have already followed using your stream site username (not all stream sites supported)
3. Add other channels manually
4. Double click on live channels to open the stream or click on the quality button while having a stream row selected

## General Features
* Add your favourite livestreams to be monitored from twitch, youtube, mixer or smashcast
* Custom command line option for launching twitch/youtube/hitbox chat for the selected stream (presets include Chrome/Firefox/Edge)
* Stream quality selection options
* Toast notifications for streams coming online
* Toast notifications for popular streams (so you never miss a special event)
* Filter your channel list to find and manage streams quickly
* Change your colour theme using the theme button in settings


TIP: You can configure what is considered a "popular stream" in the settings menu (cog icon on right of titlebar)

## Twitch/smashcast features
* Import your already followed streams for live monitoring
* Search for top streams and filter top streams by game (add for monitoring with 1 click)
* Search for VODS by streamer (you can also just paste any valid vod url and launch that instead)

## Suggested streamlink/livestreamer configuration
1. Edit your streamlink [configuration file](https://streamlink.github.io/cli.html#configuration-file) and set your preferred video player
2. Change the number of threads when streaming HLS/HDS to 4
```
# Number of threads to use when streaming HLS streams
hls-segment-threads=4

# Number of threads to use when streaming HDS streams
hds-segment-threads=4
```

### Why make another livestreamer GUI?
I didn't like the large amount of memory used by java/web based GUIs for livestreamer and there were some features I wanted that the others didn't provide.

### FAQ
**Q. How do I add YouTube streams?**  
A. You need to provide the username/channel name. You can find this by clicking on a users channel link to get to their profile page.  
(The URL looks like this: https://www.youtube.com/user/{username})

**Q. Why do some youtube stream chats show an error page when launched from Livestream Monitor?**  
A. Some YouTube streams have chat disabled (you can see this by visiting the stream page in your browser).  
I don't believe the Youtube API provides that information right now... hopefully it's something google can add in the future.
