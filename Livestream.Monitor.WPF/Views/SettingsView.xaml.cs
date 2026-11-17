using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Livestream.Monitor.Core;
using Livestream.Monitor.WPF.Core;
using Livestream.Monitor.WPF.ViewModels;
using MahApps.Metro.Controls;

namespace Livestream.Monitor.WPF.Views;

public partial class SettingsView
{
    public SettingsView() => InitializeComponent();

    internal void RevealPlatformActions(ApiPlatformSettingsEditor editor)
    {
        var model = (SettingsViewModel)DataContext;
        var (section, actions, save, revert) = editor switch
        {
            _ when editor == model.Twitch => (TwitchSection, TwitchActions, TwitchSave, TwitchRevert),
            _ when editor == model.Kick => (KickSection, KickActions, KickSave, KickRevert),
            _ when editor == model.YouTube => (YouTubeSection, YouTubeActions, YouTubeSave, YouTubeRevert),
            _ => throw new ArgumentOutOfRangeException(nameof(editor)),
        };

        ShakeFlyout();
        section.IsExpanded = true;
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsVisible || !editor.HasChanges) return;

            var action = editor.CanSave ? save : revert;
            action.Focus();
            actions.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private void ShakeFlyout()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        if (this.TryFindParent<Flyout>()?.RenderTransform is not TranslateTransform transform) return;

        const double StepMilliseconds = 55;
        const double Amplitude = 4;
        var shake = new DoubleAnimationUsingKeyFrames
        {
            FillBehavior = FillBehavior.Stop,
            KeyFrames =
            {
                new LinearDoubleKeyFrame(Amplitude, TimeSpan.FromMilliseconds(StepMilliseconds)),
                new LinearDoubleKeyFrame(-Amplitude / 2, TimeSpan.FromMilliseconds(StepMilliseconds * 2)),
                new LinearDoubleKeyFrame(Amplitude / 4, TimeSpan.FromMilliseconds(StepMilliseconds * 3)),
                new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(StepMilliseconds * 4)),
            },
        };

        transform.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    // WPF requires a void Click handler. Save and apply failures are reported through the dialog service.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void SavePlatform(object sender, RoutedEventArgs e)
    {
        var editor = (ApiPlatformSettingsEditor)((Button)sender).DataContext;
        var model = (SettingsViewModel)DataContext;
        try
        {
            await model.SavePlatform(editor);
        }
        catch (Exception exception)
        {
            await model.ShowMessageAsync("Unable to apply settings", exception.ExtractErrorMessage());
        }
    }

    private void RevertPlatform(object sender, RoutedEventArgs e)
    {
        var editor = (ApiPlatformSettingsEditor)((Button)sender).DataContext;
        editor.Revert();
    }

    private void CommitViewerThreshold(object sender, RoutedEventArgs e) =>
        ((SettingsViewModel)DataContext).CommitViewerThreshold();

    private void ViewerThresholdKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        CommitViewerThreshold(sender, e);
        e.Handled = true;
    }
}
