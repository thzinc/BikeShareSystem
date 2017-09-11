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
            Receive<Bot.Poll>(_ =>
            {
                var self = Self;
                twitterContext.Streaming
                    .Where(stream => stream.Type == StreamingType.User)
                    .StartAsync(stream => {
                        // Console.WriteLine($"stream: {stream.Content} ({stream.EntityType}, {stream.Entity?.GetType().FullName})");
                        switch (stream.Entity)
                        {
                            case LinqToTwitter.Status tweet:
                                self.Tell(tweet);
                                break;
                        }
                        return Task.CompletedTask;
                    });
            });

            Receive<LinqToTwitter.Status>(tweet =>
            {
                Console.WriteLine($"{tweet.Text}");
            });

            Self.Tell(new Bot.Poll());
        }
    }
}
