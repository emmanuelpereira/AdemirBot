using Discord;
using Discord.WebSocket;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace DiscordBot.Domain.Entities
{
    public class Member : IEntity
    {
        [BsonId]
        public Guid Id { get; set; }
        public ulong MemberId { get; set; }
        public ulong GuildId { get; set; }
        public string MemberUserName { get; set; }
        public string MemberNickname { get; set; }
        public DateTime? LastMessageTime { get; set; }
        public DateTime DateJoined { get; set; }
        public DateTime DateLastJoined { get; set; }
        public DateTime? LastActivityMentionTime { get; set; }
        public DateTime? DateBanned { get; set; }
        public long XP { get; set; }
        public long MessageCount { get; set; }
        public long BumpCount { get; set; }
        public TimeSpan VoiceTime { get; set; }
        public TimeSpan MutedTime { get; set; }
        public TimeSpan StreamingTime { get; set; }
        public TimeSpan VideoTime { get; set; }
        public int Level { get; set; }
        public int LurkrLevel { get; set; }
        public long LurkrXP { get; set; }
        public int EventsPresent { get; set; }
        public string ReasonBanned { get; set; }
        public string CardBackgroundFile { get; set; }
        public string AccentColor { get; set; }
        public ulong WelcomeMessageId { get; set; }
        public ulong[] RoleIds { get; set; }
        public bool IsBot { get; set; }
        public bool ProtectionWhiteListed { get; set; }

        internal static Member FromGuildUser(IGuildUser user)
        {
            return new Member
            {
                Id = Guid.NewGuid(),
                GuildId = user.GuildId,
                MemberId = user.Id,
                MemberUserName = user.Username,
                MemberNickname = user.GlobalName,
                RoleIds = user.RoleIds.ToArray(),
                DateLastJoined = user.JoinedAt.GetValueOrDefault().UtcDateTime,
            };
        }
    }
}