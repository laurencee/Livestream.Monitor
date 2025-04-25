using System;
using Caliburn.Micro;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.ViewModels
{
    public class MessageBoxViewModel : Screen
    {
        private string messageText;
        private bool hideOnLoadCheckboxVisible;
        private bool hideOnLoad;
        private ISettingsHandler settingsHandler;

        public MessageBoxViewModel()
        {
            DisplayName = "Livestream Monitor";
        }
        
        public bool HideOnLoadCheckboxVisibleVisible => hideOnLoadCheckboxVisible;
        
        public string MessageText
        {
            get => messageText;
            set => Set(ref messageText, value);
        }

        public bool HideOnLoad
        {
            get => hideOnLoad;
            set
            {
                if (value == hideOnLoad) return;
                hideOnLoad = value;
                if (settingsHandler != null)
                {
                    settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad = hideOnLoad;
                    settingsHandler.SaveSettings();
                }
                NotifyOfPropertyChange(() => HideOnLoad);
            }
        }

        /// <summary> Enables visibility and persisting the HideOnLoad checkbox value</summary>
        public void InitSettingsHandler(ISettingsHandler settingsHandler)
        {
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            hideOnLoadCheckboxVisible = true;
            hideOnLoad = settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad;
        }
    }
}
