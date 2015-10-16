using System;
using System.Collections.Generic;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;

namespace Livestream.Monitor.ViewModels
{
    public class ThemeSelectorViewModel : Screen
    {
        public ThemeSelectorViewModel()
        {
            foreach (MetroThemeBaseColour themeBaseColour in Enum.GetValues(typeof(MetroThemeBaseColour)))
            {
                var menuItem = new MenuItem(() => MetroWindowManager.ChangeTheme(themeBaseColour))
                {
                    Name = themeBaseColour.ToString().ToFriendlyString()
                };
                BaseThemes.Add(menuItem);
            }

            foreach (MetroThemeAccentColour themeAccentColour in Enum.GetValues(typeof(MetroThemeAccentColour)))
            {
                var menuItem = new MenuItem(() => MetroWindowManager.ChangeTheme(themeAccentColour))
                {
                    Name = themeAccentColour.ToString().ToFriendlyString()
                };
                AccentColours.Add(menuItem);
            }
        }

        public List<MenuItem> BaseThemes { get; } = new List<MenuItem>();

        public List<MenuItem> AccentColours { get; } = new List<MenuItem>();
    }
}
