using System;
using System.Linq;
using Caliburn.Micro;
using Livestream.Monitor.Core;

namespace Livestream.Monitor.WPF.Core.UI
{
    public abstract class PagingConductor<T> : Conductor<T>.Collection.OneActive where T : class
    {
        /// <summary> The current page (starts at 1) </summary>
        public int Page
        {
            get;
            set => Set(ref field, value);
        } = 1;

        public int ItemsPerPage
        {
            get;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Items Per Page can not be less than 1");
                if (value == field) return;
                field = value;
                NotifyOfPropertyChange(() => ItemsPerPage);
            }
        }

        public abstract bool CanNext { get; }

        public abstract bool CanPrevious { get; }

        public virtual void Next()
        {
            Page++;

            MovePage();
        }

        public virtual void Previous()
        {
            Page--;

            MovePage();
        }

        protected virtual void MovePage()
        {
            _ = ActivateItemAsync(Items.SkipAndTake(Page - 1, ItemsPerPage).FirstOrDefault());
            NotifyOfPropertyChange(() => CanNext);
            NotifyOfPropertyChange(() => CanPrevious);
        }
    }
}
