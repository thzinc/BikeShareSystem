using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Linq;
using System.Text.RegularExpressions;
using Akka.Actor;
using GoogleApi;
using GoogleApi.Entities.Places.Search.Text.Request;
using LinqToTwitter;

namespace BikeShareSystem
{
    public class Conversation : ReceiveActor
    {
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
            Receive<LinqToTwitter.Status>(tweet =>
            {
                if (tweet.Text.Contains("challenge"))
                {
                    Become(() => Challenging(twitterContext, tweet));
                }
            });
        }

        private void Challenging(TwitterContext twitterContext, LinqToTwitter.Status challengeTweet)
        {
            // TODO: Get location
            // TODO: Ask BikeShareSystem for challenge
            // TODO: Get directions from Google
            // TODO: Reply with challenge

            Receive<BikeShareSystem.Challenge>(challenge =>
            {
                Console.WriteLine($"Go from {challenge.From.Name} to {challenge.To.Name}");
                Context.Stop(Self);
            });

            // TODO: Look for tweet location
            var coordinates = challengeTweet.Place?.BoundingBox?.Coordinates
                .Select(c => new GeoCoordinate(c.Latitude, c.Longitude))
                .ToList()
                ?? new List<GeoCoordinate>();
            GeoCoordinate fromCoordinate = null;
            GeoCoordinate toCoordinate = null;
            if (coordinates.Any())
            {
                fromCoordinate = GetCentralGeoCoordinate(coordinates);
            }

            // TODO: Look for "from"
            if (fromCoordinate == null)
            {
                var pattern = new Regex(@"\bfrom\b\s+(?<Place>.*?)(to|$)", RegexOptions.IgnoreCase);
                var match = pattern.Match(challengeTweet.Text);
                if (match.Success)
                {
                    var place = match.Groups["Place"].Value;
                    Console.WriteLine($"From: {place}");

                    var response = GooglePlaces.TextSearch.Query(new PlacesTextSearchRequest
                    {
                        Key = _googleApiKey,
                        Query = place,
                        Location = new GoogleApi.Entities.Common.Location(_settings.AreaOfInterest.Center.Latitude, _settings.AreaOfInterest.Center.Longitude),
                        Radius = _settings.AreaOfInterest.Radius * 1000,
                    });

                    if (response.Status.GetValueOrDefault() == GoogleApi.Entities.Common.Enums.Status.Ok)
                    {
                        fromCoordinate = response.Results
                            .Select(x => new GeoCoordinate(x.Geometry.Location.Latitude, x.Geometry.Location.Longitude))
                            .FirstOrDefault();
                    }
                }
            }

            // TODO: Look for "to"
            if (toCoordinate == null)
            {
                var pattern = new Regex(@"\bto\b\s+(?<Place>.*?)(from|$)", RegexOptions.IgnoreCase);
                var match = pattern.Match(challengeTweet.Text);
                if (match.Success)
                {
                    var place = match.Groups["Place"].Value;
                    Console.WriteLine($"To: {place}");

                    var response = GooglePlaces.TextSearch.Query(new PlacesTextSearchRequest
                    {
                        Key = _googleApiKey,
                        Query = place,
                        Location = new GoogleApi.Entities.Common.Location(_settings.AreaOfInterest.Center.Latitude, _settings.AreaOfInterest.Center.Longitude),
                        Radius = _settings.AreaOfInterest.Radius * 1000,
                    });

                    if (response.Status.GetValueOrDefault() == GoogleApi.Entities.Common.Enums.Status.Ok)
                    {
                        toCoordinate = response.Results
                            .Select(x => new GeoCoordinate(x.Geometry.Location.Latitude, x.Geometry.Location.Longitude))
                            .FirstOrDefault();
                    }
                }
            }

            var dist = fromCoordinate != null && toCoordinate != null ? fromCoordinate.GetDistanceTo(toCoordinate) : double.NaN;
            Console.WriteLine($"From {fromCoordinate} to {toCoordinate} {dist}");

            // twitterContext.ReplyAsync(challengeTweet.StatusID, $"@{challengeTweet.User.ScreenNameResponse} I'll challenge you soon...")
            //     .ContinueWith(task =>
            //     {
            //         var tweet = task.Result;
            //         return tweet;
            //     })
            //     .PipeTo(Self);
            var bikeShareSystems = _settings.BikeShareSystemIds.Select(systemId => Context.ActorSelection($"../../{systemId}"));
            foreach (var bikeShareSystem in bikeShareSystems)
            {
                bikeShareSystem.Tell(new BikeShareSystem.RequestChallenge
                {
                    From = fromCoordinate,
                    To = toCoordinate,
                });
            }
            // Self.Tell(PoisonPill.Instance);
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
