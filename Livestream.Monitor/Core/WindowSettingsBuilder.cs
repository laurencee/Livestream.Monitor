using System.Dynamic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Livestream.Monitor.Core
{
    public class WindowSettingsBuilder
    {
        private readonly dynamic settings = new ExpandoObject();

        /// <summary>
        /// Popup settings builder to help creating <see cref="Popup"/>s.
        /// <para>
        ///     Default values: StaysOpen = false, Placement = <see cref="PlacementMode.Bottom"/>,
        ///     PopupAnimation = <see cref="PopupAnimation.Scroll"/>
        /// </para>
        /// </summary>
        public WindowSettingsBuilder PopupSettings(UIElement placementTarget, bool staysOpen = false)
        {
            settings.PlacementTarget = placementTarget;
            settings.StaysOpen = staysOpen;
            settings.Placement = PlacementMode.Bottom;
            settings.PopupAnimation = PopupAnimation.Scroll;
            return this;
        }

        public WindowSettingsBuilder WithTopLeft(double top = 0, double left = 0)
        {
            WithStartupLocation(WindowStartupLocation.Manual);
            settings.Left = left;
            settings.Top = top;
            return this;
        }

        /// <summary> Convenience for setting WithResizeMode noresize and WindowStyle none </summary>
        public WindowSettingsBuilder NoResizeBorderless()
        {
            WithResizeMode(ResizeMode.NoResize);
            WithWindowStyle(WindowStyle.None);
            return this;
        }

        public WindowSettingsBuilder SizeToContent(SizeToContent size = System.Windows.SizeToContent.WidthAndHeight)
        {
            settings.SizeToContent = size;
            return this;
        }

        public WindowSettingsBuilder WithResizeMode(ResizeMode resizeMode)
        {
            settings.ResizeMode = resizeMode;
            return this;
        }

        public WindowSettingsBuilder WithWindowStyle(WindowStyle windowStyle)
        {
            if (windowStyle != WindowStyle.None)
                settings.AllowsTransparency = false; // transparency + windows style other than none will trigger an exception

            settings.WindowStyle = windowStyle;
            return this;
        }

        public WindowSettingsBuilder WithPlacement(PlacementMode placementMode)
        {
            settings.Placement = placementMode;
            return this;
        }

        public WindowSettingsBuilder WithStartupLocation(WindowStartupLocation startupLocation)
        {
            settings.WindowStartupLocation = startupLocation;
            return this;
        }

        public WindowSettingsBuilder WithPopupAnimation(PopupAnimation popupAnimation)
        {
            settings.PopupAnimation = popupAnimation;
            return this;
        }

        public WindowSettingsBuilder AsTopmost(bool isTopmost)
        {
            settings.TopMost = isTopmost;
            return this;
        }

        public WindowSettingsBuilder TransparentBackground()
        {
            settings.AllowsTransparency = true;
            settings.Background = Brushes.Transparent;
            return this;
        }

        public ExpandoObject Create()
        {
            return settings;
        }
    }
}
