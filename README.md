# Livestream Monitor
A windows GUI for [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) written in .NET

[Latest Release](https://github.com/laurencee/Livestream.Monitor/releases/latest)

![image](https://cloud.githubusercontent.com/assets/3850553/12476536/b701f96c-c075-11e5-8bdd-45237f94f812.png)

The UI layout was influenced by [Skadi](https://github.com/s1mpl3x/skadi) which is a java GUI for livestreamer.

## Pre-requisites
* [.NET 4.6](https://www.microsoft.com/en-us/download/details.aspx?id=48130) installed
* [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) installed and configured
* Chrome browser installed (app runs fine without chrome but chrome is required for chat to function)

## Usage
1. Run Livestream.Monitor.exe
2. Import Channels you have already followed using your twitch username (no authentication required)
3. Add other channels manually
4. Double click on live channels to open the stream or click on the quality button while having a stream row selected

## General Features
* Add your favourite livestreams to be monitored from twitch/youtube
* Open twitch/youtube chat for the selected stream (requires chrome)
* Ability to choose quality of stream (before opening the stream)
* Toast notifications for streams coming online
* Toast notifications for popular streams (so you never miss a special event)
* Filter your channel list to find streams quickly
* Change your colour theme using the theme button in the top right

TIP: You can configure what is considered a "popular stream" in the settings menu (cog icon on right of titlebar)

## Twitch features
* Import your already followed streams from twitch for live monitoring
* Search for top streams and filter top streams by game (add for monitoring with 1 click)
* Search for VODS by streamer and launch vods through livestreamer (you can also just paste any valid vod url and launch that instead)

## Suggested livestreamer configuration
1. Edit your livestreamer [configuration file](http://docs.livestreamer.io/cli.html#configuration-file) (%APPDATA%\livestreamer\livestreamerrc) and uncomment your preferred video player
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
Q. How do I add YouTube streams?
A. You need to copy the video id from the url (the characters after '**watch?v=**' in: https://www.youtube.com/watch?v=knUZi4NkduA)

Q. When I launch chat for a YouTube stream it shows an error page, why?
A. Some YouTube streams have chat disabled (you can see this by visiting the stream page in your browser). Currently I don't know how to find out if chat is disabled/enabled through the YouTube API
