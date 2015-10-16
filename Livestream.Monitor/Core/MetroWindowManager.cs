using System;
using System.Windows;
using Caliburn.Micro;
using MahApps.Metro;
using MahApps.Metro.Controls;

namespace Livestream.Monitor.Core
{
    public class MetroWindowManager : WindowManager
    {
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

            return window;
        }

        public static void ChangeTheme(MetroThemeBaseColour baseColour)
        {
            var currentTheme = ThemeManager.DetectAppStyle(Application.Current.MainWindow);
            var baseTheme = ThemeManager.GetAppTheme(baseColour.ToString());
            ChangeTheme(baseTheme, currentTheme.Item2);
        }

        public static void ChangeTheme(MetroThemeAccentColour accentColour)
        {
            var currentTheme = ThemeManager.DetectAppStyle(Application.Current.MainWindow);
            var accent = ThemeManager.GetAccent(accentColour.ToString());
            ChangeTheme(currentTheme.Item1, accent);
        }

        /// <summary> Changes the colour of base and accent colour of the application </summary>
        public static void ChangeTheme(AppTheme baseColour, Accent accentColour)
        {
            // change the theme for the main window so the update is immediate
            ThemeManager.ChangeAppStyle(Application.Current.MainWindow, accentColour, baseColour);

            // change the default theme for all future windows opened
            ThemeManager.ChangeAppStyle(Application.Current, accentColour, baseColour);
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
                                       "pack://application:,,,/MahApps.Metro;component/Styles/Colors.xaml",
                                       UriKind.RelativeOrAbsolute)
                               },
                           new ResourceDictionary
                               {
                                   Source =
                                       new Uri(
                                       "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
                                       UriKind.RelativeOrAbsolute)
                               },
                           new ResourceDictionary
                               {
                                   Source =
                                       new Uri(
                                       "pack://application:,,,/MahApps.Metro;component/Styles/Controls.AnimatedSingleRowTabControl.xaml",
                                       UriKind.RelativeOrAbsolute)
                               },
                           new ResourceDictionary
                               {
                                   Source =
                                       new Uri(
                                       "pack://application:,,,/MahApps.Metro;component/Styles/Accents/Orange.xaml",
                                       UriKind.RelativeOrAbsolute)
                               },
                           new ResourceDictionary
                               {
                                   Source =
                                       new Uri(
                                       "pack://application:,,,/MahApps.Metro;component/Styles/Accents/BaseLight.xaml",
                                       UriKind.RelativeOrAbsolute)
                               }
                       };
        }
    }
}
