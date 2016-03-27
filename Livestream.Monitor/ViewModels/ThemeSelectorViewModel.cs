using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.Model;

namespace Livestream.Monitor.ViewModels
{
    public class ThemeSelectorViewModel : Screen
    {
        private readonly ISettingsHandler settingsHandler;

        #region Design time constructor
        public ThemeSelectorViewModel()
        {
            if (!Execute.InDesignMode)
                throw new InvalidOperationException("Constructor only accessible from design time");
            
        }

        #endregion

        public ThemeSelectorViewModel(ISettingsHandler settingsHandler)
        {
            this.settingsHandler = settingsHandler;
            if (settingsHandler == null) throw new ArgumentNullException(nameof(settingsHandler));

            foreach (MetroThemeBaseColour themeBaseColour in Enum.GetValues(typeof(MetroThemeBaseColour)))
            {
                var menuItem = new MenuItem(() => settingsHandler.Settings.MetroThemeBaseColour = themeBaseColour)
                {
                    Name = themeBaseColour.ToString().ToFriendlyString()
                };

                var savedBaseColour = settingsHandler.Settings.MetroThemeBaseColour.ToString();
                menuItem.IsChecked = savedBaseColour.IsEqualTo(menuItem.Name.Replace(" ", string.Empty));
                BaseThemes.Add(menuItem);
            }

            foreach (MetroThemeAccentColour themeAccentColour in Enum.GetValues(typeof(MetroThemeAccentColour)))
            {
                var menuItem = new MenuItem(() => settingsHandler.Settings.MetroThemeAccentColour = themeAccentColour)
                {
                    Name = themeAccentColour.ToString().ToFriendlyString(),
                };

                var savedAccecentColour = settingsHandler.Settings.MetroThemeAccentColour.ToString();
                menuItem.IsChecked = savedAccecentColour.IsEqualTo(menuItem.Name);
                AccentColours.Add(menuItem);
            }
        }

        public List<MenuItem> BaseThemes { get; } = new List<MenuItem>();

        public List<MenuItem> AccentColours { get; } = new List<MenuItem>();

        protected override void OnActivate()
        {
            settingsHandler.Settings.PropertyChanged += SettingsOnPropertyChanged;
            base.OnActivate();
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(settingsHandler.Settings.MetroThemeAccentColour))
            {
                AccentColours.ForEach(x => x.IsChecked = false);
                var savedAccentColour = settingsHandler.Settings.MetroThemeAccentColour.ToString();
                var menuItem = AccentColours.FirstOrDefault(x => x.Name.ReverseFriendlyString().IsEqualTo(savedAccentColour));
                if (menuItem != null)
                    menuItem.IsChecked = true;
            }
            else if (e.PropertyName == nameof(settingsHandler.Settings.MetroThemeBaseColour))
            {
                BaseThemes.ForEach(x => x.IsChecked = false);
                var savedBaseColour = settingsHandler.Settings.MetroThemeBaseColour.ToString();
                var menuItem = BaseThemes.FirstOrDefault(x => x.Name.ReverseFriendlyString().IsEqualTo(savedBaseColour));
                if (menuItem != null)
                    menuItem.IsChecked = true;
            }
        }
    }
}
