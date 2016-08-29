namespace ExternalAPIs.Beam.Pro.Dto
{
    public class KnownGame
    {
        public int id { get; set; }

        public string name { get; set; }

        public string parent { get; set; }

        public string description { get; set; }

        public string source { get; set; }

        public int viewersCurrent { get; set; }

        public string coverUrl { get; set; }

        /// <summary> Number of streams online for this game </summary>
        public int online { get; set; }
    }
}
