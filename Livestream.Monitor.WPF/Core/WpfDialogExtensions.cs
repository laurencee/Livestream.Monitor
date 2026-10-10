using System;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace Livestream.Monitor.WPF.Core
{
    public static class WpfDialogExtensions
    {
        /// <summary> Safe implementation to show messages from viewmodels </summary>
        public static async Task<MessageDialogResult> ShowMessageAsync(this IViewAware screen, string title, string message,
            MessageDialogStyle messageDialogStyle = MessageDialogStyle.Affirmative,
            MetroDialogSettings dialogSettings = null)
        {
            return await screen.GetMetroWindowFromScreen().ShowMessageAsync(title, message, messageDialogStyle, dialogSettings);
        }

        public static async Task<string> ShowDialogAsync(this IViewAware screen, string title, string message,
            MetroDialogSettings dialogSettings = null)
        {
            return await screen.GetMetroWindowFromScreen().ShowInputAsync(title, message, dialogSettings);
        }

        public static async Task<ProgressDialogController> ShowProgressAsync(this IViewAware screen, string title, string message,
            bool isCancellable = false,
            MetroDialogSettings dialogSettings = null)
        {
            return await screen.GetMetroWindowFromScreen().ShowProgressAsync(title, message, isCancellable, dialogSettings);
        }

        private static MetroWindow GetMetroWindowFromScreen(this IViewAware screen)
        {
            var uiElement = screen?.GetView() as DependencyObject;
            if (uiElement == null)
                throw new InvalidOperationException($"The view for {typeof(Screen).Name} is not a dependency object");

            var metroWindow = Window.GetWindow(uiElement) as MetroWindow;
            if (metroWindow == null)
                throw new InvalidOperationException($"Unable to get metro window from screen '{typeof(Screen).Name}'");

            return metroWindow;
        }

    }
}
