using DiscordBot.Domain.Lurkr;
using DiscordBot.Domain.ValueObjects;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using OpenAI.ObjectModels.RequestModels;
using SkiaSharp;

namespace DiscordBot.Domain.Entities
{
    public class AdemirConfig : IEntity
    {
        [BsonId]
        public Guid AdemirConfigId { get; set; }
        public ulong GuildId { get; set; }
        public int? GlobalVolume { get; set; }
        public PlaybackState PlaybackState { get; set; }
        public PlayMode PlayMode { get; set; }
        public ulong? VoiceChannel { get; set; }
        [BsonRepresentation(BsonType.Double, AllowTruncation = true)]
        public float? Position { get; set; }
        public int? CurrentTrack { get; set; }
        public ulong ActiveTalkerRole { get; set; }
        public ulong EventInviteRole { get; set; }
        public ulong AdemirRoleId { get; set; }
        public ulong AutoRoleId { get; set; }
        public ulong MinorRoleId { get; set; }
        public int AdemirConversationRPM { get; set; }
        public ulong EventVoiceChannelId { get; set; }
        public string HighestGptClass { get; set; }
        public bool Premium { get; set; }
        public bool EnableAudioXP { get; set; }
        public bool EnableMentionXP { get; set; }
        public bool EnableRoleRewards { get; set; }
        public RoleReward[] RoleRewards { get; set; }
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfArrays)]
        public Dictionary<ulong, double> ChannelXpMultipliers { get; set; }
        public byte[] WelcomeBanner { get; set; }
        public ulong EventStageChannelId { get; set; }
        public long MinRecommendationLevel { get; set; }
        public bool KickNewAccounts { get; set; }
        public ChatMessage[] TrainingMessages { get; set; }
        public bool EnableBotUserNameDetection { get; set; }
    }
}