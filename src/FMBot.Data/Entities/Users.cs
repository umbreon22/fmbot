using System;
using System.Collections.Generic;

namespace FMBot.Data.Entities
{
    public class User
    {
        public int UserId { get; set; }

        public ulong DiscordUserId { get; set; }

        public bool? Featured { get; set; }

        public bool? Blacklisted { get; set; }

        public UserType UserType { get; set; }

        public bool? TitlesEnabled { get; set; }

        public string UserNameLastFM { get; set; }

        public ChartType ChartType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }

        public DateTime? LastGeneratedChartDateTimeUtc { get; set; }

        public ICollection<Friend> FriendedByUsers { get; set; }

        public ICollection<Friend> Friends { get; set; }
    }
}
