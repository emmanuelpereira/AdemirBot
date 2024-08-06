using MongoDB.Bson.Serialization.Attributes;

namespace DiscordBot.Domain.Entities
{
    public class Message : IEntity
    {
        [BsonId]
        public ulong MessageId { get; set; }
        public ulong UserId { get; set; }
        public ulong GuildId { get; set; }
        public ulong ChannelId { get; set; }
        public string Content { get; set; }
        public DateTime MessageDate { get; set; }
        public long MessageLength { get; set; }
        public Dictionary<string, int> Reactions { get; set; }
        public List<string> Attachments { get; set; }
        public List<string> EmbedsJson { get; set; }
    }
}