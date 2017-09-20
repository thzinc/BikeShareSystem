using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using GoogleApi;
using GoogleApi.Entities.Maps.Common.Enums;
using GoogleApi.Entities.Maps.Directions.Request;
using GoogleApi.Entities.Places.Search.Text.Request;
using LinqToTwitter;

namespace BikeShareSystem
{
    public class Conversation : ReceiveActor, IWithUnboundedStash
    {
        public IStash Stash { get; set; }

        private Settings.Bot _settings;
        private string _googleApiKey;
        public Conversation(Settings.Bot settings, string googleApiKey, TwitterContext twitterContext)
        {
            _settings = settings;
            _googleApiKey = googleApiKey;
            Become(() => Listening(twitterContext));
        }

        private void Listening(TwitterContext twitterContext)
        {
            var poisonPill = Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromMinutes(15), Self, PoisonPill.Instance, Self);

            Receive<LinqToTwitter.Status>(tweet =>
            {
                if (tweet.Text.Contains("challenge"))
                {
                    poisonPill.Cancel();
                    Become(() => Challenging(twitterContext, tweet));
                }
                else if (new[] {"how", "system"}.All(tweet.Text.Contains))
                {
                    poisonPill.Cancel();
                    Become(() => GettingBikeShareSystemInfo(twitterContext, tweet));
                }
            });
        }

        private void GettingBikeShareSystemInfo(TwitterContext twitterContext, LinqToTwitter.Status tweet)
        {
            Receive<BikeShareSystem.Status>(status =>
            {
                twitterContext.ReplyAsync(tweet.StatusID, $"@{tweet.User.ScreenNameResponse} There are {status.Bikes} bikes and {status.Docks} docks across {status.Stations} stations.")
                    .ContinueWith(task =>
                    {
                        Console.WriteLine($"Tweeted {task.Result.Text}");
                        return true;
                    },
                    TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(Self);

                Become(() => Listening(twitterContext));

                Stash.UnstashAll();
            });

            TellAllBikeShareSystems(new BikeShareSystem.RequestStatus
            {
                AreaOfInterest = _settings.AreaOfInterest,
            });
        }

        private void Challenging(TwitterContext twitterContext, LinqToTwitter.Status challengeTweet)
        {
            Receive<BikeShareSystem.Challenge>(challenge =>
            {
                var result = GoogleMaps.Directions.Query(new DirectionsRequest
                {
                    Key = _googleApiKey,
                    Origin = new GoogleApi.Entities.Common.Location(challenge.From.Lat, challenge.From.Lon),
                    Destination = new GoogleApi.Entities.Common.Location(challenge.To.Lat, challenge.To.Lon),
                    TravelMode = TravelMode.Bicycling,
                    Units = Units.Imperial,
                });

                if (result.Status.GetValueOrDefault() == GoogleApi.Entities.Common.Enums.Status.Ok)
                {
                    var leg = result.Routes
                        .SelectMany(r => r.Legs)
                        .FirstOrDefault();
                    string status = BuildChallengeTweet(challenge, leg);

                    twitterContext.ReplyAsync(challengeTweet.StatusID, $"@{challengeTweet.User.ScreenNameResponse} {status}")
                        .ContinueWith(task =>
                        {
                            Console.WriteLine($"Tweeted {task.Result.Text}");
                            return true;
                        },
                        TaskContinuationOptions.ExecuteSynchronously)
                        .PipeTo(Self);
                }

                Become(() => Listening(twitterContext));

                Stash.UnstashAll();
            });

            ReceiveAny(_ => Stash.Stash());

            var fromCoordinate = GetPlaceCoordinate(new Regex(@"\bfrom\b\s+(?<Place>.*?)(\bto\b|$)", RegexOptions.IgnoreCase), challengeTweet.Text);
            var toCoordinate = GetPlaceCoordinate(new Regex(@"\bto\b\s+(?<Place>.*?)(\bfrom\b|$)", RegexOptions.IgnoreCase), challengeTweet.Text);

            if (fromCoordinate == null)
            {
                List<GeoCoordinate> coordinates = GetCoordinatesFromTweet(challengeTweet);
                if (coordinates.Any())
                {
                    fromCoordinate = GetCentralGeoCoordinate(coordinates);
                }
            }

            TellAllBikeShareSystems(new BikeShareSystem.RequestChallenge
            {
                From = fromCoordinate,
                To = toCoordinate,
                AreaOfInterest = _settings.AreaOfInterest,
            });
        }

        private void TellAllBikeShareSystems(object request)
        {
            var bikeShareSystems = _settings.BikeShareSystemIds.Select(systemId => Context.ActorSelection($"../../{systemId}"));
            foreach (var bikeShareSystem in bikeShareSystems)
            {
                bikeShareSystem.Tell(request);
            }
        }

        private string BuildChallengeTweet(BikeShareSystem.Challenge challenge, GoogleApi.Entities.Maps.Directions.Response.Leg leg)
        {
            var start = challenge.FromShortName;
            var end = challenge.ToShortName;
            var distance = leg.Distance.Text;
            var duration = leg.Duration.Text;
            var link = $"https://www.google.com/maps/dir/?api=1&origin={challenge.From.Lat},{challenge.From.Lon}&destination={challenge.To.Lat},{challenge.To.Lon}&travelmode=bicycling";

            var status = _settings.Replies.Challenge.Messages
                .OrderBy(_ => Guid.NewGuid())
                .Select(template => template
                    .Replace("{start}", start)
                    .Replace("{end}", end)
                    .Replace("{distance}", distance)
                    .Replace("{duration}", duration)
                    .Replace("{link}", link))
                .First();
            return status;
        }

        private static List<GeoCoordinate> GetCoordinatesFromTweet(LinqToTwitter.Status tweet)
        {
            return tweet.Place?.BoundingBox?.Coordinates
                .Select(c => new GeoCoordinate(c.Latitude, c.Longitude))
                .ToList()
                ?? new List<GeoCoordinate>();
        }

        private GeoCoordinate GetPlaceCoordinate(Regex pattern, string text)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                var place = match.Groups["Place"].Value;
                var response = GooglePlaces.TextSearch.Query(new PlacesTextSearchRequest
                {
                    Key = _googleApiKey,
                    Query = place,
                    Location = new GoogleApi.Entities.Common.Location(_settings.AreaOfInterest.Center.Latitude, _settings.AreaOfInterest.Center.Longitude),
                    Radius = _settings.AreaOfInterest.Radius * 1000,
                });

                if (response.Status.GetValueOrDefault() == GoogleApi.Entities.Common.Enums.Status.Ok)
                {
                    return response.Results
                        .Select(x => new GeoCoordinate(x.Geometry.Location.Latitude, x.Geometry.Location.Longitude))
                        .FirstOrDefault();
                }
            }

            return null;
        }

        /// <remarks>
        /// From https://stackoverflow.com/a/14231286
        /// </remarks>
        public static GeoCoordinate GetCentralGeoCoordinate(IList<GeoCoordinate> geoCoordinates)
        {
            if (geoCoordinates.Count == 1)
            {
                return geoCoordinates.Single();
            }

            double x = 0;
            double y = 0;
            double z = 0;

            foreach (var geoCoordinate in geoCoordinates)
            {
                var latitude = geoCoordinate.Latitude * Math.PI / 180;
                var longitude = geoCoordinate.Longitude * Math.PI / 180;

                x += Math.Cos(latitude) * Math.Cos(longitude);
                y += Math.Cos(latitude) * Math.Sin(longitude);
                z += Math.Sin(latitude);
            }

            var total = geoCoordinates.Count;

            x = x / total;
            y = y / total;
            z = z / total;

            var centralLongitude = Math.Atan2(y, x);
            var centralSquareRoot = Math.Sqrt(x * x + y * y);
            var centralLatitude = Math.Atan2(z, centralSquareRoot);

            return new GeoCoordinate(centralLatitude * 180 / Math.PI, centralLongitude * 180 / Math.PI);
        }
    }
}
