using System;
using System.Collections.Generic;
using System.Linq;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Model.ApiClients;
using MahApps.Metro.Controls.Dialogs;

namespace Livestream.Monitor.ViewModels
{
    public class ApiClientsQualitiesViewModel : Conductor<ApiClientQualitiesViewModel>.Collection.OneActive
    {
        private readonly IApiClientFactory apiClientFactory;
        private readonly ISettingsHandler settingsHandler;

        public ApiClientsQualitiesViewModel()
        {
            if (!Execute.InDesignMode) throw new InvalidOperationException("Constructor only accessible in design time");

            var apiClientQuality = new ApiClientQualitiesViewModel()
            {
                DisplayName = "Api client A",
                FallbackQuality = "Worst"
            };
            Items.AddRange(
                new []
                {
                    apiClientQuality,
                    new ApiClientQualitiesViewModel()
                    {
                        DisplayName = "Api client B",
                        Qualities = new BindableCollection<string>(new []{ "SomeValue", "SomeOtherValue"}),
                        FallbackQuality = FavoriteQualities.FallbackQualityOption.Best
                    },
                });

            ActiveItem = apiClientQuality;
        }

        public ApiClientsQualitiesViewModel(
            ISettingsHandler settingsHandler,
            IApiClientFactory apiClientFactory)
        {
            this.apiClientFactory = apiClientFactory ?? throw new ArgumentNullException(nameof(apiClientFactory));
            this.settingsHandler = settingsHandler ?? throw new ArgumentNullException(nameof(settingsHandler));
        }

        public override string DisplayName { get; set; } = "Stream Qualities";

        public bool CanSave
        {
            get
            {
                foreach (var apiClientQualities in Items)
                {
                    var previousQualities = settingsHandler.Settings.GetStreamQualities(apiClientQualities.DisplayName);
                    if (!previousQualities.Equals(apiClientQualities.ToFavoriteQualities()))
                        return true;
                }
                return false;
            }
        }

        public bool CanRevert => CanSave;

        public override async void CanClose(Action<bool> callback)
        {
            if (CanSave)
            {
                var result = await this.ShowMessageAsync("Unsaved changes", "Close without saving?", MessageDialogStyle.AffirmativeAndNegative);
                callback(result == MessageDialogResult.Affirmative);
            }
            else
            {
                callback(true);
            }
        }

        public void Save()
        {
            if (!CanSave) return;

            foreach (var apiClientQualities in Items)
            {
                settingsHandler.Settings.FavoriteApiQualities[apiClientQualities.DisplayName] = apiClientQualities.ToFavoriteQualities();
            }
            settingsHandler.SaveSettings();

            NotifyOfPropertyChange(() => CanSave);
            NotifyOfPropertyChange(() => CanRevert);
        }

        public void Revert()
        {
            if (!CanRevert) return;

            foreach (var apiClientQualities in Items)
            {
                var savedQualities = settingsHandler.Settings.GetStreamQualities(apiClientQualities.DisplayName);
                apiClientQualities.Qualities.Clear();
                apiClientQualities.Qualities.AddRange(savedQualities.Qualities);
                apiClientQualities.FallbackQuality = savedQualities.FallbackQuality;
            }
        }

        protected override void OnActivate()
        {
            var settingsStreamQualities = settingsHandler.Settings.FavoriteApiQualities;
            var apiClients = apiClientFactory.GetAll().ToList();

            foreach (var apiClient in apiClients)
            {
                settingsStreamQualities.TryGetValue(apiClient.ApiName, out var qualities);
                var bindableQualities = new BindableCollection<string>(qualities?.Qualities ?? new List<string>());

                var apiClientQualities = new ApiClientQualitiesViewModel()
                {
                    DisplayName = apiClient.ApiName,
                    Qualities = bindableQualities,
                    FallbackQuality = qualities?.FallbackQuality ?? FavoriteQualities.FallbackQualityOption.Best
                };

                apiClientQualities.PropertyChanged += (sender, args) => { NotifySaveRevert(); };
                apiClientQualities.Qualities.CollectionChanged += (sender, args) => { NotifySaveRevert(); };
                Items.Add(apiClientQualities);
            }
        }

        private void NotifySaveRevert()
        {
            NotifyOfPropertyChange(() => CanSave);
            NotifyOfPropertyChange(() => CanRevert);
        }
    }
}