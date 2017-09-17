using Akka.Actor;

namespace BikeShareSystem
{
    public class Core : ReceiveActor
    {
        public Core(Settings settings)
        {
            Become(() => StartingActors(settings));
        }

        private void StartingActors(Settings settings)
        {
            settings.BikeShareSystems.ForEach(bss =>
                Context.ActorOf(
                    Props.Create(() => new BikeShareSystem(bss)),
                    bss.SystemId));
            
            settings.Bots.ForEach(bot =>
                Context.ActorOf(
                    Props.Create(() => new Bot(bot, settings.GoogleApiKey)),
                    bot.Twitter.ScreenName));
        }
    }
}
