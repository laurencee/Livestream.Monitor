using System;
using Caliburn.Micro;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model
{
    public class VodDetails : PropertyChangedBase
    {
        private string url;
        private TimeSpan length;
        private long views;
        private DateTimeOffset recordedAt;
        private string game;
        private string description;
        private string title;
        private string previewImage;
        private IApiClient apiClient;
        private bool isUpcoming;

        public string Url
        {
            get { return url; }
            set => Set(ref url, value);
        }

        public TimeSpan Length
        {
            get { return length; }
            set => Set(ref length, value);
        }

        public long Views
        {
            get { return views; }
            set => Set(ref views, value);
        }

        public DateTimeOffset RecordedAt
        {
            get { return recordedAt; }
            set => Set(ref recordedAt, value);
        }

        public string Title
        {
            get { return title; }
            set => Set(ref title, value);
        }

        public string Description
        {
            get { return description; }
            set => Set(ref description, value);
        }

        public string Game
        {
            get { return game; }
            set => Set(ref game, value);
        }

        public string PreviewImage
        {
            get { return previewImage; }
            set => Set(ref previewImage, value);
        }

        public bool IsUpcoming
        {
            get { return isUpcoming; }
            set => Set(ref isUpcoming, value);
        }

        public IApiClient ApiClient
        {
            get { return apiClient; }
            set => Set(ref apiClient, value);
        }
    }
}