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

        public class RequestStatus
        {
            public Settings.CircularArea AreaOfInterest { get; set; }
        }

        public class Status
        {
            public int Bikes { get; set; }
            public int Docks { get; set; }
            public int Stations { get; set; }
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
                client.GetManifest().PipeTo(Self);
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
            });

            Receive<(RefreshStationStatus Refresh, StationStatus StationStatus)>(tuple =>
            {
                var delay = TimeSpan.FromSeconds(tuple.StationStatus.TimeToLive);
                _stationStatusRefresh = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, tuple.Refresh, Self);

                _stationStatus = tuple.StationStatus.Data;
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
                    .Join(stationStatuses, x => x.Information.StationId, ss => ss.StationId, (x, status) => (
                        Coordinate: x.Coordinate,
                        Information: x.Information,
                        Status: status
                    ))
                    .ToList()
                    .AsEnumerable();

                var fromStation = GetCoordinate(all, request.From, x => x.NumBikesAvailable);
                var toStation = GetCoordinate(all, request.To, x => x.NumDocksAvailable, fromStation.Information);

                Sender.Tell(new Challenge
                {
                    FromShortName = ShortenStationName(fromStation.Information.Name),
                    From = fromStation.Information,
                    ToShortName = ShortenStationName(toStation.Information.Name),
                    To = toStation.Information,
                });
            });

            Receive<RequestStatus>(request =>
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
                    .ToList();

                Sender.Tell(new Status
                {
                    Bikes = all.Sum(x => x.Status.NumBikesAvailable),
                    Docks = all.Sum(x => x.Status.NumDocksAvailable),
                    Stations = all.Count,
                });
            });

            Self.Tell(new RefreshManifest());
        }

        private string ShortenStationName(string stationName)
        {
            if (_stationNameReplacements.Count == 0) return stationName;

            return _stationNameReplacements.Aggregate(stationName, (sn, x) => sn.Replace(x.Key, x.Value));
        }

        private static (GeoCoordinate Coordinate, Station Information, Gbfs.Net.v1.Status Status) GetCoordinate<TOrderBy>(
            IEnumerable<(GeoCoordinate Coordinate, Station Information, Gbfs.Net.v1.Status Status)> stations,
            GeoCoordinate referencePoint,
            Func<Gbfs.Net.v1.Status, TOrderBy> orderBySelector,
            Station excludedStation = null)
        {
            var selectedStations = stations;

            if (excludedStation != null) {
                selectedStations = selectedStations.Where(x => x.Information != excludedStation);
            }

            if (referencePoint != null)
            {
                var nearestStations = selectedStations
                    .Select(x => (Distance: x.Coordinate.GetDistanceTo(referencePoint), Item: x))
                    .OrderBy(x => x.Distance)
                    .Take(5)
                    .ToList()
                    .AsEnumerable();

                var maxNearbyInMeters = 500;
                var min = nearestStations.Min(x => x.Distance);
                var range = nearestStations.Max(x => x.Distance) - min;
                if (range > maxNearbyInMeters)
                {
                    nearestStations = nearestStations.Where(x => x.Distance <= min + maxNearbyInMeters);
                }

                selectedStations = nearestStations.Select(x => x.Item);
            }
            var selectedStation = selectedStations
                .OrderByDescending(x => orderBySelector(x.Status))
                .First();

            return selectedStation;
        }
    }
}
