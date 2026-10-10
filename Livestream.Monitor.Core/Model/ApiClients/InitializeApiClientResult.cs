namespace Livestream.Monitor.Model.ApiClients
{
    public class InitializeApiClientResult
    {
        /// <summary> Indicates that we need to resave livestreams as something changed in the channel data </summary>
        public bool ChannelIdentifierDataDirty { get; set; }
    }
}