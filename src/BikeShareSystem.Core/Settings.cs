using System;
using System.Collections.Generic;

namespace BikeShareSystem
{
    public class Settings
    {
        public List<BikeShareSystem> BikeShareSystems { get; set; } = new List<BikeShareSystem>();
        public List<Bot> Bots { get; set; } = new List<Bot>();
        public string GoogleApiKey { get; set; }

        public class BikeShareSystem
        {
            public string SystemId { get; set; }
            public string ManifestUrl { get; set; }
            public Dictionary<string, string> StationNameReplacements { get; set; } = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        }

        public class Bot
        {
            public CircularArea AreaOfInterest { get; set; }
            public List<string> BikeShareSystemIds { get; set; } = new List<string>();
            public TwitterSettings Twitter { get; set; }
            public List<Schedule> Schedules { get; set; } = new List<Schedule>();
            public ReplySettings Replies { get; set; }
            public class TwitterSettings
            {
                public string ScreenName { get; set; }
                public string ConsumerKey { get; set; }
                public string ConsumerSecret { get; set; }
                public string AccessTokenKey { get; set; }
                public string AccessTokenSecret { get; set; }
            }

            public class Schedule
            {
                public string Time { get; set; }
                public List<string> Messages { get; set; } = new List<string>();
            }

            public class ReplySettings
            {
                public ChallengeSettings Challenge { get; set; }

                public class ChallengeSettings
                {
                    public string LocationNotFound { get; set; }
                    public List<string> Messages { get; set; } = new List<string>();
                }
            }
        }

        public class CircularArea
        {
            public Point Center { get; set; }
            public double Radius { get; set; }
        }

        public class Point
        {
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }
    }
}
