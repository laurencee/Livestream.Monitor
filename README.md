# Livestream Monitor
A windows GUI for [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) written in .NET

The UI layout was influenced by [Skadi](https://github.com/s1mpl3x/skadi) which is a java GUI for livestreamer.

## Pre-requisites
* [.NET 4.6](https://www.microsoft.com/en-us/download/details.aspx?id=48130) installed
* [livestreamer](http://docs.livestreamer.io/install.html#windows-binaries) installed
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
