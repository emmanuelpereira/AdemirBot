using DiscordBot.Domain.Entities;
using DiscordBot.Repository;
using DiscordBot.Utils;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;

namespace DiscordBot
{
    public class Context
    {
        private readonly IMongoDatabase db;
        public Context(IMongoDatabase db)
        {
            this.db = db;
        }

        public IRepository<Membership> memberships { get => db.GetRepository<Membership>("memberships"); }
        public GridFSBucket BgCardBucket
        {
            get =>
            new GridFSBucket(db, new GridFSBucketOptions
            {
                BucketName = "bgcards",
                ChunkSizeBytes = 1048576, // 1MB
                WriteConcern = WriteConcern.WMajority,
                ReadPreference = ReadPreference.Secondary
            });
        }
        public IRepository<MinigameMatch> minigames { get => db.GetRepository<MinigameMatch>("minigame_matches"); }
        public IRepository<Member> members { get => db.GetRepository<Member>("members"); }
        public IRepository<EventPresence> eventPresence { get => db.GetRepository<EventPresence>("event_presence"); }
        public IRepository<BumpConfig> bumpCfg { get => db.GetRepository<BumpConfig>("bump_config"); }
        public IRepository<GuildEvent> events { get => db.GetRepository<GuildEvent>("events"); }
        public IRepository<AdemirConfig> ademirCfg { get => db.GetRepository<AdemirConfig>("ademir_cfg"); }
        public IRepository<BlacklistChatPattern> backlistPatterns { get => db.GetRepository<BlacklistChatPattern>("blacklist_patterns"); }
        public IRepository<DenunciaConfig> denunciaCfg { get => db.GetRepository<DenunciaConfig>("denuncia_config"); }
        public IRepository<Message> messagelog { get => db.GetRepository<Message>("messages"); }
        public IRepository<Denuncia> denuncias { get => db.GetRepository<Denuncia>("denuncias"); }
        public IRepository<Bump> bumps { get => db.GetRepository<Bump>("bumps"); }
        public IRepository<Macro> macros { get => db.GetRepository<Macro>("macros"); }
        public IRepository<Track> tracks { get => db.GetRepository<Track>("tracks"); }
        public IRepository<ServerNumberProgression> progression { get => db.GetRepository<ServerNumberProgression>("progression"); }
        public IRepository<ThreadChannel> threads { get => db.GetRepository<ThreadChannel>("threads"); }
        public IRepository<UserMention> userMentions { get => db.GetRepository<UserMention>("user_mentions"); }
        public IRepository<UserRecommendation> userRecomms { get => db.GetRepository<UserRecommendation>("user_recommendations"); }

        public async Task<IClientSessionHandle> StartSessionAsync()
        {
            return await db.Client.StartSessionAsync();
        }
        public IClientSessionHandle StartSession()
        {
            return db.Client.StartSession();
        }
    }
}
