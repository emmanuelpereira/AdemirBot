﻿using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DiscordBot.Domain.Enum;
using DiscordBot.Domain.ValueObjects;
using DiscordBot.Services;
using DiscordBot.Utils;
using MongoDB.Driver;
using SkiaSharp;

namespace DiscordBot.Modules
{
    public class MemberModule : InteractionModuleBase
    {
        private readonly Context db;
        private readonly PaginationService paginator;
        private readonly GuildPolicyService guildPolicy;

        public MemberModule(Context context, GuildPolicyService guildPolicy, PaginationService paginationService)
        {
            db = context;
            this.guildPolicy = guildPolicy;
            paginator = paginationService;
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("membercount", "Informa a quantidade de membros do server.")]
        public async Task MemberCount()
        {
            await DeferAsync();
            var progression = await db.progression.Find(t => t.Date == DateTime.Today).FirstOrDefaultAsync();

            if (progression == null)
            {
                await ModifyOriginalResponseAsync(a =>
                {
                    a.Embed = new EmbedBuilder()
                    .WithCurrentTimestamp()
                    .WithColor(Color.Default)
                    .WithFields(new[] { new EmbedFieldBuilder().WithName("Membros").WithValue($"{((SocketGuild)Context.Guild).MemberCount}") })
                    .Build();
                });
                return;
            }

            await ModifyOriginalResponseAsync(a =>
            {
                a.Embed = new EmbedBuilder()
                .WithCurrentTimestamp()
                .WithColor(Color.Default)
                .WithFields(new[] {
                    new EmbedFieldBuilder().WithName("Membros").WithValue($"{((SocketGuild)Context.Guild).MemberCount}"),
                    new EmbedFieldBuilder().WithName("Hoje").WithValue($"{progression.GrowthToday}")
                })
                .Build();
            });
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("avatar", "Mostra o Avatar de um usuario")]
        public async Task Avatar([Summary(description: "Usuario")] IUser usuario = null)
        {
            await DeferAsync();
            var usuarioGuilda = await Context.Guild.GetUserAsync((usuario ?? Context.User).Id);
            var url = (await Context.Guild.GetUserAsync(usuarioGuilda.Id)).GetDisplayAvatarUrl(size: 1024);

            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = " ";
                a.Embed = new EmbedBuilder()
                    .WithAuthor(usuarioGuilda)
                    .WithColor(Color.Default)
                    .WithCurrentTimestamp()
                    .WithImageUrl(url)
                    .Build();
            });
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("colour", "Define a cor pricipal do card de evolução")]
        public async Task Colour([Summary(description: "Cor")] string cor)
        {
            await DeferAsync();
            var cfg = await db.ademirCfg.Find(a => a.GuildId == Context.Guild.Id).FirstOrDefaultAsync();
            var user = await Context.Guild.GetUserAsync(Context.User.Id);
            var member = await db.members.Find(a => a.GuildId == Context.Guild.Id && a.MemberId == Context.User.Id).FirstOrDefaultAsync();
            if (cfg == null)
            {
                return;
            }

            if (cor == "cargo")
            {
                cor = cfg.RoleRewards
                    .Where(a => a.Level <= member.Level)
                    .OrderByDescending(a => a.Level)
                    .FirstOrDefault()?.Roles.Select(a => a.Color).FirstOrDefault() ?? "Transparent";
            }
            else if (cor == "media")
            {
                var avgColor = await GetAverageColor(Context.User.GetAvatarUrl());
                cor = avgColor.ToString();
            }

            if (SKColor.TryParse(cor, out var color))
            {
                member.AccentColor = color.ToString();
            }
            else
            {
                await ModifyOriginalResponseAsync(a =>
                {
                    a.Content = $"A cor que você selecionou não é válida. Tente um numero hexadecimal, media ou cargo.";
                });
                return;
            }

            await db.members.UpsertAsync(member, a => a.GuildId == Context.Guild.Id && a.MemberId == Context.User.Id);
            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = " ";
                a.Embed = new EmbedBuilder()
                    .WithColor(Color.Default)
                    .WithCurrentTimestamp()
                    .WithDescription("Cor principal do card de evolução atualizada.")
                    .Build();
            });
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("background", "Define o background do card de evolução")]
        public async Task BackgroundSet([Summary(description: "Imagem (1600x400)")] IAttachment imagem)
        {
            await DeferAsync();
            var cfg = await db.ademirCfg.Find(a => a.GuildId == Context.Guild.Id).FirstOrDefaultAsync();
            var user = await Context.Guild.GetUserAsync(Context.User.Id);
            var member = await db.members.Find(a => a.GuildId == Context.Guild.Id && a.MemberId == Context.User.Id).FirstOrDefaultAsync();
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
                member.CardBackground = ms.ToArray();
            }
            else
            {
                await ModifyOriginalResponseAsync(a =>
                {
                    a.Content = $"A imagem que você selecionou não é válida.";
                });
                return;
            }

            await db.members.UpsertAsync(member, a => a.GuildId == Context.Guild.Id && a.MemberId == Context.User.Id);
            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = " ";
                a.Embed = new EmbedBuilder()
                    .WithColor(Color.Default)
                    .WithCurrentTimestamp()
                    .WithDescription("Imagem de fundo do card de evolução atualizada.")
                    .Build();
            });
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("predict", "Prever a data que o servidor terá a determinada quantidade de membros")]
        public async Task Banner([Summary(description: "Qtd. Membros")] int qtd)
        {
            await DeferAsync();

            var initdate = DateTime.UtcNow.AddDays(-90);
            var prog = await db.progression.Find(a => a.Date > initdate.AddDays(1) && a.Date < DateTime.Today && a.GuildId == Context.Guild.Id).SortBy(a => a.Date).ToListAsync();
            var members = prog.Last().MemberCount;
            var avg = Math.Round(prog.Average(a => a.GrowthToday));
            var x = (qtd - members) / avg;

            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = " ";
                a.Embed = new EmbedBuilder()
                    .WithColor(Color.Default)
                    .WithFields(new[]
                    {
                        new EmbedFieldBuilder
                        {
                            Name = $"Data prevista de {qtd} membros",
                            Value = $"{TimestampTag.FromDateTime(DateTime.Today.AddDays(x), TimestampTagStyles.ShortDate)}",
                            IsInline = true
                        },
                        new EmbedFieldBuilder
                        {
                            Name = $"Média de crescimento",
                            Value = $"{avg} membros por dia",
                            IsInline = true
                        },
                    })
                    .WithCurrentTimestamp()
                    .Build();
            });
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("banner", "Mostra o Banner de um usuario")]
        public async Task Banner([Summary(description: "Usuario")] IUser usuario = null)
        {
            await DeferAsync();
            var usuarioGuilda = await Context.Guild.GetUserAsync((usuario ?? Context.User).Id);
            var restUser = await ((DiscordSocketClient)Context.Client).Rest.GetUserAsync(usuarioGuilda.Id);
            var url = restUser.GetBannerUrl();
            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = " ";
                a.Embed = new EmbedBuilder()
                    .WithAuthor(usuarioGuilda)
                    .WithColor(Color.Default)
                    .WithCurrentTimestamp()
                    .WithImageUrl(url)
                    .Build();
            });
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("membergraph", "Mostra a evolução de membros")]
        public async Task Membergraph([Summary(description: "Dias")] int dias = 7)
        {
            await DeferAsync();

            var chartFileName = await ProcessMemberGraph(Context.Guild, dias);
            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = " ";
                a.Attachments = new[] { new FileAttachment(chartFileName, "graph.png") };
            });
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("rank", "Mostra o Card de Ranking no Server")]
        public async Task Rank([Summary(description: "Usuario")] IUser usuario = null)
        {
            await DeferAsync();
            var id = usuario?.Id ?? Context.User.Id;
            var cardfilename = await ProcessCard(await Context.Guild.GetUserAsync(id));
            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = " ";
                a.Attachments = new[] { new FileAttachment(cardfilename, "rank-card.png") };
            });
        }

        [SlashCommand("leaderboard", "Mostra o ranking atual dos membros")]
        public async Task Leaderboard()
        {
            await DeferAsync();
            var members = await db.members.Find(a => a.GuildId == Context.Guild.Id).SortByDescending(a => a.Level).ToListAsync();
            var member = members.FirstOrDefault(m => m.MemberId == Context.User.Id);

            var currentPage = 1;
            if (member != null)
            {
                currentPage = ((members.IndexOf(member) + 1) / 10) + 1;
            }
            var numPaginas = (int)Math.Ceiling(members.Count / 10M);
            var paginas = new List<Page>(Enumerable.Range(0, numPaginas).Select(a => new Page()).ToList());
            for (var i = 0; i < numPaginas; i++)
            {
                var page = paginas[i];
                var lines = members.Where(a => (int)Math.Ceiling((members.IndexOf(a) + 1) / 10M) - 1 == i).Select(a => $"**{members.IndexOf(a) + 1}.** <@{a.MemberId}>: **level {a.Level}** ({a.XP:n0}xp)");
                page.Description = string.Join("\n", lines);
                page.Fields = new EmbedFieldBuilder[0];
            }

            var message = new PaginatedMessage(paginas, $"Ranking {Context.Guild.Name}", Color.Default, Context.User, new AppearanceOptions { });
            message.CurrentPage = currentPage;
            await paginator.SendPaginatedMessageAsync(Context.Channel, message);
            await DeleteOriginalResponseAsync();
        }

        private async Task<SKColor> GetAverageColor(string avatarUrl)
        {
            using var client = new HttpClient();
            using var ms = new MemoryStream();
            var info = await client.GetStreamAsync(avatarUrl);
            info.CopyTo(ms);
            ms.Position = 0;
            using var bitmap = SKBitmap.Decode(ms);

            var colorCounts = new Dictionary<SKColor, int>();

            for (int x = 0; x < bitmap.Width; x++)
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    var c = bitmap.GetPixel(x, y);
                    var q = 32;
                    var pixelColor = new SKColor((byte)((c.Red / q) * q), (byte)((c.Green / q) * q), (byte)((c.Blue / q) * q));
                    if (colorCounts.ContainsKey(pixelColor))
                    {
                        colorCounts[pixelColor]++;
                    }
                    else
                    {
                        colorCounts.Add(pixelColor, 1);
                    }
                }
            }

            var dominantColor = colorCounts.OrderByDescending(x => x.Value).FirstOrDefault().Key;

            return dominantColor;
        }

        private async Task<string> ProcessMemberGraph(IGuild guild, int dias)
        {
            var progression = await db.progression.Find(a => a.GuildId == guild.Id && a.Date > DateTime.UtcNow.Date.AddDays(-dias)).SortBy(a => a.Date).ToListAsync();
            // Configurações do gráfico
            int width = 840;
            int height = 640;
            long[] data = progression.Select(a => a.MemberCount).ToArray(); // Dados do gráfico
            DateTime[] dates = progression.Select(a => a.Date).ToArray(); // Dados do gráfico
            var mudaOAno = dates.Select(a => a.Year).Distinct().Count() > 1;
            long min = (long)(Math.Floor(data.Min() / 20M)) * 20;
            long max = (long)(Math.Ceiling(data.Max() / 20M)) * 20;
            var offsetx = 100;
            var offsety = 20;
            var range = max - min;
            var zeroY = height - 120;
            var yratio = ((float)zeroY) / (float)range;
            float xratio = (width - offsetx) / data.Length;

            using (var bitmap = new SKBitmap(width, height))
            {
                using (var canvas = new SKCanvas(bitmap))
                {
                    canvas.Clear(SKColors.Transparent);
                    int yscale = 10;
                    using (var paintGrid = new SKPaint())
                    {
                        paintGrid.StrokeWidth = 2;
                        paintGrid.Color = new SKColor(128, 128, 128, 50);
                        paintGrid.IsAntialias = true;
                        var lastX = -50;

                        using var textDatePaint = new SKPaint();
                        textDatePaint.Color = SKColor.Parse("#c0c0c0");
                        textDatePaint.TextSize = 24;
                        textDatePaint.TextAlign = SKTextAlign.Right;
                        textDatePaint.IsAntialias = true;
                        var m = SKMatrix.CreateRotation(45);
                        for (int i = 0; i < data.Length; i++)
                        {
                            var x = offsetx + i * xratio;
                            if (x - lastX >= 40)
                            {
                                canvas.DrawLine(x, offsety, x, zeroY + offsety, paintGrid);
                                //canvas.DrawText($"{dates[i]:dd/MM/yyyy}", x, zeroY + offsety + 30, textDatePaint);
                                SKPath path = new SKPath();
                                path.MoveTo(x - 50, zeroY + offsety + 90);
                                path.LineTo(x + 10, zeroY + offsety + 15);

                                canvas.DrawTextOnPath(mudaOAno ? $"{dates[i]:dd/MM/yy}" : $"{dates[i]:dd/MM}", path, 0, 0, textDatePaint);

                                lastX = (int)x;
                            }
                        }

                        using var textPaint = new SKPaint();
                        textPaint.Color = SKColor.Parse("#c0c0c0");
                        textPaint.TextSize = 24;
                        textPaint.TextAlign = SKTextAlign.Right;
                        textPaint.IsAntialias = true;

                        for (int i = 0; i <= 10; i++)
                        {
                            var y = i * (range / 10) * yratio + offsety;
                            var ytext = min + range - (Math.Ceiling(range / 10M) * i);
                            canvas.DrawText($"{ytext}", 90, y + 8, textPaint);
                            canvas.DrawLine(offsetx, y, (data.Length - 1) * xratio + offsetx, y, paintGrid);
                        }
                        canvas.DrawLine(offsetx, zeroY + offsety, (data.Length - 1) * xratio + offsetx, zeroY + offsety, paintGrid);
                    }

                    using (var paint = new SKPaint())
                    {
                        paint.Color = SKColor.Parse("#1de9b6");
                        paint.StrokeWidth = 3;
                        paint.IsAntialias = true;

                        for (int i = 0; i < data.Length - 1; i++)
                        {
                            float x1 = i * xratio;
                            float y1 = ((max - data[i]) * (yratio));
                            float x2 = (i + 1) * xratio;
                            float y2 = ((max - data[i + 1]) * (yratio));

                            if (i == 0)
                                canvas.DrawCircle(new SKPoint(offsetx + x1, offsety + y1), 3, paint);

                            canvas.DrawCircle(new SKPoint(offsetx + x2, offsety + y2), 3, paint);
                            canvas.DrawLine(offsetx + x1, offsety + y1, offsetx + x2, offsety + y2, paint);
                        }
                    }

                    var filename = Path.GetTempFileName();

                    using (var image = SKImage.FromBitmap(bitmap))
                    using (var chart = image.Encode(SKEncodedImageFormat.Png, 100))
                    using (var stream = File.OpenWrite(filename))
                    {
                        chart.SaveTo(stream);
                        return filename;
                    }
                }
            }
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("togglerolerewards", "Ativar/Desativar o módulo de cargos por XP")]
        public async Task ToggleRoleRewards()
        {
            await DeferAsync();
            var config = await db.ademirCfg.FindOneAsync(a => a.GuildId == Context.Guild.Id);

            if (config == null)
            {
                config = new Domain.Entities.AdemirConfig
                {
                    GuildId = Context.Guild.Id,
                };
            }
            config.EnableRoleRewards = !config.EnableRoleRewards;

            await db.ademirCfg.UpsertAsync(config, a => a.GuildId == Context.Guild.Id);
            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = $"Cargos de recompensa por XP {(config.EnableRoleRewards ? "habilitados" : "desabilitados")}.";
            });
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("syncrolerewards", "Sincroniza os cargos do usuario pelo level")]
        public async Task SyncLevels([Summary(description: "Usuario")] IUser usuario = null)
        {
            var guild = Context.Guild;
            var admin = (await guild.GetUserAsync(Context.User.Id)).GuildPermissions.Administrator;

            var id = usuario?.Id ?? Context.User.Id;

            if (usuario?.Id != Context.User.Id && !admin)
            {
                await RespondAsync("Apenas administradores podem sincronizar outros usuários.", ephemeral: true);
                return;
            }

            await DeferAsync();

            var member = await db.members.FindOneAsync(a => a.MemberId == id && a.GuildId == Context.Guild.Id);

            if (member == null)
            {
                await ModifyOriginalResponseAsync(a =>
                {
                    a.Content = "Membro não tem informações no server.";
                });
            }
            else
            {
                var config = await db.ademirCfg.FindOneAsync(a => a.GuildId == member.GuildId);
                await guildPolicy.ProcessRoleRewards(config, member);
                await ModifyOriginalResponseAsync(a =>
                {
                    a.Content = $"Cargos sincronizados para o usuário {member.MemberUserName}.";
                });
            }
        }

        [RequireUserPermission(GuildPermission.UseApplicationCommands)]
        [SlashCommand("importlevelinfo", "Importar informações de level de outro bot")]
        public async Task ImportLevelInfo(ImportBot bot)
        {
            if (Context.User.Id != 596787570881462391 && Context.User.Id != 695465058913746964)
            {
                await RespondAsync("Você não pode usar esse comando. Fale com os embaixadores do projeto para solicitar liberação.");
                return;
            }

            await DeferAsync();

            switch (bot)
            {
                case ImportBot.Lurkr:
                    await Lurkr.ImportLevelInfo(Context.Client, Context.Guild, db);
                    break;
            }

            await ModifyOriginalResponseAsync(a =>
            {
                a.Content = $"Informações de level importadas do bot {bot}";
            });
        }

        private async Task<string> ProcessCard(IGuildUser user)
        {
            var members = await db.members.Find(a => a.GuildId == user.GuildId).SortByDescending(a => a.Level).ToListAsync();
            var member = members.First(a => a.MemberId == user.Id);
            var rankPosition = members.IndexOf(member) + 1;
            int width = 1600;
            int height = 400;
            SKColor backgroundColor = SKColor.Parse("#313338");

            using (var surface = SKSurface.Create(new SKImageInfo(width, height)))
            {
                var canvas = surface.Canvas;
                canvas.Clear(backgroundColor);

                if (member.CardBackground != null && member.CardBackground.Length > 0)
                {
                    using var bitmap = SKBitmap.Decode(member.CardBackground);
                    canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height));
                }

                // Adicionar retângulo de fundo
                SKColor backgroundRectColor = SKColor.Parse("#23272A");
                int backgroundRectSize = 300;
                int backgroundRectX = 50;
                int backgroundRectY = 50;
                int backgroundRectCornerRadius = 25;
                canvas.DrawRoundRect(new SKRect(backgroundRectX, backgroundRectY, backgroundRectX + backgroundRectSize, backgroundRectY + backgroundRectSize), backgroundRectCornerRadius, backgroundRectCornerRadius, new SKPaint { IsAntialias = true, Color = backgroundRectColor });

                // Adicionar fundo da barra de progresso
                SKColor additionalRectColor = SKColor.Parse("#23272A");
                int additionalRectWidth = 1200;
                int additionalRectHeight = 60;
                int additionalRectX = 375;
                int additionalRectY = 290;
                int additionalRectCornerRadius = 30;
                canvas.DrawRoundRect(new SKRect(additionalRectX, additionalRectY, additionalRectX + additionalRectWidth, additionalRectY + additionalRectHeight), additionalRectCornerRadius, additionalRectCornerRadius, new SKPaint { IsAntialias = true, Color = additionalRectColor });

                var typeface = SKTypeface.FromFile("./shared/fonts/gg sans SemiBold.ttf");
                var levelMinXp = 50 * Math.Pow(member.Level - 1, 2) + 100;
                var levelMaxXp = 50 * Math.Pow(member.Level - 0, 2) + 100;
                var levelXp = member.XP - levelMinXp;
                var totalLevelXp = levelMaxXp - levelMinXp;
                var remainigXp = levelMaxXp - member.XP;
                var levelProgress = levelXp / totalLevelXp;

                // Adicionar preenchimento da barra de progresso
                SKColor additionalRect2Color = SKColor.Parse(member.AccentColor ?? "#B0FFFFFF");
                int additionalRect2Width = Convert.ToInt32(1200 * levelProgress);
                int additionalRect2Height = 60;
                int additionalRect2X = 375;
                int additionalRect2Y = 290;
                int additionalRect2CornerRadius = 30;
                canvas.DrawRoundRect(new SKRect(additionalRect2X, additionalRect2Y, additionalRect2X + additionalRect2Width, additionalRect2Y + additionalRect2Height), additionalRect2CornerRadius, additionalRect2CornerRadius, new SKPaint { IsAntialias = true, Color = additionalRect2Color });

                (string text, string color, int size, int x, int y) userName = (user.Username, "#FFFFFF", 66, 380, 273);
                (string text, string color, int size, int x, int y) rank = ($"RANK#{rankPosition}", "#99AAB5", 55, 1570, 150);
                (string text, string color, int size, int x, int y) lvl = ($"LEVEL {member.Level}", "#FFFFFF", 96, 1570, 104);
                (string text, string color, int size, int x, int y) xp = ($"{member.XP:n0} XP", "#FFFFFF", 55, 1232, 273);
                (string text, string color, int size, int x, int y) remain = ($"({remainigXp} to Next Level)", "#99AAB5", 30, 1569, 273);
                (string text, string color, int size, int x, int y) voiceTime = ($"Audio: {member.VoiceTime.TotalHours:n0}h{member.VoiceTime.Minutes:00}", "#eeeeee", 28, 1570, 180);
                (string text, string color, int size, int x, int y) videoTime = ($"Video: {member.VideoTime.TotalHours:n0}h{member.VoiceTime.Minutes:00}", "#eeeeee", 28, 1570, 210);
                (string text, string color, int size, int x, int y) events = ($"Been to {member.EventsPresent:n0} events", "#eeeeee", 28, 1570, 240);

                canvas.DrawText(userName.text, userName.x, userName.y, new SKFont(typeface, userName.size), new SKPaint { IsAntialias = true, Color = SKColor.Parse(userName.color) });
                canvas.DrawText(rank.text, rank.x, rank.y, new SKFont(typeface, rank.size), new SKPaint { IsAntialias = true, Color = SKColor.Parse(rank.color), TextAlign = SKTextAlign.Right });
                canvas.DrawText(lvl.text, lvl.x, lvl.y, new SKFont(typeface, lvl.size), new SKPaint { IsAntialias = true, Color = SKColor.Parse(lvl.color), TextAlign = SKTextAlign.Right });
                canvas.DrawText(xp.text, xp.x, xp.y, new SKFont(typeface, xp.size), new SKPaint { IsAntialias = true, Color = SKColor.Parse(xp.color), TextAlign = SKTextAlign.Right });
                canvas.DrawText(remain.text, remain.x, remain.y, new SKFont(typeface, remain.size), new SKPaint { IsAntialias = true, Color = SKColor.Parse(remain.color), TextAlign = SKTextAlign.Right });
                canvas.DrawText(voiceTime.text, voiceTime.x, voiceTime.y, new SKFont(typeface, voiceTime.size), new SKPaint { IsAntialias = true, Color = SKColor.Parse(voiceTime.color), TextAlign = SKTextAlign.Right });
                canvas.DrawText(videoTime.text, videoTime.x, videoTime.y, new SKFont(typeface, videoTime.size), new SKPaint { IsAntialias = true, Color = SKColor.Parse(videoTime.color), TextAlign = SKTextAlign.Right });
                canvas.DrawText(events.text, events.x, events.y, new SKFont(typeface, events.size), new SKPaint { IsAntialias = true, Color = SKColor.Parse(events.color), TextAlign = SKTextAlign.Right });

                var avatarUrl = user.GetGuildAvatarUrl(size: 512) ?? user.GetDisplayAvatarUrl(size: 512);

                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    using var client = new HttpClient();
                    using (var ms = new MemoryStream())
                    {
                        var info = await client.GetStreamAsync(avatarUrl);
                        info.CopyTo(ms);
                        ms.Position = 0;
                        using var avatar = SKBitmap.Decode(ms);
                        var avatarRect = new SKRect(75, 75, 325, 325);
                        canvas.DrawBitmap(avatar, avatarRect);
                    }
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
    }
}
