using System;
using System.Collections.Generic;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model;

namespace Livestream.Monitor.ViewModels
{
    public class ThemeSelectorViewModel : Screen
    {
        #region Design time constructor
        public ThemeSelectorViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
            
        }

        #endregion

        public ThemeSelectorViewModel(ISettingsHandler settingsHandler)
        {
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));

            foreach (MetroThemeBaseColour themeBaseColour in Enum.GetValues(typeof(MetroThemeBaseColour)))
            {
                var menuItem = new MenuItem(() => settingsHandler.Settings.MetroThemeBaseColour = themeBaseColour)
                {
                    Name = themeBaseColour.ToString().ToFriendlyString()
                };
                BaseThemes.Add(menuItem);
            }

            foreach (MetroThemeAccentColour themeAccentColour in Enum.GetValues(typeof(MetroThemeAccentColour)))
            {
                var menuItem = new MenuItem(() => settingsHandler.Settings.MetroThemeAccentColour = themeAccentColour)
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
