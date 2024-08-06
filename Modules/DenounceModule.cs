using Discord;
using Discord.Interactions;
using DiscordBot.Domain.Entities;
using DiscordBot.Services;
using MongoDB.Driver;
using System.Text.RegularExpressions;

namespace DiscordBot.Modules
{
    public class DenounceModule : InteractionModuleBase
    {
        private readonly Context db;
        private readonly GuildPolicyService policySvc;

        public DenounceModule(Context context, GuildPolicyService policySvc)
        {
            db = context;
            this.policySvc = policySvc;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("config-log", "Configurar o canal de logs.", runMode: RunMode.Async)]
        public async Task ConfigDenuncias(
            [Summary(description: "Canal de logs")] IChannel canal)
        {
            var config = await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id);
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    GuildId = Context.Guild.Id,
                    LogChannelId = canal.Id
                });
            }
            else 
            {
                config.LogChannelId = canal.Id;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("Canal de logs configurado.", ephemeral: true);
        }

        [SlashCommand("denunciar", "Denunciar algo que fere as regras do servidor.", runMode: RunMode.Async)]
        public async Task Denunciar(
            [Summary(description: "Usuário a denunciar")] IUser usuario,
            [Summary(description: "Relato da denuncia")] string relato,
            [Summary(description: "Testemunha")] IUser testemunha = default,
            [Summary(description: "Print da conversa")] IAttachment print = default,
            [Summary(description: "Postar anonimamente")] bool anonimato = default)
        {
            var config = await db.denunciaCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id);

            if (config == null)
            {
                await RespondAsync($"O canal de denúncias ainda não está configurado.", ephemeral: true);
                return;
            }
            await RespondAsync($"Não se preocupe, {Context.User.Username}. Esta informação será mantida em sigilo.", ephemeral: true);

            var guildId = Context.Guild.Id;
            var channels = await Context.Guild.GetChannelsAsync();
            var canal = (IMessageChannel)channels.First(a => a.Id == config.ChannelId);

            var denunciado = usuario;

            var msg = await canal.SendMessageAsync("", false, new EmbedBuilder()
            {
                Fields = new List<EmbedFieldBuilder>
                {
                    new EmbedFieldBuilder().WithName("Denunciado").WithIsInline(true).WithValue($"{denunciado}"),
                    new EmbedFieldBuilder().WithName("Denúncia").WithValue(relato),
                },
                ImageUrl = print?.Url,
                Description = anonimato ? "" : $"|| Denúnciado por: {Context.User.Mention} " + (testemunha == null ? "" : $"Testemunha: {testemunha.Mention}") + " ||"
            }.Build());

            await db.denuncias.AddAsync(new Denuncia
            {
                DenunciaId = Guid.NewGuid(),
                ReportId = msg.Id,
                GuildId = guildId,
                DenunciaDate = DateTime.Now,
                DenunciadoUserId = denunciado.Id,
                DenuncianteUserId = Context.User.Id,
                PrintUrl = print?.Url,
                TestemunhaUserId = testemunha?.Id
            });
        }


        [MessageCommand("Denunciar")]
        public async Task Denunciar(IMessage msg)
        {
            var config = await db.denunciaCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id);

            if (config == null)
            {
                await RespondAsync($"O canal de denúncias ainda não está configurado.", ephemeral: true);
                return;
            }
            await RespondAsync($"Não se preocupe, {Context.User.Username}. Esta informação será mantida em sigilo.", ephemeral: true);

            var guildId = Context.Guild.Id;
            var channels = await Context.Guild.GetChannelsAsync();
            var canal = (IMessageChannel)channels.First(a => a.Id == config.ChannelId);

            var denunciado = msg.Author;

            var m = await canal.SendMessageAsync("", false, new EmbedBuilder()
            {
                Fields = new List<EmbedFieldBuilder>
                {
                    new EmbedFieldBuilder().WithName("Denunciado").WithIsInline(true).WithValue($"{denunciado}"),
                    new EmbedFieldBuilder().WithName("Denúncia").WithValue(msg.CleanContent),
                },
                Description = $"|| Denúnciado por: {Context.User.Mention} || "
            }.Build());

            await db.denuncias.AddAsync(new Denuncia
            {
                DenunciaId = Guid.NewGuid(),
                GuildId = guildId,
                Conteudo = msg.CleanContent,
                DenunciaDate = DateTime.Now,
                DenunciadoUserId = denunciado.Id,
                DenuncianteUserId = Context.User.Id,
            });
        }


        [RequireUserPermission(GuildPermission.Administrator)]
        [MessageCommand("Blacklist")]
        public async Task Blacklist(IMessage msg)
        {
            await RespondAsync($"Padrão colocado na lista negra.", ephemeral: true);

            var guildId = Context.Guild.Id;
            await db.backlistPatterns.AddAsync(new BlacklistChatPattern
            {
                PatternId = Guid.NewGuid(),
                GuildId = guildId,
                Pattern = Regex.Escape(msg.Content)
            });
            await policySvc.BuscarPadroesBlacklistados(Context.Guild);
        }
    }
}
