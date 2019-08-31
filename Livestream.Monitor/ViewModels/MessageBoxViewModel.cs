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
            get { return messageText; }
            set
            {
                if (value == messageText) return;
                messageText = value;
                NotifyOfPropertyChange(() => MessageText);
            }
        }

        public bool HideOnLoad
        {
            get { return hideOnLoad; }
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

        /// <summary> Shows the hide on load checkbox requiring a settings handler to bind the checkbox to </summary>
        public void ShowHideOnLoadCheckbox(ISettingsHandler settingsHandler)
        {
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
            hideOnLoadCheckboxVisible = true;
            hideOnLoad = settingsHandler.Settings.HideStreamOutputMessageBoxOnLoad;
        }
    }
}
