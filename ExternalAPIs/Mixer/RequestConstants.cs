namespace ExternalAPIs.Mixer
{
    public static class RequestConstants
    {
        public const string MixerApiRoot = "https://mixer.com/api/v1";
        public const string Channels = MixerApiRoot + "/channels";
        public const string Videos = MixerApiRoot + "/channels/{0}/recordings";
        public const string Types = MixerApiRoot + "/types";
    }
}