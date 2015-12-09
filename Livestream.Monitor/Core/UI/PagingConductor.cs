using System;
using System.Linq;
using Caliburn.Micro;

namespace Livestream.Monitor.Core.UI
{
    public abstract class PagingConductor<T> : Conductor<T>.Collection.OneActive where T : class
    {
        private int page = 1;
        private int itemsPerPage;

        /// <summary> The current page (starts at 1) </summary>
        public int Page
        {
            get { return page; }
            set
            {
                if (value == page) return;
                page = value;
                NotifyOfPropertyChange(() => Page);
            }
        }

        public int TotalPages => Math.Min(Items.Count / ItemsPerPage, Items.Count);

        public int ItemsPerPage
        {
            get { return itemsPerPage; }
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Items Per Page can not be less than 1");
                if (value == itemsPerPage) return;
                itemsPerPage = value;
                NotifyOfPropertyChange(() => ItemsPerPage);
                NotifyOfPropertyChange(() => TotalPages);
            }
        }

        /// <summary> Will return to the start or end of the list on executing Next/Previous. </summary>
        protected bool Circular { get; set; }

        public virtual bool CanNext => Circular || Page < TotalPages;

        public virtual bool CanPrevious => Circular || Page > 1;

        public virtual void Next()
        {
            if (Circular && TotalPages <= Page)
                Page = 1;
            else
                Page++;

            MovePage();
        }

        public virtual void Previous()
        {
            if (Circular && Page == 1)
                Page = TotalPages;
            else
                Page--;

            MovePage();
        }

        protected virtual void MovePage()
        {
            ActivateItem(Items.SkipAndTake(Page - 1, ItemsPerPage).FirstOrDefault());
            NotifyOfPropertyChange(() => CanNext);
            NotifyOfPropertyChange(() => CanPrevious);
        }
    }
}