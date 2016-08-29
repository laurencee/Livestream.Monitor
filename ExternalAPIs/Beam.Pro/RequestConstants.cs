namespace ExternalAPIs.Beam.Pro
{
    public static class RequestConstants
    {
        public const string BeamProApiRoot = "https://beam.pro/api/v1";
        public const string Channels = BeamProApiRoot + "/channels";
        public const string Videos = BeamProApiRoot + "/channels/{0}/recordings";
        public const string Types = BeamProApiRoot + "/types";
    }
}