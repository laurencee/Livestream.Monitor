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
* Add your favourite livestreams to be monitored from twitch/youtube*/hitbox
* Open twitch/youtube/hitbox chat for the selected stream (requires chrome)
* Ability to choose quality of stream (before opening the stream)
* Toast notifications for streams coming online
* Toast notifications for popular streams (so you never miss a special event)
* Filter your channel list to find and manage streams quickly
* Change your colour theme using the theme button in settings

TIP: You can configure what is considered a "popular stream" in the settings menu (cog icon on right of titlebar)


*Youtube currently linked to individual videos instead of the channels, this will be fixed in the future: [#9](https://github.com/laurencee/Livestream.Monitor/issues/9)


## Twitch/hitbox features
* Import your already followed streams for live monitoring
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
**Q. How do I add YouTube streams?** <br />
A. You need to copy the video id from the url <br />
(the characters after '**watch?v=**' in: https://www.youtube.com/watch?v=knUZi4NkduA)

**Q. Why do some youtube stream chats show an error page when launched from Livestream Monitor?** <br />
A. Some YouTube streams have chat disabled (you can see this by visiting the stream page in your browser). Currently I don't know how to find out if chat is disabled/enabled through the YouTube API

<form action="https://www.paypal.com/cgi-bin/webscr" method="post" target="_top">
<input type="hidden" name="cmd" value="_s-xclick">
<input type="hidden" name="encrypted" value="-----BEGIN PKCS7-----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-----END PKCS7-----
">
<input type="image" src="https://www.paypalobjects.com/en_AU/i/btn/btn_donateCC_LG.gif" border="0" name="submit" alt="PayPal — The safer, easier way to pay online.">
<img alt="" border="0" src="https://www.paypalobjects.com/en_AU/i/scr/pixel.gif" width="1" height="1">
</form>
