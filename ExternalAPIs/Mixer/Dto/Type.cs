namespace ExternalAPIs.Mixer.Dto
{
    public class Type
    {
        public int id { get; set; }

        public string name { get; set; }

        public string parent { get; set; }

        public string description { get; set; }

        public string source { get; set; }

        public int viewersCurrent { get; set; }

        public string coverUrl { get; set; }

        public int online { get; set; }
    }
}