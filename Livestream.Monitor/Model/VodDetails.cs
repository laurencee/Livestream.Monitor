using System;
using Caliburn.Micro;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model
{
    public class VodDetails : PropertyChangedBase
    {
        private string url;
        private TimeSpan length;
        private int views;
        private DateTimeOffset recordedAt;
        private string game;
        private string description;
        private string title;
        private string previewImage;
        private IApiClient apiClient;

        public string Url
        {
            get { return url; }
            set
            {
                if (value == url) return;
                url = value;
                NotifyOfPropertyChange(() => Url);
            }
        }

        public TimeSpan Length
        {
            get { return length; }
            set
            {
                if (value == length) return;
                length = value;
                NotifyOfPropertyChange(() => Length);
            }
        }

        public int Views
        {
            get { return views; }
            set
            {
                if (value == views) return;
                views = value;
                NotifyOfPropertyChange(() => Views);
            }
        }

        public DateTimeOffset RecordedAt
        {
            get { return recordedAt; }
            set
            {
                if (value.Equals(recordedAt)) return;
                recordedAt = value;
                NotifyOfPropertyChange(() => RecordedAt);
            }
        }

        public string Title
        {
            get { return title; }
            set
            {
                if (value == title) return;
                title = value;
                NotifyOfPropertyChange(() => Title);
            }
        }

        public string Description
        {
            get { return description; }
            set
            {
                if (value == description) return;
                description = value;
                NotifyOfPropertyChange(() => Description);
            }
        }

        public string Game
        {
            get { return game; }
            set
            {
                if (value == game) return;
                game = value;
                NotifyOfPropertyChange(() => Game);
            }
        }

        public string PreviewImage
        {
            get { return previewImage; }
            set
            {
                if (value == previewImage) return;
                previewImage = value;
                NotifyOfPropertyChange(() => PreviewImage);
            }
        }

        public IApiClient ApiClient
        {
            get { return apiClient; }
            set
            {
                if (Equals(value, apiClient)) return;
                apiClient = value;
                NotifyOfPropertyChange(() => ApiClient);
            }
        }
    }
}