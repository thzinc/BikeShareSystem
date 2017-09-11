using System;
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

        private ICancelable _stationInformationRefresh;
        private ICancelable _stationStatusRefresh;

        private StationInformationData _stationInformation { get; set; } = new StationInformationData();
        private StationStatusData _stationStatus { get; set; } = new StationStatusData();

        public BikeShareSystem(string manifestUrl)
        {
            var client = GbfsClient.GetInstance(manifestUrl);
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

            Self.Tell(new RefreshManifest());
        }
    }
}
