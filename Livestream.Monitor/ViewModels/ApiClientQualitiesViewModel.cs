using System;
using System.Collections.Generic;
using System.Windows.Input;
using Caliburn.Micro;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.ViewModels
{
    public class ApiClientQualitiesViewModel : Screen
    {
        private string fallbackQuality;
        private string newQuality;

        public ApiClientQualitiesViewModel()
        {
            if (Execute.InDesignMode)
            {
                Qualities.AddRange(new []
                {
                    "720p60",
                    "720"
                });
                FallbackQuality = "Worst";
            }
        }

        public BindableCollection<string> Qualities { get; set; } = new BindableCollection<string>();

        public string FallbackQuality
        {
            get { return fallbackQuality; }
            set => Set(ref fallbackQuality, value);
        }

        public bool FallbackQualityBestChecked
        {
            get { return FallbackQuality == FavoriteQualities.FallbackQualityOption.Best; }
            set
            {
                if (!value) return;
                if (FallbackQuality == FavoriteQualities.FallbackQualityOption.Best) return;
                FallbackQuality = FavoriteQualities.FallbackQualityOption.Best;
                NotifyOfPropertyChange();
            }
        }

        public bool FallbackQualityWorstChecked
        {
            get { return FallbackQuality == FavoriteQualities.FallbackQualityOption.Worst; }
            set
            {
                if (!value) return;
                if (FallbackQuality == FavoriteQualities.FallbackQualityOption.Worst) return;
                FallbackQuality = FavoriteQualities.FallbackQualityOption.Worst;
                NotifyOfPropertyChange();
            }
        }

        public string NewQuality
        {
            get { return newQuality; }
            set
            {
                if (value == newQuality) return;
                newQuality = value;
                NotifyOfPropertyChange();
                NotifyOfPropertyChange(() => CanAddQuality);
            }
        }

        public bool CanAddQuality => !string.IsNullOrWhiteSpace(newQuality) &&
                                     !Qualities.Contains(newQuality.Trim(), StringComparison.OrdinalIgnoreCase);


        public bool CanMoveQualityDown(string streamQuality)
        {
            var index = Qualities.IndexOf(streamQuality);
            return Qualities.Count > 1 &&
                   index < Qualities.Count - 1;
        }

        public bool CanMoveQualityUp(string streamQuality)
        {
            var index = Qualities.IndexOf(streamQuality);
            return Qualities.Count > 1 &&
                   index != 0;
        }

        public void AddQuality()
        {
            if (!CanAddQuality) return;

            Qualities.Insert(0, NewQuality.Trim());
            Qualities.Refresh(); // causes the CanMove guards to be checked
            NotifyOfPropertyChange(() => CanAddQuality);
        }

        public void RemoveQuality(string streamQuality)
        {
            Qualities.Remove(streamQuality);
            Qualities.Refresh(); // causes the CanMove guards to be checked
        }

        public void MoveQualityDown(string streamQuality)
        {
            if (!CanMoveQualityDown(streamQuality)) return;

            var index = Qualities.IndexOf(streamQuality);
            Qualities.Move(index, index + 1);
            Qualities.Refresh(); // causes the CanMove guards to be checked
        }

        public void MoveQualityUp(string streamQuality)
        {
            if (!CanMoveQualityUp(streamQuality)) return;

            var index = Qualities.IndexOf(streamQuality);
            Qualities.Move(index, index - 1);
            Qualities.Refresh(); // causes the CanMove guards to be checked
        }

        public void KeyPressed(KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                AddQuality();
        }

        public FavoriteQualities ToFavoriteQualities()
        {
            return new FavoriteQualities()
            {
                Qualities = new List<string>(Qualities),
                FallbackQuality = FallbackQuality
            };
        }
    }
}
