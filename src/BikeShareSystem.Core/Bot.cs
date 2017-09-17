using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using GoogleApi.Entities.Places.Search.Common.Enums;
using GoogleApi.Entities.Places.Search.NearBy.Request;
using LinqToTwitter;

namespace BikeShareSystem
{
    public class Bot : ReceiveActor, IWithUnboundedStash
    {
        public class Poll { }

        public IStash Stash { get; set; }

        private Settings.Bot _settings;
        private string _googleApiKey;
        public Bot(Settings.Bot settings, string googleApiKey)
        {
            _settings = settings;
            _googleApiKey = googleApiKey;
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
            Receive<LinqToTwitter.Status>(tweet =>
            {
                var fromScreenName = tweet.User.ScreenNameResponse;

                var conversation = Context.Child(fromScreenName);
                if (Equals(conversation, ActorRefs.Nobody))
                {
                    conversation = Context.ActorOf(Props.Create(() => new Conversation(_settings, _googleApiKey, twitterContext)), fromScreenName);
                }
                conversation.Tell(tweet);
            });

            var self = Self;
            twitterContext.Streaming
                .Where(stream => stream.Type == StreamingType.User && stream.AllReplies)
                .StartAsync(stream =>
                {
                    switch (stream.Entity)
                    {
                        case LinqToTwitter.Status tweet:
                            if (tweet.User.ScreenNameResponse != _settings.Twitter.ScreenName)
                            {
                                self.Tell(tweet);
                            }
                            break;
                    }
                    return Task.CompletedTask;
                });
        }
    }
}
