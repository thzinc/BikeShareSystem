using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using LinqToTwitter;

namespace BikeShareSystem
{
    public class Bot : ReceiveActor, IWithUnboundedStash
    {

        public class Poll { }

        public IStash Stash { get; set; }

        private Settings.Bot _settings;
        public Bot(Settings.Bot settings)
        {
            _settings = settings;
            Become(Authorizing);
        }

        private void Authorizing()
        {
            Receive<TwitterContext>(twitterContext =>
            {
                Become(() => Connected(twitterContext));
                Stash.UnstashAll();
            });

            ReceiveAny(_ => Stash.Stash());

            async Task<TwitterContext> GetTwitterContext()
            {
                var auth = new SingleUserAuthorizer
                {
                    CredentialStore = new SingleUserInMemoryCredentialStore
                    {
                        ConsumerKey = _settings.Twitter.ConsumerKey,
                        ConsumerSecret = _settings.Twitter.ConsumerSecret,
                        AccessToken = _settings.Twitter.AccessTokenKey,
                        AccessTokenSecret = _settings.Twitter.AccessTokenSecret,
                    },
                };

                await auth.AuthorizeAsync();

                return new TwitterContext(auth);
            }

            GetTwitterContext().PipeTo(Self);
        }

        private void Connected(TwitterContext twitterContext)
        {
            ICancelable poll = null;
            ReceiveAsync<Bot.Poll>(async _ =>
            {
                var rateLimits = await twitterContext.Help
                    .Where(help => help.Type == HelpType.RateLimits)
                    .SingleOrDefaultAsync();

                var limit = rateLimits.RateLimits["statuses"].Single(x => x.Resource == "/statuses/mentions_timeline");
                var delay = TimeSpan.FromSeconds(5);
                Console.WriteLine($"Remaining limit: {limit.Remaining}/{limit.Limit}");
                if (limit.Remaining < 10)
                {
                    poll?.Cancel();
                    var reset = DateTimeOffset.FromUnixTimeSeconds((long)limit.Reset);
                    delay = reset - DateTimeOffset.Now;
                }

                var tweets = await twitterContext.Status
                    .Where(tweet => tweet.Type == StatusType.Mentions)
                    .Where(tweet => tweet.Text.Contains("challenge me"))
                    .ToListAsync();

                tweets.ForEach(Self.Tell);

                Console.WriteLine($"Next poll in {delay}");
                poll = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, new Bot.Poll(), Self);
            });

            Receive<LinqToTwitter.Status>(tweet =>
            {
                Console.WriteLine($"{tweet.Text}");
            });

            Self.Tell(new Bot.Poll());
        }
    }
}
