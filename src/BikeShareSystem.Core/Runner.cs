using System;
using System.IO;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BikeShareSystem
{
    public class Runner : IDisposable
    {
        private ActorSystem _actorSystem;
        private ManualResetEventSlim _mre;
        private readonly Settings _settings;

        public Runner(string settingsPath)
        {
            _settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsPath), new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                }
            });
        }

        public void Start()
        {
            _actorSystem = ActorSystem.Create("BikeShareSystemCore");
            _actorSystem.ActorOf(Props.Create(() => new Core(_settings)));
            _mre = new ManualResetEventSlim();
        }

        public void Wait()
        {
            _mre.Wait();
        }

        public void Stop()
        {
            _mre.Set();
        }

        public void Dispose()
        {
            if (_actorSystem != null) _actorSystem.Dispose();
        }
    }
}
