using Discord;
using Discord.Interactions;
using DiscordBot.Domain.Entities;
using DiscordBot.Services;
using DiscordBot.Utils;
using MongoDB.Driver;
using System;

namespace DiscordBot.Modules
{
    public class AdemirConfigModule : InteractionModuleBase
    {
        private readonly Context db;
        private readonly GuildPolicyService policySvc;

        public AdemirConfigModule(Context context, GuildPolicyService policySvc)
        {
            db = context;
            this.policySvc = policySvc;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("config-cargo-ademir", "Configurar cargo que pode falar com o Ademir.", runMode: RunMode.Async)]
        public async Task ConfigCargoAdemir(
            [Summary(description: "Cargo permitido falar com o Ademir")] IRole cargo)
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    AdemirRoleId = cargo.Id
                });
            }
            else
            {
                config.AdemirRoleId = cargo.Id;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("Cargo permitido para o Ademir configurado.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("set-recommendation-level", "Define o level mínimo necessário para recomendar um membro.", runMode: RunMode.Async)]
        public async Task Colour([Summary(description: "Level mínimo")] long level)
        {
            await DeferAsync();
            var cfg = await db.ademirCfg.Find(a => a.GuildId == Context.Guild.Id).FirstOrDefaultAsync();
            if (cfg == null)
            {
                await ModifyOriginalResponseAsync(a => a.Content = "Configuração ausente.");
                return;
            }
            cfg.MinRecommendationLevel = level;
            await db.ademirCfg.UpsertAsync(cfg);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("set-activetalker-role", "Configurar cargo de participação ativa.", runMode: RunMode.Async)]
        public async Task SetActiveTalkerRole(
        [Summary(description: "Cargo de participação ativa")] IRole cargo)
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    ActiveTalkerRole = cargo.Id
                });
            }
            else
            {
                config.ActiveTalkerRole = cargo.Id;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("Cargo de participação ativa configurado.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("set-eventinvite-role", "Configurar cargo a convidar para os eventos.", runMode: RunMode.Async)]
        public async Task SetEventInviteRole(
            [Summary(description: "Cargo de convite para eventos ativa")] IRole cargo)
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    EventInviteRole = cargo.Id
                });
            }
            else
            {
                config.EventInviteRole = cargo.Id;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("Cargo de convite para eventos configurado.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("kick-new-accounts", "Bloquear a entrada de contas novas", runMode: RunMode.Async)]
        public async Task KickNewAccounts()
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    KickNewAccounts = true,
                });
            }
            else
            {
                config.KickNewAccounts = true;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("Entrada de contas novas bloqueada.", ephemeral: true);
        }


        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("disable-kick-new-accounts", "Desbloquear a entrada de contas novas", runMode: RunMode.Async)]
        public async Task DisableKickNewAccounts()
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    KickNewAccounts = false,
                });
            }
            else
            {
                config.KickNewAccounts = false;
                await db.ademirCfg.UpsertAsync(config);
            }

            await RespondAsync("Entrada de contas novas desbloqueada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("ban-suspect-users", "Bloquear a entrada de usuarios com username/avaters suspeitos", runMode: RunMode.Async)]
        public async Task EnablesuspectuserBan()
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    EnableBotUserNameDetection = true,
                });
            }
            else
            {
                config.EnableBotUserNameDetection = true;
                await db.ademirCfg.UpsertAsync(config, a => a.GuildId == Context.Guild.Id);
            }

            await RespondAsync("Entrada de contas suspeitas bloqueada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("suspend-channel-flood-protection", "suspend-channel-flood-protection", runMode: RunMode.Async)]
        public async Task SuspendChannelFloodProtection(ITextChannel channel)
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            var channels = new List<ulong>();
            if (config == null)
            {
                config = new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    FloodProtectionByPassChannels = new ulong[0],
                    EnableBotUserNameDetection = true,
                };
            }
            else
            {
                config.EnableBotUserNameDetection = true;
                if (config.FloodProtectionByPassChannels != null)
                    channels.AddRange(config.FloodProtectionByPassChannels);
            }
            channels.Add(channel.Id);
            config.FloodProtectionByPassChannels = channels.Distinct().ToArray();

            await db.ademirCfg.UpsertAsync(config, a => a.GuildId == Context.Guild.Id);
            await policySvc.BuscarCanaisComBypassDeFlood(Context.Guild);
            await RespondAsync("suspend-channel-flood-protection.", ephemeral: true);
        }


        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("disable-ban-suspect-users", "Desbloquear a entrada de usuarios com username/avaters suspeitos", runMode: RunMode.Async)]
        public async Task DisableBotUserNameDetection()
        {
            var config = (await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));
            if (config == null)
            {
                await db.ademirCfg.AddAsync(new AdemirConfig
                {
                    AdemirConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    EnableBotUserNameDetection = false,
                });
            }
            else
            {
                config.EnableBotUserNameDetection = false;
                await db.ademirCfg.UpsertAsync(config, a => a.GuildId == Context.Guild.Id);
            }

            await RespondAsync("Entrada de contas suspeitas desbloqueada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("lock-server", "Bloquear a entrada de novos membros", runMode: RunMode.Async)]
        public async Task Lock()
        {
            policySvc.LockServer(Context.Guild.Id);
            await RespondAsync("Entrada de novos membros bloqueada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("unlock-server", "Desbloquear a entrada de novos membros")]
        public async Task UnLock()
        {
            policySvc.UnlockServer(Context.Guild.Id);
            await RespondAsync("Entrada de novos membros desbloqueada.", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("welcomebanner", "Define a imagem do banner de boas vindas", runMode: RunMode.Async)]
        public async Task BackgroundSet([Summary(description: "Imagem (1600x400)")] IAttachment imagem = null)
        {
            await DeferAsync();
            var cfg = await db.ademirCfg.Find(a => a.GuildId == Context.Guild.Id).FirstOrDefaultAsync();

            if (cfg == null)
            {
                return;
            }

            if (imagem.ContentType.Matches("image/.*"))
            {
                using var client = new HttpClient();
                using var ms = new MemoryStream();
                var info = await client.GetStreamAsync(imagem.Url);
                info.CopyTo(ms);
                ms.Position = 0;
                cfg.WelcomeBanner = ms.ToArray();
            }
            else if (imagem == null)
            {
                cfg.WelcomeBanner = null;
            }
            else
            {
                await ModifyOriginalResponseAsync(a =>
                {
                    a.Content = $"A cor que você selecionou não é válida. Tente um numero hexadecimal, media ou cargo.";
                });
                return;
            }

            await db.ademirCfg.UpsertAsync(cfg, a => a.GuildId == Context.Guild.Id);
            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = " ";
                a.Embed = new EmbedBuilder()
                    .WithColor(Color.Default)
                    .WithCurrentTimestamp()
                    .WithDescription($"Imagem de boas vindas {(imagem == null ? "removida" : "definida")} com sucesso.")
                    .Build();
            });
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("config-rewards", "Configurar recompensas de bump.", runMode: RunMode.Async)]
        public async Task ConfigRewards(
            [Summary(description: "Canal do Bump")] IChannel canal,
            [Summary(description: "Cargo de Bumper")] IRole cargo,
            [Summary(description: "XP por bump")] long xp)
        {
            var config = (await db.bumpCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id));

            if (config == null)
            {
                config = new BumpConfig
                {
                    BumpConfigId = Guid.NewGuid(),
                    GuildId = Context.Guild.Id,
                    BumpChannelId = canal.Id,
                    BumpRoleId = cargo.Id,
                    XPPerBump = (int)xp
                };
                await db.bumpCfg.AddAsync(config);
            }
            else
            {
                config.BumpChannelId = canal.Id;
                config.BumpRoleId = cargo.Id;
                config.XPPerBump = (int)xp;
                await db.bumpCfg.UpsertAsync(config);
            }
            await RespondAsync("Recompensas por bump configuradas.", ephemeral: true);
        }
    }
}
