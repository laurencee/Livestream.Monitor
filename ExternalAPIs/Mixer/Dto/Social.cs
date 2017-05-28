using System.Collections.Generic;

namespace ExternalAPIs.Mixer.Dto
{
    public class Social
    {
        public string twitter { get; set; }
        public string facebook { get; set; }
        public string youtube { get; set; }
        public string discord { get; set; }
        public List<object> verified { get; set; }
        public string player { get; set; }
    }
}