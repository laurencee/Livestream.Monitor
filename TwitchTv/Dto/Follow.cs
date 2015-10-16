namespace TwitchTv.Dto
{
    public class Follow
    {
        public string CreatedAt { get; set; }

        public bool? Notifications { get; set; }

        public Channel Channel { get; set; }
    }
}