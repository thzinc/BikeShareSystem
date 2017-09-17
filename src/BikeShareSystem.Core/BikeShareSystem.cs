using System;
using System.Collections.Generic;
using System.Device.Location;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Gbfs.Net.v1;

namespace BikeShareSystem
{
    public class BikeShareSystem : ReceiveActor
    {
        public class RefreshManifest { }
        public abstract class RefreshStations
        {
            public Manifest Manifest { get; set; }
            public string Language { get; set; }
        }

        public class RefreshStationInformation : RefreshStations { }
        public class RefreshStationStatus : RefreshStations { }
        public class RequestChallenge
        {
            public GeoCoordinate From { get; set; }
            public GeoCoordinate To { get; set; }
            public Settings.CircularArea AreaOfInterest { get; set; }
        }

        public class Challenge
        {
            public Station From { get; set; }
            public Station To { get; set; }
            public string FromShortName { get; set; }
            public string ToShortName { get; set; }
        }

        private ICancelable _stationInformationRefresh;
        private ICancelable _stationStatusRefresh;
        private readonly Dictionary<string, string> _stationNameReplacements;

        private StationInformationData _stationInformation { get; set; } = new StationInformationData();
        private StationStatusData _stationStatus { get; set; } = new StationStatusData();

        public BikeShareSystem(Settings.BikeShareSystem settings)
        {
            var client = GbfsClient.GetInstance(settings.ManifestUrl);
            _stationNameReplacements = settings.StationNameReplacements;
            Become(() => Listening(client));
        }

        private void Listening(IGbfsApi client)
        {
            Receive<RefreshManifest>(refresh =>
            {
                _stationInformationRefresh?.Cancel();
                _stationStatusRefresh?.Cancel();
                client.GetManifest()
                    .ContinueWith(manifestTask =>
                    {
                        var manifest = manifestTask.Result;
                        Console.WriteLine($"Got manifest with TTL of {manifest.TimeToLive}");

                        return manifest;
                    },
                    TaskContinuationOptions.ExecuteSynchronously)
                .PipeTo(Self);
            });

            Receive<Manifest>(manifest =>
            {
                var delay = TimeSpan.FromSeconds(manifest.TimeToLive);
                Context.System.Scheduler.ScheduleTellOnce(delay, Self, new RefreshManifest(), Self);

                var language = manifest.Data.ContainsKey("en") ? "en" : manifest.Data.Keys.First();

                Self.Tell(new RefreshStationInformation
                {
                    Manifest = manifest,
                    Language = language,
                });

                Self.Tell(new RefreshStationStatus
                {
                    Manifest = manifest,
                    Language = language,
                });
            });

            Receive<RefreshStationInformation>(refresh =>
            {
                refresh.Manifest.GetStationInformation(client, refresh.Language)
                    .ContinueWith(informationTask =>
                    {
                        return (Refresh: refresh, StationInformation: informationTask.Result);
                    },
                    TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(Self);
            });

            Receive<RefreshStationStatus>(refresh =>
            {
                refresh.Manifest.GetStationStatus(client, refresh.Language)
                    .ContinueWith(statusTask =>
                    {
                        return (Refresh: refresh, StationStatus: statusTask.Result);
                    },
                    TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(Self);
            });

            Receive<(RefreshStationInformation Refresh, StationInformation StationInformation)>(tuple =>
            {
                var delay = TimeSpan.FromSeconds(tuple.StationInformation.TimeToLive);
                _stationInformationRefresh = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, tuple.Refresh, Self);

                _stationInformation = tuple.StationInformation.Data;
                Console.WriteLine($"Station information count: {_stationInformation.Stations.Count}");
            });

            Receive<(RefreshStationStatus Refresh, StationStatus StationStatus)>(tuple =>
            {
                var delay = TimeSpan.FromSeconds(tuple.StationStatus.TimeToLive);
                _stationStatusRefresh = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, tuple.Refresh, Self);

                _stationStatus = tuple.StationStatus.Data;
                Console.WriteLine($"Station status count: {_stationStatus.Stations.Count}");
            });

            Receive<RequestChallenge>(request =>
            {
                var stationStatuses = _stationStatus.Stations
                    .Where(x => x.IsRenting && x.IsReturning && x.IsInstalled);

                var areaOfInterest = new GeoCoordinate(request.AreaOfInterest.Center.Latitude, request.AreaOfInterest.Center.Longitude);
                var all = _stationInformation.Stations
                    .Select(si => new
                    {
                        Coordinate = new GeoCoordinate(si.Lat, si.Lon),
                        Information = si,
                    })
                    .Where(x => x.Coordinate.GetDistanceTo(areaOfInterest) <= request.AreaOfInterest.Radius * 1000)
                    .Join(stationStatuses, x => x.Information.StationId, ss => ss.StationId, (x, status) => new
                    {
                        x.Coordinate,
                        x.Information,
                        Status = status,
                    })
                    .ToList()
                    .AsEnumerable();

                // if a from/to is given, find 5 nearest stations and pick fullest/emptiest
                var from = all;
                if (request.From != null)
                {
                    from = from
                        .OrderBy(x => x.Coordinate.GetDistanceTo(request.From))
                        .Take(5);
                }
                var fromStation = from
                    .OrderBy(x => x.Status.NumBikesAvailable)
                    .First();

                var to = all.Where(x => x != fromStation);
                if (request.To != null)
                {
                    to = to
                        .OrderBy(x => x.Coordinate.GetDistanceTo(request.To))
                        .Take(5);
                }
                to = to.OrderByDescending(x => x.Status.NumDocksAvailable);
                var toStation = to.First();

                Sender.Tell(new Challenge
                {
                    FromShortName = ShortenStationName(fromStation.Information.Name),
                    From = fromStation.Information,
                    ToShortName = ShortenStationName(toStation.Information.Name),
                    To = toStation.Information,
                });
            });

            Self.Tell(new RefreshManifest());
        }

        private string ShortenStationName(string stationName)
        {
            return _stationNameReplacements.Aggregate(stationName, (sn, x) => sn.Replace(x.Key, x.Value));
        }


    }
}
