using Discord;
using Discord.Interactions;
using DiscordBot.Utils;
using DiscordBot.Modules.Modals;
using System.Globalization;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Modules
{
    public class AdminModule : InteractionModuleBase
    {
        private readonly Context db;

        public AdminModule(Context context)
        {
            db = context;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("massban", "Banir membros em massa.", runMode: RunMode.Async)]
        public async Task MassBan()
            => await Context.Interaction.RespondWithModalAsync<MassBanModal>("mass_ban");

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("masskick", "Expulsar membros em massa.", runMode: RunMode.Async)]
        public async Task MassKick()
            => await Context.Interaction.RespondWithModalAsync<MassKickModal>("mass_kick");

        [ModalInteraction("mass_ban")]
        public async Task BanResponse(MassBanModal modal)
        {
            var memberIds = StringUtils.SplitAndParseMemberIds(modal.Membros);
            foreach (var id in memberIds)
            {
                await (await Context.Client.GetGuildAsync(Context.Guild.Id)).AddBanAsync(id);
            }
            await RespondAsync($"{memberIds.Length} Usuários Banidos.");
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("ban", "Bane um membro")]
        public async Task Ban([Summary("usuario", "Usuario a ser banido")] IUser user, string motivo = null)
        {
            if (user.Id == Context.User.Id)
            {
                await RespondAsync($"Desculpe {user.Username}, eu me recuso a te banir.", ephemeral: true);
                return;
            }
            await (await Context.Client.GetGuildAsync(Context.Guild.Id)).AddBanAsync(user.Id, reason: motivo);
            await RespondAsync($"**{user.Username}** foi banido do servidor.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("kick", "Expulsa um membro")]
        public async Task Kick([Summary("usuario", "Usuario a ser expulso")] IUser user, string motivo = null)
        {
            if (user.Id == Context.User.Id)
            {
                await RespondAsync($"Desculpe {user.Username}, eu me recuso a te expulsar.", ephemeral: true);
                return;
            }
            await (await Context.Guild.GetUserAsync(user.Id)).KickAsync(motivo);
            await RespondAsync($"**{user.Username}** foi expulso do servidor.", ephemeral: true);
        }

        [ModalInteraction("mass_kick")]
        public async Task KickResponse(MassKickModal modal)
        {
            var memberIds = StringUtils.SplitAndParseMemberIds(modal.Membros);
            foreach (var id in memberIds)
            {
                var user = await (await Context.Client.GetGuildAsync(Context.Guild.Id)).GetUserAsync(id);
                if (user != null)
                    await user.KickAsync();
            }
            await RespondAsync($"{memberIds.Length} Usuários Expulsos.", ephemeral: true);
        }


        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("create-message", "Criar mensagem.", runMode: RunMode.Async)]
        public async Task CreateMessage()
            => await Context.Interaction.RespondWithModalAsync<MessageModal>("create_message");

        [ModalInteraction(@"create_message")]
        public async Task CreateMessage(MessageModal modal)
        {
            await Context.Channel.SendMessageAsync(modal.Mensagem);
            await RespondAsync($"Mensagem enviada", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [MessageCommand("Editar mensagem")]
        public async Task CriarEventoPalco(IMessage msg)
        {
            await Context.Interaction.RespondWithModalAsync($"edit_message:{msg.Id}", new EventModal
            {
                Title = $"Editar mensagem",
                Nome = msg.Content
            });
        }

        [ModalInteraction(@"edit_message:*", TreatAsRegex = true)]
        public async Task EditMessageMessage(MessageModal modal)
        {
            string id = ((IModalInteraction)Context.Interaction).Data.CustomId;
            var msgid = ulong.Parse(Regex.Match(id, @"edit_message:(\d+)").Groups[1].Value);
            await Context.Channel.ModifyMessageAsync(msgid,  m => m.Content = modal.Mensagem);
            await RespondAsync($"Mensagem editada", ephemeral: true);
        }


        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("purge", "Remover uma certa quantidade de mensagens de um canal", runMode: RunMode.Async)]
        public async Task PurgeMessages(
            [Summary(description: "Quantidade de mensgens a excluir")] int qtd,
            [Summary("canal", "Canal a ser limpo")] IMessageChannel channel = default)
        {
            await DeferAsync();
            channel = channel ?? Context.Channel;
            IEnumerable<IMessage> messages = await channel.GetMessagesAsync(qtd).FlattenAsync();
            await ((ITextChannel)Context.Channel).DeleteMessagesAsync(messages);
            await DeleteOriginalResponseAsync();
        }


        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("purge-user-messages", "Remover uma certa quantidade de mensagens de um usuário no canal", runMode: RunMode.Async)]
        public async Task PurgeMessagesFromUser(
            [Summary("usuario", "Usuário a limpar do chat")] IUser usuario,
            [Summary("canal", "Canal a ser limpo")] IMessageChannel channel = default)
        {
            await DeferAsync();
            channel = channel ?? Context.Channel;
            var msgs = await channel.GetMessagesAsync(1).FlattenAsync();

            while (msgs.Count() > 0)
            {
                var message = msgs.First();
                msgs = (await channel.GetMessagesAsync(message.Id, dir: Direction.Before, limit: 100)
                        .FlattenAsync())
                        .OrderBy(a => a.Timestamp)
                        .ToArray();

                var filteredMesssages = msgs.Where(m => m.Author?.Id == usuario.Id);
                await ModifyOriginalResponseAsync(a => a.Content = $"Processando mensagens do dia {message.Timestamp.DateTime:dd/MM/yy}");
                foreach (var m in filteredMesssages)
                    await ((ITextChannel)channel).DeleteMessageAsync(m, new RequestOptions
                    {
                        RetryMode = RetryMode.AlwaysRetry,
                        Timeout = Timeout.Infinite,
                        AuditLogReason = $"/purge {usuario.Username} em #{channel.Name}"
                    });

            }

            await DeleteOriginalResponseAsync();
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("enable-audio-xp", "Habilitar XP de Audio", runMode: RunMode.Async)]
        public async Task EnableAudioXP()
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    EnableAudioXP = true,
                });
            }
            else
            {
                config.EnableAudioXP = true;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("XP de Audio habilitada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("disable-audio-xp", "Desabilitar XP de Audio", runMode: RunMode.Async)]
        public async Task DisableAudioXP()
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    EnableAudioXP = false,
                });
            }
            else
            {
                config.EnableAudioXP = false;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("XP de Audio desabilitada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("enable-mention-xp", "Habilitar XP de menção", runMode: RunMode.Async)]
        public async Task EnableMentionXP()
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    EnableMentionXP = true,
                });
            }
            else
            {
                config.EnableMentionXP = true;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("XP de menção habilitada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("disable-mention-xp", "Desabilitar XP de menção", runMode: RunMode.Async)]
        public async Task DisableMentionXP()
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    EnableMentionXP = false,
                });
            }
            else
            {
                config.EnableAudioXP = false;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("XP de menção desabilitada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("set-voice-event-channel", "Define canal de voz dos eventos e realoca os eventos agendados", runMode: RunMode.Async)]
        public async Task SetEventChannel(
            [Summary(description: "Canal de voz")] IVoiceChannel channel)
        {
            var cfg = await db.ademirCfg.Find(a => a.GuildId == Context.Guild.Id).FirstOrDefaultAsync();
            if (cfg == null)
            {
                await RespondAsync("Configuração de eventos invalida.", ephemeral: true);
                return;
            }
            else
            {
                cfg.EventVoiceChannelId = channel.Id;
                await db.ademirCfg.UpsertAsync(cfg, a => a.GuildId == Context.Guild.Id);
                await RespondAsync("Canal de eventos de voz salvo.", ephemeral: true);

                var events = await Context.Guild.GetEventsAsync();

                foreach (var @event in events.Where(a => a.Type == GuildScheduledEventType.Voice))
                {
                    if (@event.Status == GuildScheduledEventStatus.Scheduled)
                        await @event.ModifyAsync(a => a.ChannelId = channel.Id);
                }
            }
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("set-stage-event-channel", "Define canal de palco dos eventos e realoca os eventos agendados", runMode: RunMode.Async)]
        public async Task SetStageEventChannel(
            [Summary(description: "Canal Palco")] IStageChannel channel)
        {
            var cfg = await db.ademirCfg.Find(a => a.GuildId == Context.Guild.Id).FirstOrDefaultAsync();
            if (cfg == null)
            {
                await RespondAsync("Configuração de eventos invalida.", ephemeral: true);
                return;
            }
            else
            {
                cfg.EventStageChannelId = channel.Id;
                await db.ademirCfg.UpsertAsync(cfg, a => a.GuildId == Context.Guild.Id);
                await RespondAsync("Canal de eventos palco salvo.", ephemeral: true);

                var events = await Context.Guild.GetEventsAsync();

                foreach (var @event in events.Where(a => a.Type == GuildScheduledEventType.Stage))
                {
                    if (@event.Status == GuildScheduledEventStatus.Scheduled)
                        await @event.ModifyAsync(a => a.ChannelId = channel.Id);
                }
            }
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [MessageCommand("Criar Evento de Voz")]
        public async Task CriarEvento(IMessage msg)
        {
            var content = msg.Content;
            var data = DateTime.Today.AddHours(DateTime.Now.Hour + 1);
            try
            {
                if (content.Matches(@"([0-9]{1,2})\/([0-9]{1,2})(?:\/([0-9]{2,4}))?"))
                {
                    var match = content.Match(@"([0-9]{1,2})\/([0-9]{1,2})(?:\/([0-9]{1,4}))?").Groups;
                    var dia = match[1];
                    var mes = match[2];
                    var ano = match[3];

                    if (ano.Success)
                    {
                        data = DateTime.ParseExact($"{dia}/{mes}/{ano}", "dd/MM/yyyy", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        data = DateTime.ParseExact($"{dia}/{mes}", "dd/MM", CultureInfo.InvariantCulture);
                    }
                }

                if (content.Matches(@"(\d\d)(?:\:|h|H)(\d\d)"))
                {
                    var match = content.Match(@"(\d\d)(?:\:|h|H)(\d\d)").Groups;
                    var hora = TimeSpan.ParseExact($"{match[1].Value}:{match[2].Value}", @"hh\:mm", CultureInfo.InvariantCulture);
                    data = data.Date + hora;
                }
                else if (content.Matches(@"(\d\d)\s?(?:hrs|Hrs|hr|horas|Horas|H|h)\s"))
                {
                    var match = content.Match(@"(\d\d)\s?(?:hrs|Hrs|hr|horas|Horas|H|h)\s").Groups;
                    var hora = TimeSpan.ParseExact(match[1].Value, "HH", CultureInfo.InvariantCulture);
                    data = data.Date + hora;
                }
            }
            catch
            {
                ;
            }
            var nome = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).First();
            await Context.Interaction.RespondWithModalAsync($"criar_evento_msg:{msg.Id}", new EventModal
            {
                Title = $"Criar Evento",
                Nome = nome,
                DataHora = data.ToString("dd/MM/yyyy HH:mm"),
                Descricao = content,
            });
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [MessageCommand("Criar Evento Palco")]
        public async Task CriarEventoPalco(IMessage msg)
        {
            var content = msg.Content;
            var data = DateTime.Today.AddHours(DateTime.Now.Hour + 1);
            try
            {
                if (content.Matches(@"([0-9]{1,2})\/([0-9]{1,2})(?:\/([0-9]{2,4}))?"))
                {
                    var match = content.Match(@"([0-9]{1,2})\/([0-9]{1,2})(?:\/([0-9]{1,4}))?").Groups;
                    var dia = match[1];
                    var mes = match[2];
                    var ano = match[3];

                    if (ano.Success)
                    {
                        data = DateTime.ParseExact($"{dia}/{mes}/{ano}", "dd/MM/yyyy", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        data = DateTime.ParseExact($"{dia}/{mes}", "dd/MM", CultureInfo.InvariantCulture);
                    }
                }

                if (content.Matches(@"(\d\d)(?:\:|h|H)(\d\d)"))
                {
                    var match = content.Match(@"(\d\d)(?:\:|h|H)(\d\d)").Groups;
                    var hora = TimeSpan.ParseExact($"{match[1].Value}:{match[2].Value}", @"hh\:mm", CultureInfo.InvariantCulture);
                    data = data.Date + hora;
                }
                else if (content.Matches(@"(\d\d)\s?(?:hrs|Hrs|hr|horas|Horas|H|h)\s"))
                {
                    var match = content.Match(@"(\d\d)\s?(?:hrs|Hrs|hr|horas|Horas|H|h)\s").Groups;
                    var hora = TimeSpan.ParseExact(match[1].Value, "HH", CultureInfo.InvariantCulture);
                    data = data.Date + hora;
                }
            }
            catch
            {
                ;
            }
            var nome = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).First();
            await Context.Interaction.RespondWithModalAsync($"criar_evento_palco_msg:{msg.Id}", new EventModal
            {
                Title = $"Criar Evento",
                Nome = nome,
                DataHora = data.ToString("dd/MM/yyyy HH:mm"),
                Descricao = content,
            });
        }

        [ModalInteraction(@"criar_evento_msg:*", TreatAsRegex = true)]
        public async Task CriarEventoModal(EventModal modal)
        {
            var cfg = await db.ademirCfg.Find(a => a.GuildId == Context.Guild.Id).FirstOrDefaultAsync();
            if (cfg == null)
            {
                await RespondAsync("Configuração de eventos invalida.", ephemeral: true);
                return;
            }

            string id = ((IModalInteraction)Context.Interaction).Data.CustomId;
            var msgid = ulong.Parse(Regex.Match(id, @"criar_evento_msg:(\d+)").Groups[1].Value);
            await DeferAsync(ephemeral: true);
            var msg = await Context.Channel.GetMessageAsync(msgid);

            Image? imagem = null;

            using var ms = new MemoryStream();
            if (msg.Attachments.Count > 0 && msg.Attachments.First().Url != null)
            {

                using var client = new HttpClient();
                var info = await client.GetStreamAsync(msg.Attachments.First().Url);
                info.CopyTo(ms);
                ms.Position = 0;
                imagem = new Image(ms);
            }
            try
            {
                var channels = await Context.Guild.GetChannelsAsync();
                var data = DateTime.ParseExact(modal.DataHora, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                modal.Nome = modal.Nome.Trim();
                await Context.Guild.CreateEventAsync(
                    modal.Nome,
                    data,
                    GuildScheduledEventType.Voice,
                    GuildScheduledEventPrivacyLevel.Private,
                    modal.Descricao,
                    coverImage: imagem,
                    channelId: cfg.EventVoiceChannelId);

                await Context.Interaction.ModifyOriginalResponseAsync(a => a.Content = $"Evento de voz criado.");
            }
            catch (Exception ex)
            {

                await Context.Interaction.ModifyOriginalResponseAsync(a => a.Content = ex.ToString());
            }
        }

        [ModalInteraction(@"criar_evento_palco_msg:*", TreatAsRegex = true)]
        public async Task CriarEventoPalcoModal(EventModal modal)
        {
            var cfg = await db.ademirCfg.Find(a => a.GuildId == Context.Guild.Id).FirstOrDefaultAsync();
            if (cfg == null)
            {
                await RespondAsync("Configuração de eventos invalida.", ephemeral: true);
                return;
            }

            string id = ((IModalInteraction)Context.Interaction).Data.CustomId;
            var msgid = ulong.Parse(Regex.Match(id, @"criar_evento_palco_msg:(\d+)").Groups[1].Value);
            await DeferAsync(ephemeral: true);
            var msg = await Context.Channel.GetMessageAsync(msgid);

            Image? imagem = null;

            using var ms = new MemoryStream();
            if (msg.Attachments.Count > 0 && msg.Attachments.First().Url != null)
            {

                using var client = new HttpClient();
                var info = await client.GetStreamAsync(msg.Attachments.First().Url);
                info.CopyTo(ms);
                ms.Position = 0;
                imagem = new Image(ms);
            }
            try
            {
                var channels = await Context.Guild.GetChannelsAsync();
                var data = DateTime.ParseExact(modal.DataHora, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                modal.Nome = modal.Nome.Trim();
                await Context.Guild.CreateEventAsync(
                    modal.Nome,
                    data,
                    GuildScheduledEventType.Stage,
                    GuildScheduledEventPrivacyLevel.Private,
                    modal.Descricao,
                    coverImage: imagem,
                    channelId: cfg.EventStageChannelId);

                await Context.Interaction.ModifyOriginalResponseAsync(a => a.Content = $"Evento de palco criado.");
            }
            catch (Exception ex)
            {

                await Context.Interaction.ModifyOriginalResponseAsync(a => a.Content = ex.ToString());
            }
        }
    }
}
