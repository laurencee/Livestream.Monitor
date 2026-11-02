using System;
using System.Linq;
using System.Windows.Input;
using Caliburn.Micro;
using Livestream.Monitor.Core;
using Livestream.Monitor.Core.Settings;

namespace Livestream.Monitor.WPF.ViewModels
{
    public class ApiClientQualitiesViewModel : Screen
    {
        private string newQuality;

        public ApiClientQualitiesViewModel()
        {
            Qualities.CollectionChanged += (_, _) =>
            {
                NotifyOfPropertyChange(() => QualityRows);
                NotifyOfPropertyChange(() => HasQualities);
                NotifyOfPropertyChange(() => CanAddQuality);
            };

            if (Execute.InDesignMode)
            {
                Qualities.AddRange(["720p60", "720"]);
                FallbackQuality = "Worst";
            }
        }

        public BindableCollection<string> Qualities { get; } = new();

        public QualityPriorityRow[] QualityRows => Qualities.Select((quality, index) => new QualityPriorityRow(index + 1, quality)).ToArray();

        public bool HasQualities => Qualities.Count != 0;

        public string EmptyQualityHint
        {
            get;
            set => Set(ref field, value);
        } = "[Using the fallback quality]";

        public string FallbackQuality
        {
            get;
            set
            {
                if (!Set(ref field, value)) return;

                NotifyOfPropertyChange(() => FallbackQualityBestChecked);
                NotifyOfPropertyChange(() => FallbackQualityWorstChecked);
            }
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

        public bool CanMoveQualityDown(QualityPriorityRow qualityRow) => qualityRow.Priority > 0 && qualityRow.Priority < Qualities.Count;

        public bool CanMoveQualityUp(QualityPriorityRow qualityRow) => qualityRow.Priority > 1 && qualityRow.Priority <= Qualities.Count;

        public void AddQuality()
        {
            if (!CanAddQuality) return;

            Qualities.Insert(0, NewQuality.Trim());
            NewQuality = "";
            Qualities.Refresh(); // causes the CanMove guards to be checked
            NotifyOfPropertyChange(() => CanAddQuality);
        }

        public void RemoveQuality(QualityPriorityRow qualityRow)
        {
            Qualities.RemoveAt(qualityRow.Priority - 1);
            Qualities.Refresh(); // causes the CanMove guards to be checked
        }

        public void MoveQualityDown(QualityPriorityRow qualityRow)
        {
            if (!CanMoveQualityDown(qualityRow)) return;

            var index = qualityRow.Priority - 1;
            Qualities.Move(index, index + 1);
            Qualities.Refresh(); // causes the CanMove guards to be checked
        }

        public void MoveQualityUp(QualityPriorityRow qualityRow)
        {
            if (!CanMoveQualityUp(qualityRow)) return;

            var index = qualityRow.Priority - 1;
            Qualities.Move(index, index - 1);
            Qualities.Refresh(); // causes the CanMove guards to be checked
        }

        public void KeyPressed(KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                AddQuality();
        }
    }

    public sealed class QualityPriorityRow(int priority, string value)
    {
        public int Priority { get; } = priority;

        public string Value { get; } = value;
    }
}
