using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace Livestream.Monitor.Core
{
    public static class Extensions
    {
        /// <summary>
        /// Returns spaced output for a Pascal cased string value
        /// <example>"SomePascalCaseString" comes back as "Some Pascal Case String"</example>
        /// </summary>
        /// <param name="pascalCaseValue">A string in Pascal case format</param>
        /// <returns>Spaced Pascal case string</returns>
        public static string ToFriendlyString(this string pascalCaseValue)
        {
            return Regex.Replace(pascalCaseValue, "(?!^)([A-Z])", " $1");
        }

        /// <summary>
        /// Contains method allowing for a <see cref="StringComparison"/> to be passed in
        /// </summary>
        /// <param name="original">Original string value</param>
        /// <param name="value">String value being compared against</param>
        /// <param name="stringComparison">Specifies the culter, case and sort rules for the comparison</param>
        /// <returns>True if the original string contains the comparing string value following the <see cref="StringComparison"/> rules</returns>
        public static bool Contains(this string original, string value, StringComparison stringComparison)
        {
            return original.IndexOf(value, stringComparison) >= 0;
        }

        public static bool IsEqualTo(this string original, string value, StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            return String.Compare(original, value, stringComparison) == 0;
        }

        /// <summary> Save implementation to show messages from viewmodels </summary>
        public static async Task ShowMessage(this IViewAware screen, string title, string message, 
            MessageDialogStyle messageDialogStyle = MessageDialogStyle.Affirmative,
            MetroDialogSettings dialogSettings = null)
        {
            if (screen == null) return;
            var uiElement = screen.GetView() as DependencyObject;
            if (uiElement != null)
            {
                var metroWindow = MetroWindow.GetWindow(uiElement) as MetroWindow;
                if (metroWindow != null)
                {
                    await metroWindow.ShowMessageAsync(title, message, messageDialogStyle, dialogSettings);
                }
            }
        }
    }
}