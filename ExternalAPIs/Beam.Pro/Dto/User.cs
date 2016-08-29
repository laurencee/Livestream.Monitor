using System;

namespace ExternalAPIs.Beam.Pro.Dto
{
    public class User
    {
        public int level { get; set; }

        public Social social { get; set; }

        public int id { get; set; }

        public string username { get; set; }

        public bool verified { get; set; }

        public int experience { get; set; }

        public int sparks { get; set; }

        public string avatarUrl { get; set; }

        public string bio { get; set; }

        public int? primaryTeam { get; set; }

        public DateTimeOffset? createdAt { get; set; }

        public DateTimeOffset? updatedAt { get; set; }

        public DateTimeOffset? deletedAt { get; set; }
    }
}