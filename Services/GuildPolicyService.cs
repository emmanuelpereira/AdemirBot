using Discord;
using Discord.WebSocket;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.ValueObjects;
using DiscordBot.Utils;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DiscordBot.Services
{
    public class GuildPolicyService : Service
    {
        private Context _db;
        private DiscordShardedClient _client;
        private ILogger<GuildPolicyService> _log;
        private Dictionary<ulong, List<string>> backlistPatterns = new Dictionary<ulong, List<string>>();
        private Dictionary<ulong, long> msgSinceAdemirCount = new Dictionary<ulong, long>();
        private Dictionary<ulong, ulong[]> channelsBypassFlood = new Dictionary<ulong, ulong[]>();
        private Dictionary<ulong, bool> lockServer = new Dictionary<ulong, bool>();
        List<IMessage> mensagensUltimos5Minutos = new List<IMessage>();

        public GuildPolicyService(Context context, DiscordShardedClient client, ILogger<GuildPolicyService> logger)
        {
            _db = context;
            _client = client;
            _log = logger;
        }

        public override void Activate()
        {
            var conventionPack = new ConventionPack { new IgnoreExtraElementsConvention(true) };
            ConventionRegistry.Register("IgnoreExtraElements", conventionPack, type => true);
            BindEventListeners();
        }

        private void BindEventListeners()
        {
            _client.MessageReceived += _client_MessageReceived;
            _client.MessageUpdated += _client_MessageUpdated;
            _client.UserJoined += _client_UserJoined;
            _client.UserLeft += _client_UserLeft;
            _client.ShardReady += _client_ShardReady;
            _client.ReactionAdded += _client_ReactionAdded;
            _client.GuildMemberUpdated += _client_GuildMemberUpdated;
            _client.UserBanned += _client_UserBanned;
            _client.UserUnbanned += _client_UserUnbanned;
            _client.GuildScheduledEventCompleted += _client_GuildScheduledEventCompleted;
            _client.GuildScheduledEventStarted += _client_GuildScheduledEventStarted;
            _client.GuildScheduledEventUpdated += _client_GuildScheduledEventUpdated;
        }

        private Task _client_MessageUpdated(Cacheable<IMessage, ulong> arg1, SocketMessage arg2, ISocketMessageChannel arg3)
        {
            var _ = Task.Run(() => ProtectFromFloodAndBlacklisted(arg2));
            var __ = Task.Run(() => LogMessage(arg2));
            return Task.CompletedTask;
        }

        internal void UnlockServer(ulong id)
        {
            lockServer[id] = false;
        }

        internal void LockServer(ulong id)
        {
            lockServer[id] = true;
        }

        private Task _client_GuildMemberUpdated(Cacheable<SocketGuildUser, ulong> olduser, SocketGuildUser user)
        {
            var _ = Task.Run(async () =>
            {
                var member = await _db.members.FindOneAsync(a => a.MemberId == user.Id && a.GuildId == user.Guild.Id);

                if (member == null)
                {
                    member = Member.FromGuildUser(user);
                }

                member.RoleIds = user.Roles.Select(a => a.Id).ToArray();
                member.MemberNickname = user.Nickname;
                member.MemberUserName = user.Username;
                await _db.members.UpsertAsync(member, a => a.MemberId == user.Id && a.GuildId == user.Guild.Id);

                var config = await _db.ademirCfg.FindOneAsync(a => a.GuildId == user.Guild.Id);
                await CheckIfMinorsAndBanEm(config, member);
            });
            return Task.CompletedTask;
        }

        private Task _client_ReactionAdded(Cacheable<IUserMessage, ulong> arg1, Cacheable<IMessageChannel, ulong> arg2, SocketReaction arg3)
        {
            var _ = Task.Run(async () =>
            {
                var message = await _db.messagelog.FindOneAsync(a => a.MessageId == arg1.Id && a.ChannelId == arg2.Id);
                if (message != null)
                {
                    var reactionkey = arg3.Emote.ToString()!;
                    message.Reactions = message.Reactions ?? new Dictionary<string, int>();
                    if (message.Reactions.ContainsKey(reactionkey))
                        message.Reactions[reactionkey]++;
                    else
                        message.Reactions.Add(reactionkey, 1);
                    await _db.messagelog.UpsertAsync(message);
                }
            });
            return Task.CompletedTask;
        }

        private async Task _client_ShardReady(DiscordSocketClient arg)
        {
            foreach (var guild in _client.Guilds)
            {
                // await LoadMembersRoles(guild);
                await LoadGuildMessagesFromDb(guild);
            }

            var _ = Task.Run(async () =>
            {
                while (true)
                {
                    foreach (var guild in _client.Guilds)
                    {
                        // await SairDeServidoresNaoAutorizados(guild);
                        await ProcessMemberProgression(guild);
                        await TrancarThreadAntigasDoAdemir(guild);
                    }

                    await Task.Delay(TimeSpan.FromMinutes(20));
                }
            });

            var __ = Task.Run(async () =>
            {
                while (true)
                {
                    var sw = new Stopwatch();
                    var tasks = _client.Guilds.Select(guild => Task.Run(async () =>
                    {
                        await AtualizarListaGuildsPremium(guild);
                        await ProcessarXPDeAudio(guild);
                        await AnunciarEventosComecando(guild);
                        await BuscarPadroesBlacklistados(guild);
                        await BuscarCanaisComBypassDeFlood(guild);
                    })).ToArray();
                    Task.WaitAll(tasks);
                    await Task.Delay(TimeSpan.FromSeconds(120) - sw.Elapsed);
                }
            });
        }

        private async Task AtualizarListaGuildsPremium(SocketGuild g)
        {
            try
            {
                var guild = await this._db.ademirCfg.Find(a => a.GuildId == g.Id).FirstOrDefaultAsync();
                if (guild != null)
                {
                    g.SetPremium(guild.Premium);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro ao atualizar lista de Guild Premium.");
            }
        }

        private async Task LoadMembersRoles(SocketGuild guild)
        {
            try
            {
                var users = await guild.GetUsersAsync().FlattenAsync();
                foreach (var user in users)
                {
                    try
                    {
                        var member = await _db.members.Find(a => a.GuildId == guild.Id && a.MemberId == user.Id).FirstOrDefaultAsync();
                        if (member == null)
                        {
                            member = Member.FromGuildUser(user);
                        }
                        var membership = await _db.members.Find(a => a.GuildId == guild.Id && a.MemberId == user.Id && a.DateJoined != DateTime.MinValue)
                            .SortBy(a => a.DateJoined).FirstOrDefaultAsync();
                        member.DateLastJoined = user.JoinedAt.Value.UtcDateTime;
                        member.DateJoined = membership?.DateJoined ?? user.JoinedAt.Value.UtcDateTime;
                        member.RoleIds = user.RoleIds.ToArray();
                        await _db.members.UpsertAsync(member, a => a.GuildId == guild.Id && a.MemberId == user.Id);
                    }
                    catch (Exception exx)
                    {
                        _log.LogError(exx, "Erro ao atualizar usuario");
                    }
                }
                _log.LogInformation("Informações de join carregadas.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro ao buscar mensagens do banco");
            }
        }

        private async Task LoadGuildMessagesFromDb(SocketGuild guild)
        {
            try
            {
                if (!msgSinceAdemirCount.ContainsKey(guild.Id))
                    msgSinceAdemirCount.Add(guild.Id, 0);

                var msgs = await _db.messagelog.Find(a => a.GuildId == guild.Id && a.MessageDate >= DateTime.UtcNow.AddMinutes(-5)).ToListAsync();

                var ademirTalked50 = await _db.messagelog
                    .Find(a => a.GuildId == guild.Id && a.MessageDate >= DateTime.UtcNow.AddMinutes(-5))
                    .SortByDescending(a => a.MessageDate)
                    .Limit(50)
                    .ToListAsync();

                var ademirMsg = ademirTalked50.FirstOrDefault(a => a.UserId == _client.CurrentUser.Id);
                if (ademirMsg == null)
                    msgSinceAdemirCount[guild.Id] = 50;
                else
                    msgSinceAdemirCount[guild.Id] = ademirTalked50.IndexOf(ademirMsg);

                foreach (var msg in msgs)
                {
                    var autor = guild.GetUser(msg.UserId);
                    var channel = guild.GetTextChannel(msg.ChannelId);
                    if (autor != null && channel != null)
                        mensagensUltimos5Minutos.Add(new VirtualMessage
                        {
                            Channel = channel,
                            Author = autor,
                            Timestamp = new DateTimeOffset(msg.MessageDate),
                            Content = msg.Content
                        });
                }

                _log.LogInformation("Estado de velocidade de mensagens carregado.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro ao buscar mensagens do banco");
            }
        }

        private async Task SairDeServidoresNaoAutorizados(SocketGuild guild)
        {
            var config = await _db.ademirCfg.FindOneAsync(a => a.GuildId == guild.Id);
            if (config == null)
                await guild.LeaveAsync();
        }

        public async Task BuscarCanaisComBypassDeFlood(IGuild guild)
        {
            var ademirCfg = await _db.ademirCfg.Find(a => a.GuildId == guild.Id).FirstOrDefaultAsync();
            if (channelsBypassFlood.ContainsKey(guild.Id))
            {
                channelsBypassFlood[guild.Id] = ademirCfg.FloodProtectionByPassChannels;
            }
            else
            {
                channelsBypassFlood.Add(guild.Id, ademirCfg.FloodProtectionByPassChannels);
            }
        }
        public async Task BuscarPadroesBlacklistados(IGuild guild)
        {
            var blacklist = await _db.backlistPatterns.Find(a => a.GuildId == guild.Id).ToListAsync();
            if (backlistPatterns.ContainsKey(guild.Id))
            {
                backlistPatterns[guild.Id] = blacklist.Select(a => a.Pattern).ToList();
            }
            else
            {
                backlistPatterns.Add(guild.Id, blacklist.Select(a => a.Pattern).ToList());
            }
        }

        private Task _client_GuildScheduledEventStarted(SocketGuildEvent ev)
        {
            var _ = Task.Run(async () =>
            {
                var evento = await _db.events.Find(a => a.GuildId == ev.Guild.Id && a.EventId == ev.Id).FirstOrDefaultAsync();
                if (evento != null)
                {
                    evento.EndTime = DateTime.UtcNow;
                    await _db.events.UpsertAsync(evento, a => a.GuildId == ev.Guild.Id && a.EventId == ev.Id);
                }
            });
            return Task.CompletedTask;
        }

        private Task _client_GuildScheduledEventCompleted(SocketGuildEvent ev)
        {
            var _ = Task.Run(async () =>
            {
                var evento = await _db.events.Find(a => a.GuildId == ev.Guild.Id && a.EventId == ev.Id).FirstOrDefaultAsync();
                if (evento != null)
                {
                    evento.EndTime = DateTime.UtcNow;
                    await _db.events.UpsertAsync(evento, a => a.GuildId == ev.Guild.Id && a.EventId == ev.Id);
                }
            });
            return Task.CompletedTask;
        }

        private Task _client_GuildScheduledEventUpdated(Cacheable<SocketGuildEvent, ulong> old, SocketGuildEvent ev)
        {
            var _ = Task.Run(async () =>
            {
                var evento = await _db.events.Find(a => a.GuildId == ev.Guild.Id && old.Value.Id == ev.Id).FirstOrDefaultAsync();
                if (evento != null)
                {
                    evento.ChannelId = ev.Channel.Id;
                    evento.EventId = ev.Id;
                    evento.Cover = ev.GetCoverImageUrl();
                    evento.ScheduledTime = ev.StartTime.UtcDateTime;
                    evento.LastAnnounceTime = DateTime.UtcNow;
                    evento.Name = ev.Name;
                    evento.Description = ev.Description;
                    evento.Location = ev.Location;
                    evento.Type = ev.Type;
                    await _db.events.UpsertAsync(evento, a => a.GuildEventId == evento.GuildEventId);
                }

            });
            return Task.CompletedTask;
        }

        public async Task AnunciarEventosComecando(IGuild guild)
        {
            try
            {
                var events = await guild.GetEventsAsync();
                foreach (var ev in events)
                {
                    var evento = await _db.events.Find(a => a.GuildId == guild.Id && a.EventId == ev.Id).FirstOrDefaultAsync();
                    if (evento == null)
                    {
                        evento = new GuildEvent
                        {
                            GuildEventId = Guid.NewGuid(),
                            EventId = ev.Id,
                            ChannelId = ev.ChannelId ?? 0,
                            Cover = ev.GetCoverImageUrl(),
                            ScheduledTime = ev.StartTime.UtcDateTime,
                            GuildId = guild.Id,
                            LastAnnounceTime = DateTime.UtcNow,
                            Name = ev.Name,
                            Description = ev.Description,
                            Location = ev.Location,
                            Type = ev.Type
                        };
                        await _db.events.AddAsync(evento);
                    }

                    var tempoParaInicio = evento.ScheduledTime - DateTime.UtcNow;
                    var eventoHoje = evento.ScheduledTime.ToLocalTime().Date == DateTime.Today;
                    var eventoAmanha = evento.ScheduledTime.ToLocalTime().Date.AddDays(-1) == DateTime.Today;
                    bool podePostar = false;
                    string introducao = string.Empty;
                    string link = $"https://discord.com/events/{guild.Id}/{evento.EventId}";

                    var tempoDesdeUltimoAnuncio = DateTime.UtcNow - evento.LastAnnounceTime;
                    if (eventoHoje)
                    {
                        introducao = $"Atenção, <@&956383044770598942>!\nLogo mais, no canal <#{evento.ChannelId}>, teremos **{evento.Name}**. Se preparem.\n{link}";

                        if (ev.Status == GuildScheduledEventStatus.Scheduled)
                        {
                            if (tempoParaInicio.AroundMinutes(3) && tempoDesdeUltimoAnuncio > TimeSpan.FromMinutes(7))
                            {
                                introducao = $"Atenção, <@&956383044770598942>!\nTa na hora! **{evento.Name}** no <#{evento.ChannelId}>! Corre que ja vai começar!\n{link}";
                                podePostar = true;
                            }
                            else if (tempoParaInicio.AroundMinutes(10) && tempoDesdeUltimoAnuncio > TimeSpan.FromMinutes(30))
                            {
                                introducao = $"Atenção, <@&956383044770598942>!\nJá vai começar, **{evento.Name}** no <#{evento.ChannelId}>!\n{link}";
                                podePostar = true;
                            }
                            else if (tempoParaInicio.AroundMinutes(60) && tempoDesdeUltimoAnuncio > TimeSpan.FromMinutes(30))
                            {
                                introducao = $"Atenção, <@&956383044770598942>!\nEm menos de uma hora, começa **{evento.Name}** no <#{evento.ChannelId}>!\n{link}";
                                podePostar = true;
                            }
                            else if (tempoParaInicio > TimeSpan.FromMinutes(30) && msgSinceAdemirCount[guild.Id] > 50 && tempoDesdeUltimoAnuncio > TimeSpan.FromMinutes(60))
                            {
                                introducao = $"Atenção, <@&956383044770598942>!\nLogo mais, às {evento.ScheduledTime.ToLocalTime():HH'h'mm}, no **{guild.Name}**, começa **{evento.Name}** no <#{evento.ChannelId}>!\n{link}";
                                podePostar = ProcessWPM(guild.SystemChannelId ?? 0) > 25;
                            }
                            if (podePostar)
                            {
                                evento.LastAnnounceTime = DateTime.UtcNow;
                                await (await guild.GetSystemChannelAsync()).SendMessageAsync(introducao, allowedMentions: AllowedMentions.All);
                                await _db.events.UpsertAsync(evento, a => a.GuildId == guild.Id && a.EventId == ev.Id);
                            }
                        }
                    }
                    else if (eventoAmanha)
                    {
                        if (msgSinceAdemirCount[guild.Id] > 50 && tempoDesdeUltimoAnuncio > TimeSpan.FromMinutes(60))
                        {
                            introducao = $"É amanhã, pessoal <@&956383044770598942>!\nAmanhã, às {evento.ScheduledTime.ToLocalTime():HH'h'mm}, no **{guild.Name}**, começa **{evento.Name}** no <#{evento.ChannelId}>!\nEspero vocês lá!\n{link}";
                            podePostar = ProcessWPM(guild.SystemChannelId ?? 0) > 25;
                        }

                        if (podePostar)
                        {
                            evento.LastAnnounceTime = DateTime.UtcNow;
                            await (await guild.GetSystemChannelAsync()).SendMessageAsync(introducao, allowedMentions: AllowedMentions.All);
                            await _db.events.UpsertAsync(evento, a => a.GuildId == guild.Id && a.EventId == ev.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro ao processar eventos começando.");
            }
        }

        private async Task ProcessarXPDeAudio(SocketGuild guild)
        {
            try
            {
                var config = await _db.ademirCfg.FindOneAsync(a => a.GuildId == guild.Id);

                var events = await guild.GetEventsAsync();
                foreach (var voice in guild.VoiceChannels)
                {

                    var @event = events.FirstOrDefault(a => a.ChannelId == voice.Id && a.Status == GuildScheduledEventStatus.Active);
                    if (voice.Id == guild.AFKChannel?.Id)
                        continue;

                    if (voice.ConnectedUsers.Where(a => !a.IsBot).Count() < 2)
                        continue;

                    var connectedUserIds = voice.ConnectedUsers.Select(a => a.Id).ToArray();
                    var connectedMembers = await _db.members.Find(a => a.GuildId == guild.Id && connectedUserIds.Contains(a.MemberId)).ToListAsync();
                    foreach (var user in voice.ConnectedUsers)
                    {
                        if (user.IsMuted || user.IsDeafened)
                            continue;

                        var member = await _db.members.FindOneAsync(a => a.MemberId == user.Id && a.GuildId == guild.Id);
                        var initialLevel = member.Level;
                        int earnedXp = 0;
                        if (member == null)
                        {
                            member = Member.FromGuildUser(user);
                        }

                        if (user.IsSelfMuted || user.IsSelfDeafened)
                        {
                            Console.WriteLine($"+2xp de call: {member.MemberUserName}");
                            earnedXp += 2;
                            member.MutedTime += TimeSpan.FromMinutes(2);
                        }
                        else
                        {
                            Console.WriteLine($"+5xp de call: {member.MemberUserName}");
                            earnedXp += 5;
                            member.VoiceTime += TimeSpan.FromMinutes(2);
                        }

                        if (user.IsVideoing)
                        {
                            earnedXp += 7;
                            Console.WriteLine($"+7xp de camera: {member.MemberUserName}");
                            member.VideoTime += TimeSpan.FromMinutes(2);
                        }

                        if (user.IsStreaming)
                        {
                            earnedXp += 2;
                            Console.WriteLine($"+2xp de streaming: {member.MemberUserName}");
                            member.StreamingTime += TimeSpan.FromMinutes(2);
                        }

                        if (@event != null)
                        {
                            var presence = await _db.eventPresence.FindOneAsync(a => a.MemberId == user.Id && a.GuildId == guild.Id && a.EventId == @event.Id);

                            if (presence == null)
                            {
                                presence = new EventPresence
                                {
                                    EventPresenceId = Guid.NewGuid(),
                                    GuildId = guild.Id,
                                    MemberId = member.MemberId,
                                    EventId = @event.Id,
                                    ConnectedTime = TimeSpan.Zero
                                };
                                member.EventsPresent++;
                            }
                            presence.ConnectedTime += TimeSpan.FromMinutes(2);
                            await _db.eventPresence.UpsertAsync(presence, a => a.MemberId == user.Id && a.GuildId == guild.Id && a.EventId == @event.Id);

                            earnedXp *= 4;
                        }

                        if (!config.EnableAudioXP)
                            return;

                        var qtdPessoasEntraramNaMesmaEpoca = connectedMembers.Where(a => (a.DateJoined - member.DateJoined).Duration() <= TimeSpan.FromDays(21)).Count();
                        var outrasPessoas = voice.Users.Count - qtdPessoasEntraramNaMesmaEpoca;

                        if (qtdPessoasEntraramNaMesmaEpoca > outrasPessoas * 2)
                        {
                            earnedXp /= qtdPessoasEntraramNaMesmaEpoca;

                            if (earnedXp == 0)
                                earnedXp = 1;
                            Console.WriteLine($"dividido por {qtdPessoasEntraramNaMesmaEpoca}: {member.MemberUserName}");
                        }

                        member.XP += earnedXp;

                        Console.WriteLine($"{member.MemberUserName} +{earnedXp} member xp -> {member.XP}");
                        member.Level = LevelUtils.GetLevel(member.XP);
                        await _db.members.UpsertAsync(member, a => a.MemberId == user.Id && a.GuildId == guild.Id);

                        await ProcessRoleRewards(config, member);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro ao apurar XP de audio");
            }
        }

        private async Task TrancarThreadAntigasDoAdemir(SocketGuild guild)
        {
            try
            {
                var threads = await _db.threads.Find(t => t.LastMessageTime >= DateTime.UtcNow.AddHours(-72) && t.LastMessageTime <= DateTime.UtcNow.AddHours(-12)).ToListAsync();

                foreach (var thread in threads)
                {
                    var threadCh = guild.GetThreadChannel(thread.ThreadId);
                    if (threadCh != null)
                        await threadCh.ModifyAsync(a => a.Archived = true);
                }
            }
            catch
            {
                _log.LogError("Erro ao trancar threads do Ademir.");
            }
        }

        private async Task<string> ProcessWelcomeMsg(IGuildUser user, AdemirConfig cfg, bool rejoin)
        {
            int width = 1661;
            int height = 223;
            SKColor backgroundColor = SKColor.Parse("#313338");

            using (var surface = SKSurface.Create(new SKImageInfo(width, height)))
            {
                var canvas = surface.Canvas;
                canvas.Clear(backgroundColor);
                var typeface = SKTypeface.FromFile("./shared/fonts/gg sans Bold.ttf");
                var bold = SKTypeface.FromFile("./shared/fonts/gg sans Bold.ttf");
                var extrabold = SKTypeface.FromFile("./shared/fonts/gg sans Extrabold.ttf");

                var bg = SKBitmap.Decode(cfg.WelcomeBanner);
                canvas.DrawBitmap(bg, new SKPoint(0, 0));

                canvas.DrawText(user.DisplayName ?? user.Username, 294, 170, new SKFont(typeface, 80), new SKPaint
                {
                    IsAntialias = true,
                    Color = SKColor.Parse("#30D5C8")
                });

                var wcPaint = new SKPaint
                {
                    TextSize = 69,
                    Typeface = bold,
                    IsAntialias = true,
                    Color = SKColor.Parse("#FFFFFF")
                };

                SKRect textBounds = SKRect.Empty;
                var text = $"Bem-vindo(a) {(rejoin ? "de volta ao" : "ao")}";
                wcPaint.MeasureText(text, ref textBounds);
                canvas.DrawText(text, 294, 80, new SKFont(bold, 69), wcPaint);

                canvas.DrawText(user.Guild.Name, (308 + textBounds.Left + textBounds.Width), 82, new SKFont(extrabold, 69), new SKPaint
                {
                    FakeBoldText = true,
                    IsAntialias = true,
                    Color = SKColor.Parse("#9B59B6")
                });


                var avatarUrl = user.GetGuildAvatarUrl(size: 128, format: ImageFormat.Png) ?? user.GetDisplayAvatarUrl(size: 128, format: ImageFormat.Png);
                canvas.DrawCircle(new SKPoint(140, 110), 100, new SKPaint
                {
                    IsAntialias = true,
                    Color = SKColors.White,
                    StrokeWidth = 12f,
                    IsStroke = true,
                });


                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    using var client = new HttpClient();
                    var ms = new MemoryStream();
                    var info = await client.GetStreamAsync(avatarUrl);
                    info.CopyTo(ms);
                    ms.Position = 0;
                    using var avatar = SKBitmap.Decode(ms);
                    var avatarRect = new SKRect(40, 10, 240, 210);
                    var path = new SKPath();
                    path.AddCircle(140, 110, 100);
                    canvas.ClipPath(path, antialias: true);
                    canvas.DrawBitmap(avatar, avatarRect, new SKPaint
                    {
                        IsAntialias = true
                    });
                }

                var filename = Path.GetTempFileName();
                // Salvar a imagem em um arquivo
                using (var image = surface.Snapshot())
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var stream = File.OpenWrite(filename))
                {
                    data.SaveTo(stream);
                }

                return filename;
            }
        }
        private async Task ProcessMemberProgression(SocketGuild guild)
        {
            try
            {
                var progression = await _db.progression.Find(t => t.GuildId == guild.Id && t.Date == DateTime.Today).FirstOrDefaultAsync();
                var joinsToday = await _db.memberships.Find(t => t.GuildId == guild.Id && t.DateJoined >= DateTime.Today).CountDocumentsAsync();
                var leftToday = await _db.memberships.Find(t => t.GuildId == guild.Id && t.DateLeft >= DateTime.Today).CountDocumentsAsync();

                if (progression == null)
                {
                    progression = new ServerNumberProgression
                    {
                        ServerNumberProgressionId = Guid.NewGuid(),
                        GuildId = guild.Id,
                        Date = DateTime.Today
                    };
                }
                progression.GrowthToday = joinsToday - leftToday;
                progression.MemberCount = guild.MemberCount;

                await _db.progression.UpsertAsync(progression);
                _log.LogInformation($"Membros em {guild.Name}: {guild.MemberCount}. Owner: {guild.Owner.Username}.");
            }
            catch
            {
                _log.LogError($"Erro ao registrar quantidade de membros do server {guild.Name}.");
            }
        }

        private Task _client_MessageReceived(SocketMessage arg)
        {
            var _ = Task.Run(() => ProtectFromFloodAndBlacklisted(arg));
            var __ = Task.Run(() => ProcessBumpReward(arg));
            var ___ = Task.Run(() => LogMessage(arg));
            return Task.CompletedTask;
        }

        private async Task ProcessBumpReward(SocketMessage arg)
        {
            if (arg.Author != null)
            {
                var guild = _client.GetGuild(arg.GetGuildId());
                var user = guild.GetUser(arg.Author.Id);
                var member = await _db.members.FindOneAsync(a => a.MemberId == user.Id && a.GuildId == user.Guild.Id);
                var isNewbie = DateTime.UtcNow - member.DateJoined < TimeSpan.FromMinutes(60);
                var mentionIds = arg.MentionedUsers.Select(a => a.Id);
                var onGoingBump = await _db.bumps
                    .FindOneAsync(a => a.GuildId == user.Guild.Id && a.BumpDate >= DateTime.Now.AddMinutes(-60) && a.Rewarded == false);

                if (onGoingBump != null)
                {
                    if (isNewbie && mentionIds.Contains(arg.Author.Id))
                    {
                        _log.LogInformation($"{user.Username} ganhou xp de bump.");
                        onGoingBump.Rewarded = true;
                        onGoingBump.WelcomedByBumper = true;
                        await _db.bumps.UpsertAsync(onGoingBump, a => a.BumpId == onGoingBump.BumpId);
                    }
                }
            }
        }

        private async Task ProtectFromFloodAndBlacklisted(SocketMessage arg)
        {
            if (!arg.Author?.IsBot ?? false)
                mensagensUltimos5Minutos.Add(arg);

            if (arg.Author != null)
            {
                var guild = _client.GetGuild(arg.GetGuildId());
                var user = guild.GetUser(arg.Author.Id);
                if (user == null)
                    return;


                if (channelsBypassFlood.ContainsKey(guild.Id) && channelsBypassFlood[guild.Id].Contains(arg.Channel.Id))
                    return;

                var joinedJustNow = DateTime.UtcNow - user.JoinedAt.Value < TimeSpan.FromMinutes(60);

                var mensagensUltimos10Segundos = mensagensUltimos5Minutos.Where(a => a.Author.Id == arg.Author.Id && a.Timestamp.UtcDateTime >= DateTime.UtcNow.AddSeconds(-10));

                if ((arg.Content.Count(a => a == '\n') > 15 && mensagensUltimos10Segundos.Count() > 1) || mensagensUltimos10Segundos.SelectMany(a => (a.Content ?? "").Split("\n")).Count() > 30)
                {
                    var member = await _db.members.Find(a => a.GuildId == arg.GetGuildId() && a.MemberId == arg.Author.Id).FirstOrDefaultAsync();
                    if (member.Level >= 10 || member.MessageCount >= 40 || member.ProtectionWhiteListed)
                        return;

                    await user.SetTimeOutAsync(TimeSpan.FromDays(7));
                    await guild.SystemChannel.SendMessageAsync(" ", embed: new EmbedBuilder().WithDescription("Foi pego floodando. Mutado.").WithAuthor(arg.Author).Build());
                    var delecoes = mensagensUltimos5Minutos.Where(a => a.Author.Id == arg.Author.Id)
                       .Select(async (msg) => await arg.Channel.DeleteMessageAsync(msg.Id, new RequestOptions { AuditLogReason = "Flood" }))
                       .ToArray();

                    Task.WaitAll(delecoes);
                }

                if (joinedJustNow && (arg.Content.Count(a => a == '\n') > 4 || arg.Content.Length > 800))
                {
                    await user.SetTimeOutAsync(TimeSpan.FromDays(7));
                    await guild.SystemChannel.SendMessageAsync(" ", embed: new EmbedBuilder().WithDescription("Foi pego floodando. Mutado.").WithAuthor(arg.Author).Build());

                    var delecoes = mensagensUltimos10Segundos
                       .Select(async (msg) => await arg.Channel.DeleteMessageAsync(msg.Id, new RequestOptions { AuditLogReason = "Flood" }))
                       .ToArray();

                    Task.WaitAll(delecoes);
                }

                var mensagensUltimos5Segundos = mensagensUltimos5Minutos.Where(a => a.Author.Id == arg.Author.Id && a.Timestamp.UtcDateTime >= DateTime.UtcNow.AddSeconds(-3));
                if (mensagensUltimos5Segundos.Count() > 15)
                {
                    var member = await _db.members.Find(a => a.GuildId == arg.GetGuildId() && a.MemberId == arg.Author.Id).FirstOrDefaultAsync();

                    if (member.Level >= 10 || member.MessageCount >= 40 || member.ProtectionWhiteListed)
                        return;

                    var delecoes = mensagensUltimos10Segundos
                        .Select(async (msg) => await arg.Channel.DeleteMessageAsync(msg.Id, new RequestOptions { AuditLogReason = "Flood" }))
                        .ToArray();

                    Task.WaitAll(delecoes);
                }
                else if (arg.Content.Matches(@"\S{80}") && arg.Content.Distinct().Count() < 9)
                {
                    await (arg.Channel as ITextChannel)!.DeleteMessageAsync(arg, new RequestOptions { AuditLogReason = "Flood" });
                }
                else if (backlistPatterns[arg.GetGuildId()].Any(a => arg.Content.Matches(a)))
                {
                    await (arg.Channel as ITextChannel)!.DeleteMessageAsync(arg, new RequestOptions { AuditLogReason = "Mensagem em backlist" });
                }
            }
        }

        private Task _client_UserJoined(SocketGuildUser user)
        {
            var _ = Task.Run(async () =>
            {
                var guild = _client.GetGuild(user.Guild.Id);
                var rejoin = false;

                var config = await _db.ademirCfg.FindOneAsync(a => a.GuildId == guild.Id);
                if (await CheckIfUserNamePatternIsRaidBotAndBan(user, config))
                    return;

                var member = await _db.members.FindOneAsync(a => a.MemberId == user.Id && a.GuildId == user.Guild.Id);
                if (member == null)
                {
                    member = Member.FromGuildUser(user);
                    await _db.members.AddAsync(member);
                }
                else
                {
                    rejoin = true;
                }

                if (await CheckIfNewAccountAndKickEm(config, user))
                    return;

                await IncluirNovaChegada(user);

                if (lockServer.ContainsKey(guild.Id) && lockServer[guild.Id] == true)
                {
                    await user.KickAsync("O servidor está bloqueado contra raid.");
                    return;
                }

                await GiveAutoRole(config, user);
                await Task.Delay(3000);
                await ProcessRoleRewards(config, member);
                await CheckIfMinorsAndBanEm(config, member);


                await ProcessMemberProgression(guild);
                if (config.WelcomeBanner != null && config.WelcomeBanner.Length > 0)
                {
                    var __ = Task.Run(async () =>
                    {
                        while (user != null && (user.IsPending ?? false))
                        {
                            await Task.Delay(200);
                            user = guild.GetUser(user.Id);
                        }

                        if (user == null || user.IsBot)
                            return;

                        var img = await ProcessWelcomeMsg(user, config, rejoin);
                        var welcome = await guild.SystemChannel.SendFileAsync(new FileAttachment(img, "welcome.png"), $"Seja bem-vindo(a) {(rejoin ? "de volta " : "")}ao {guild.Name}, {user.Mention}!");
                        member.WelcomeMessageId = welcome.Id;
                        await _db.members.UpsertAsync(member, a => a.GuildId == member.GuildId && a.MemberId == member.MemberId);
                    });
                }
            });
            return Task.CompletedTask;
        }

        private async Task<bool> CheckIfUserNamePatternIsRaidBotAndBan(SocketGuildUser user, AdemirConfig config)
        {
            if (!config.EnableBotUserNameDetection)
                return false;

            var avatarUrl = _client.GetUser(user.Id).GetAvatarUrl();
            if (avatarUrl == null)
            {
                await user.BanAsync(reason: "Usuário não tem imagem de perfil.");
                return true;
            }

            var nomeInicial = DataSetUtils.FemaleNames.FirstOrDefault(a => user.Username.StartsWith(a.ToLower()))?.ToLower();
            if (nomeInicial != null)
            {
                if (user.Username.Matches($"^{nomeInicial}[a-z]{6}$"))
                {
                    await user.BanAsync(reason: "Padrão de Username de Bot");
                }
            }

            var isRaidBot = user.Username.Matches(@"^[a-z]+_[a-z]{7}[0-9]{4}");
            if (isRaidBot)
            {
                await user.BanAsync(reason: "Padrão de Username de Bot");
            }
            return isRaidBot;
        }

        private async Task<bool> CheckIfNewAccountAndKickEm(AdemirConfig config, SocketGuildUser user)
        {
            if (config.KickNewAccounts && DateTime.UtcNow - user.CreatedAt < TimeSpan.FromDays(10))
            {
                await user.KickAsync("Conta nova. Expulso.");
                return true;
            }
            return false;
        }

        private async Task GiveAutoRole(AdemirConfig config, SocketGuildUser user)
        {
            try
            {
                var role = user.Guild.GetRole(config.AutoRoleId);
                if (role != null)
                {
                    await user.AddRoleAsync(role, new RequestOptions { AuditLogReason = "Autorole" });
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Erro ao definir cargo automatico para {user.Username}.");
            }
        }

        private Task _client_UserBanned(SocketUser user, SocketGuild guild)
        {
            var _ = Task.Run(async () =>
            {
                var member = await _db.members.FindOneAsync(a => a.MemberId == user.Id && a.GuildId == guild.Id);
                var ban = await guild.GetBanAsync(user);
                member.DateBanned = DateTime.UtcNow;
                member.ReasonBanned = ban.Reason;
                await _db.members.UpsertAsync(member, a => a.MemberId == member.MemberId && a.GuildId == member.GuildId);
            });
            return Task.CompletedTask;
        }

        private Task _client_UserUnbanned(SocketUser user, SocketGuild guild)
        {
            var _ = Task.Run(async () =>
            {
                var member = await _db.members.FindOneAsync(a => a.MemberId == user.Id && a.GuildId == guild.Id);
                member.DateBanned = null;
                member.ReasonBanned = null;
                await _db.members.UpsertAsync(member, a => a.MemberId == member.MemberId && a.GuildId == member.GuildId);
            });
            return Task.CompletedTask;
        }

        private Task CheckIfMinorsAndBanEm(AdemirConfig config, Member member)
        {
            var _ = Task.Run(async () =>
            {
                try
                {
                    var guild = _client.GetGuild(config.GuildId);
                    var role = guild.GetRole(config.MinorRoleId);
                    var user = guild.GetUser(member.MemberId);
                    if (role != null)
                    {
                        if (user.Roles.Any(a => a.Id == role.Id))
                        {
                            await user.SendMessageAsync("Oi. Tudo bem? Infelizmente não podemos aceitar menores de idade no nosso grupo. Desculpe.");
                            await user.BanAsync(0, "Menor de Idade");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"Erro ao expulsar o menor de idade: {member.MemberUserName}.");
                }
            });
            return Task.CompletedTask;
        }

        private Task _client_UserLeft(SocketGuild guild, SocketUser user)
        {
            var _ = Task.Run(async () =>
            {
                var userId = user.Id;
                var guildId = guild.Id;
                var membership = (await _db.memberships.FindOneAsync(a => a.MemberId == userId && a.GuildId == guildId));


                var dateleft = DateTime.UtcNow;
                if (membership == null)
                {
                    await _db.memberships.AddAsync(new Membership
                    {
                        MembershipId = Guid.NewGuid(),
                        GuildId = guildId,
                        MemberId = userId,
                        MemberUserName = user.Username,
                        DateLeft = dateleft
                    });
                }
                else
                {
                    if (membership.DateJoined != null)
                    {
                        var tempoNoServidor = dateleft - membership.DateJoined.Value;
                        if (tempoNoServidor < TimeSpan.FromMinutes(30))
                        {
                            var member = (await _db.members.FindOneAsync(a => a.MemberId == userId && a.GuildId == guildId));
                            if (member != null)
                            {
                                if (member.WelcomeMessageId > 0)
                                    await guild.SystemChannel.DeleteMessageAsync(member.WelcomeMessageId, new RequestOptions { AuditLogReason = "O novato foi embora" });
                            }
                            await ProcurarEApagarMensagemDeBoasVindas(guild, membership, membership.DateJoined.Value);
                        }
                    }
                    membership.MemberUserName = user.Username;
                    membership.DateLeft = dateleft;
                    await _db.memberships.UpsertAsync(membership);
                }

                await ProcessMemberProgression(guild);
            });
            return Task.CompletedTask;
        }

        private async Task ProcurarEApagarMensagemDeBoasVindas(SocketGuild guild, Membership member, DateTime untilDate)
        {
            var buttonMessages = await guild.SystemChannel
                .GetMessagesAsync(500)
                .Where(a => a.Any(b => b.Type == MessageType.GuildMemberJoin && b.Author.Id == member.MemberId))
                .Select(a => a.Where(b => b.Type == MessageType.GuildMemberJoin))
                .FlattenAsync();

            foreach (var buttonMessage in buttonMessages)
            {
                try
                {
                    await guild.SystemChannel.DeleteMessageAsync(buttonMessage.Id, new RequestOptions { AuditLogReason = "O novato foi embora" });
                    Console.WriteLine($"Mensagem de boas vindas do usuario [{member.MemberUserName}] apagada.");
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"Erro ao apagar mensagem de boas vindas para: {member.MemberUserName}");
                }
            }
        }

        private async Task IncluirNovaChegada(SocketGuildUser arg)
        {
            var userId = arg.Id;

            var datejoined = arg.JoinedAt.HasValue ? arg.JoinedAt.Value.DateTime : default;

            await _db.memberships.AddAsync(new Membership
            {
                GuildId = arg.Guild.Id,
                MemberId = userId,
                MemberUserName = arg.Username,
                DateJoined = datejoined
            });
        }

        private async Task LogMessage(SocketMessage arg)
        {
            var channel = ((SocketTextChannel)arg.Channel);

            if (channel.Id == channel.Guild.SystemChannel.Id)
            {
                if (!msgSinceAdemirCount.ContainsKey(arg.GetGuildId()))
                    msgSinceAdemirCount.Add(arg.GetGuildId(), 0);

                msgSinceAdemirCount[arg.GetGuildId()]++;

                if (arg.Author?.Id == _client.CurrentUser.Id)
                    msgSinceAdemirCount[arg.GetGuildId()] = 0;
            }

            if (!arg.Author?.IsBot ?? false)
                await _db.messagelog.UpsertAsync(new Message
                {
                    MessageId = arg.Id,
                    ChannelId = channel.Id,
                    GuildId = channel.Guild.Id,
                    Content = arg.Content ?? "",
                    MessageDate = arg.Timestamp.UtcDateTime,
                    UserId = arg.Author?.Id ?? 0,
                    MessageLength = arg.Content?.Length ?? 0,
                    Reactions = arg.Reactions.ToDictionary(a => a.Key.ToString()!, b => b.Value.ReactionCount)
                });

            if (arg is IThreadChannel && ((IThreadChannel)arg).OwnerId == _client.CurrentUser.Id)
            {
                await _db.threads.UpsertAsync(new ThreadChannel
                {
                    ThreadId = channel.Id,
                    GuildId = channel.Guild.Id,
                    MemberId = arg.Author?.Id ?? 0,
                    LastMessageTime = arg.Timestamp.UtcDateTime,
                });
            }

            var ppm = ProcessWPM(channel.Id);
            Console.WriteLine($"PPM: {ppm}");
            await ProcessXPPerMessage(ppm, arg);
        }

        private async Task ProcessXPPerMessage(int ppm, SocketMessage arg)
        {
            if (arg.Channel is IThreadChannel)
                return;
            if (!(arg is SocketUserMessage userMessage) || userMessage.Author == null)
                return;

            if (arg.Author!.Id == 0)
                return;

            var member = await _db.members.FindOneAsync(a => a.MemberId == arg.Author!.Id && a.GuildId == arg.GetGuildId());
            if (member == null)
            {
                member = Member.FromGuildUser(arg.Author as IGuildUser);
                member.MessageCount = 0;
            }
            var lastTime = member?.LastMessageTime ?? DateTime.MinValue;
            var lastActivityMentionTime = member?.LastActivityMentionTime ?? DateTime.MinValue;
            var initialLevel = member.Level;

            var guild = _client.GetGuild(arg.GetGuildId());
            var config = await _db.ademirCfg.FindOneAsync(a => a.GuildId == member.GuildId);
            var activeTakkerRole = guild.GetRole(config.ActiveTalkerRole);

            var isMentionCoolledDown = false;
            var activeTalkerMentions = new SocketUser[0];
            var mentionRewardMultiplier = 1M;

            if (activeTakkerRole != null)
            {
                isMentionCoolledDown = lastActivityMentionTime.AddMinutes(30) >= arg.Timestamp.UtcDateTime;
                activeTalkerMentions = arg.MentionedUsers.Where(a => a.Id != arg.Author.Id && guild.GetUser(a.Id).Roles.Contains(activeTakkerRole)).ToArray();
            }

            var isCoolledDown = lastTime.AddSeconds(60) >= arg.Timestamp.UtcDateTime;

            if (config.EnableMentionXP && activeTalkerMentions.Length > 0)
            {
                if (isMentionCoolledDown)
                {
                    Console.WriteLine($"{arg.Author?.Username} mention cooldown...");
                }
                var mentionedUsers = arg.MentionedUsers.Any(a => guild.GetUser(a.Id).Roles.Contains(activeTakkerRole));
                var mostQuietUser = await _db.members
                    .Find(a => activeTalkerMentions.Any(b => b.Id == a.MemberId) && a.GuildId == arg.GetGuildId())
                    .SortBy(a => a.LastMessageTime)
                    .FirstOrDefaultAsync();

                var timeSinceLastMessage = DateTime.UtcNow - mostQuietUser.LastMessageTime;

                var lastMentionOfThisUserByAuthor = await _db.userMentions
                    .Find(a => a.MentionId == mostQuietUser.MemberId && a.AuthorId == arg.Author.Id)
                    .SortByDescending(a => a.DateMentioned)
                    .FirstOrDefaultAsync();

                if (DateTime.Now - lastActivityMentionTime < TimeSpan.FromDays(1))
                {
                    Console.WriteLine($"Mention {arg.Author?.Username} > {mostQuietUser.MemberUserName} cooldown...");
                    return;
                }

                foreach (var mentioned in arg.MentionedUsers)
                {
                    await _db.userMentions.AddAsync(new UserMention
                    {
                        UserMentionId = Guid.NewGuid(),
                        AuthorId = arg.Author.Id,
                        DateMentioned = DateTime.UtcNow,
                        GuildId = guild.Id,
                        MentionId = mentioned.Id
                    });
                }

                mentionRewardMultiplier = GetRewardMultiplierByInactivity(timeSinceLastMessage);
            }
            else if (isCoolledDown)
            {
                Console.WriteLine($"{arg.Author?.Username} cooldown...");
                return;
            }

            if (mentionRewardMultiplier > 1)
            {
                Console.WriteLine($"{arg.Author?.Username}: multiplicador de menção @{activeTakkerRole?.Name}: {mentionRewardMultiplier}x");
            }

            member.MessageCount++;
            member.LastMessageTime = arg.Timestamp.UtcDateTime;

            var timeSinceCoolDown = arg.Timestamp.UtcDateTime - lastTime;
            var raidPpm = 120M;
            var ppmMax = ppm > raidPpm ? raidPpm : ppm;
            var gainReward = ((raidPpm - ppmMax) / raidPpm) * 25M;
            var earnedXp = (int)gainReward + 15;
            earnedXp = (int)(earnedXp * mentionRewardMultiplier);
            config.ChannelXpMultipliers = config.ChannelXpMultipliers ?? new Dictionary<ulong, double>();

            if (config.ChannelXpMultipliers.ContainsKey(arg.Channel.Id))
            {
                earnedXp *= (int)config.ChannelXpMultipliers[arg.Channel.Id];
            }

            member.XP += earnedXp;
            member.IsBot = arg.Author!.IsBot;
            member.Level = LevelUtils.GetLevel(member.XP);

            if (initialLevel < member.Level)
            {
                if (config.MinRecommendationLevel != 0 && member.Level == config.MinRecommendationLevel)
                {
                    var txtRecomm = $"Oi, {arg.Author.Mention}! Tudo bem? Estamos muito felizes que causamos uma boa impressão e você decidiu ficar no server.\nVocê tem direito a recomendar 2 membros do servidor que te incentivaram a ficar.\n\nUtilize o comando /recomendar no servidor.";
                    try
                    {
                        await arg.Author.SendMessageAsync(txtRecomm);
                    }
                    catch (Exception ex)
                    {
                        await guild.SystemChannel.SendMessageAsync($"{txtRecomm}\n\nPS.: Só mandei nesse canal porque você não aceita mensagens de membros do server.");
                    }
                }
            }

            try
            {
                await ProcessRoleRewards(config, member);
            }
            catch (Exception ex)
            {
                this._log.LogError(ex, "Erro ao atribuir cargos de nivel.");
            }
            await _db.members.UpsertAsync(member, a => a.MemberId == member.MemberId && a.GuildId == member.GuildId);

            if (earnedXp > 0)
                Console.WriteLine($"{arg.Author?.Username} +{earnedXp} member xp -> {member.XP}");
        }

        public decimal GetRewardMultiplierByInactivity(TimeSpan? timeSinceLastMessage)
        {
            decimal mentionRewardMultiplier;
            switch (timeSinceLastMessage)
            {
                case TimeSpan t when t <= TimeSpan.FromHours(2):
                    mentionRewardMultiplier = 1.12M;
                    break;

                case TimeSpan t when t <= TimeSpan.FromHours(8):
                    mentionRewardMultiplier = 1.25M;
                    break;

                case TimeSpan t when t <= TimeSpan.FromHours(12):
                    mentionRewardMultiplier = 1.50M;
                    break;

                case TimeSpan t when t <= TimeSpan.FromHours(24):
                    mentionRewardMultiplier = 1.75M;
                    break;

                case TimeSpan t when t <= TimeSpan.FromDays(2):
                    mentionRewardMultiplier = 2M;
                    break;

                case TimeSpan t when t <= TimeSpan.FromDays(30):
                    mentionRewardMultiplier = 3M;
                    break;

                default:
                    mentionRewardMultiplier = 3M;
                    break;
            }
            return mentionRewardMultiplier;
        }

        public async Task ProcessRoleRewards(AdemirConfig config, Member member)
        {
            var guild = _client.GetGuild(member.GuildId);
            var user = guild.GetUser(member.MemberId);

            if (config == null || user == null)
            {
                _log.LogError("Impossível processar recompensas de nivel. Configuração de level nao executada");
                return;
            }

            if (!config.EnableRoleRewards)
                return;

            if (user.IsBot && user.Id != _client.CurrentUser.Id)
            {
                _log.LogError("Dos bots, só o Ademir pode ganhar cargo de XP.");
                return;
            }

            var levelRolesToAdd = config.RoleRewards
                .Where(a => a.Level <= member.Level)
                .OrderByDescending(a => a.Level)
                .FirstOrDefault()?.Roles.Select(a => ulong.Parse(a.Id)) ?? new ulong[] { };

            var levelRolesToRemove = config.RoleRewards.SelectMany(a => a.Roles)
                .Where(a => user.Roles.Any(b => b.Id == ulong.Parse(a.Id))
                        && !levelRolesToAdd.Any(b => b == ulong.Parse(a.Id)))
                .Select(a => ulong.Parse(a.Id));

            if (levelRolesToAdd.Count() == 0)
                return;
            if (levelRolesToAdd.Count() > 0)
                await user.AddRolesAsync(levelRolesToAdd, new RequestOptions { AuditLogReason = "Novo cargo de Level" });
            if (levelRolesToRemove.Count() > 0)
                await user.RemoveRolesAsync(levelRolesToRemove, new RequestOptions { AuditLogReason = "Antigo cargo de Level" });
        }

        private int ProcessWPM(ulong channelId = 0)
        {
            mensagensUltimos5Minutos = mensagensUltimos5Minutos.Where(a => a.Timestamp.UtcDateTime >= DateTime.UtcNow.AddMinutes(-5)).ToList();
            if (channelId == 0)
                return mensagensUltimos5Minutos.Sum(a => a.Content.Split(new char[] { ' ', ',', ';', '.', '-', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length) / 5;
            else
                return mensagensUltimos5Minutos.Where(a => a.Channel.Id == channelId)
                    .Sum(a => a.Content.Split(new char[] { ' ', ',', ';', '.', '-', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length) / 5;
        }
    }
}
