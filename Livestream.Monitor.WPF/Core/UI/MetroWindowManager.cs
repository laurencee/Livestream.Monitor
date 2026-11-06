using System;
using System.Windows;
using System.Windows.Interop;
using Caliburn.Micro;
using ControlzEx.Theming;
using Livestream.Monitor.Core.Settings;
using Livestream.Monitor.Core.UI;
using Livestream.Monitor.WPF.Views;
using MahApps.Metro.Controls;

namespace Livestream.Monitor.WPF.Core.UI
{
    public class MetroWindowManager(ISettingsHandler settingsHandler) : WindowManager
    {
        private readonly ISettingsHandler settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
        private ResourceDictionary[] resourceDictionaries;

        protected override Window EnsureWindow(object model, object view, bool isDialog)
        {
            MetroWindow window = null;
            Window inferOwnerOf;
            if (view is MetroWindow)
            {
                window = CreateCustomWindow(view, true);
                inferOwnerOf = InferOwnerOf(window);
                if (inferOwnerOf != null && isDialog)
                {
                    window.Owner = inferOwnerOf;
                }
            }

            if (window == null)
            {
                window = CreateCustomWindow(view, false);
            }

            ConfigureWindow(window);
            window.SetValue(View.IsGeneratedProperty, true);
            inferOwnerOf = InferOwnerOf(window);
            if (inferOwnerOf != null)
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Owner = inferOwnerOf;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (DisplayPlacement.IsRequested)
            {
                const double DisplayInset = 80;
                var workArea = DisplayPlacement.GetWorkArea(window);
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = workArea.Left + DisplayInset;
                window.Top = workArea.Top + DisplayInset;
                window.ShowActivated = false;
            }
            else if (window is ShellView)
            {
                window.SourceInitialized += (_, _) =>
                {
                    var placement = CreateMainWindowPlacement(window);
                    window.LocationChanged += (_, _) => placement.Capture();
                    window.Closed += (_, _) => placement.Save();
                };
            }

            return window;
        }

        private static MainWindowPlacement CreateMainWindowPlacement(MetroWindow window)
        {
            window.ApplyTemplate();
            var commandSpace = new Size(double.PositiveInfinity, window.TitleBarHeight);
            window.RightWindowCommands.Measure(commandSpace);
            window.WindowButtonCommands.Measure(commandSpace);

            var resizeBorder = window.ResizeBorderThickness;
            int topInset = (int)Math.Ceiling(Math.Max(resizeBorder.Top, window.BorderThickness.Top));
            int iconWidth = window.ShowIconOnTitleBar ? window.TitleBarHeight : 0;
            int leftInset = (int)Math.Ceiling(Math.Max(resizeBorder.Left, window.BorderThickness.Left + iconWidth));
            double commandsWidth = window.RightWindowCommands.DesiredSize.Width + window.WindowButtonCommands.DesiredSize.Width;
            int rightInset = (int)Math.Ceiling(Math.Max(resizeBorder.Right, window.BorderThickness.Right + commandsWidth));
            var handle = new WindowInteropHelper(window).Handle;
            return new MainWindowPlacement(
                window: handle,
                titleHeight: window.TitleBarHeight,
                topTitleInset: topInset,
                leftTitleInset: leftInset,
                rightTitleInset: rightInset);
        }

        public virtual void ConfigureWindow(MetroWindow window)
        {

        }

        public virtual MetroWindow CreateCustomWindow(object view, bool windowIsView)
        {
            MetroWindow result;
            if (windowIsView)
            {
                result = view as MetroWindow;
            }
            else
            {
                result = new MetroWindow
                {
                    Content = view
                };
            }

            AddMetroResources(result);
            return result;
        }

        private void AddMetroResources(MetroWindow window)
        {
            resourceDictionaries = LoadResources();
            foreach (ResourceDictionary dictionary in resourceDictionaries)
            {
                window.Resources.MergedDictionaries.Add(dictionary);
            }

            var accentColour = settingsHandler.Settings.MetroThemeAccentColour;
            var baseColour = settingsHandler.Settings.MetroThemeBaseColour;

            var themeName = $"{baseColour.ToString().Replace("Base", "")}.{accentColour}";
            ThemeManager.Current.ChangeTheme(window, themeName);
        }

        private ResourceDictionary[] LoadResources()
        {
            return new[]
                       {
                           new ResourceDictionary
                               {
                                   Source =
                                       new Uri(
                                       "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
                                       UriKind.RelativeOrAbsolute)
                               },
                           new ResourceDictionary
                               {
                                   Source =
                                       new Uri(
                                       "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
                                       UriKind.RelativeOrAbsolute)
                               }
                       };
        }
    }
}
