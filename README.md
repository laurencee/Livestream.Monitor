# Livestream Monitor
A windows GUI for [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) written in .NET

[Latest Release](https://github.com/laurencee/Livestream.Monitor/releases/latest)

![image](https://cloud.githubusercontent.com/assets/3850553/10718577/2c29d320-7bc2-11e5-8360-49464f16e6b0.png)

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

## Other Features
* Ability to choose quality of stream (before opening the stream)
* Open twitch chat for the selected stream
* Toast notifications for streams coming online
* Filter your channel list to find streams quickly
* Change your colour theme using the theme button in the top right

### TODO
* Logging... (much lazyness)
* Settings screen to enable/disable notifications & configure hardcoded executable lookup locations

## Suggested livestreamer configuration
1. Edit your livestreamer [configuration file](http://docs.livestreamer.io/cli.html#configuration-file) (%APPDATA%\livestreamer\livestreamerrc) and uncomment your preferred video player
2. Change the number of threads when streaming HLS/HDS to 4
```
# Number of threads to use when streaming HLS streams
hls-segment-threads=4

# Number of threads to use when streaming HDS streams
hds-segment-threads=4
```
