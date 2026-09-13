using System;
using Caliburn.Micro;
using Livestream.Monitor.Model.ApiClients;

namespace Livestream.Monitor.Model
{
    public class VodDetails : PropertyChangedBase
    {
        public string Url
        {
            get;
            set => Set(ref field, value);
        }

        public TimeSpan Length
        {
            get;
            set => Set(ref field, value);
        }

        public long Views
        {
            get;
            set => Set(ref field, value);
        }

        public DateTimeOffset RecordedAt
        {
            get;
            set => Set(ref field, value);
        }

        public string Title
        {
            get;
            set => Set(ref field, value);
        }

        public string Description
        {
            get;
            set => Set(ref field, value);
        }

        public string Game
        {
            get;
            set => Set(ref field, value);
        }

        public string PreviewImage
        {
            get;
            set => Set(ref field, value);
        }

        public bool IsUpcoming
        {
            get;
            set => Set(ref field, value);
        }

        public IApiClient ApiClient
        {
            get;
            set => Set(ref field, value);
        }
    }
}