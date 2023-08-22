﻿using Discord;
using Discord.WebSocket;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.ValueObjects;
using DiscordBot.Utils;
using MongoDB.Driver;
using OpenAI.Managers;
using OpenAI.ObjectModels.RequestModels;

namespace DiscordBot.Services
{
    public class MinigameService : Service
    {
        private ILogger<MinigameService> _log;
        private Context _db;
        private DiscordShardedClient _client;
        private readonly OpenAIService openAI;

        public Dictionary<ulong, MinigameMatch> StartedMinigame { get; }
        public Dictionary<ulong, int> MsgCounter { get; }

        public MinigameService(ILogger<MinigameService> log, Context context, DiscordShardedClient client, OpenAIService openAi)
        {
            _log = log;
            _db = context;
            _client = client;
            this.openAI = openAi;
            StartedMinigame = new Dictionary<ulong, MinigameMatch>();
            MsgCounter = new Dictionary<ulong, int>();
        }

        public override void Activate()
        {
            BindEventListeners();
        }

        public void BindEventListeners()
        {
            _client.MessageReceived += _client_MessageReceived;
            _client.ShardReady += _client_ShardReady;
        }

        private Task _client_ShardReady(DiscordSocketClient arg)
        {
            var _ = Task.Run(async () =>
            {
                while (true)
                {
                    foreach (var guild in _client.Guilds)
                    {
                        if (!StartedMinigame.ContainsKey(guild.Id))
                            StartedMinigame[guild.Id] = null;

                        var startedGame = await _db.minigames.Find(a => a.GuildId == guild.Id && a.Finished == false).FirstOrDefaultAsync();
                        StartedMinigame[guild.Id] = startedGame;

                        if (startedGame == null && await VerificarInicioMinigame(guild))
                        {
                            await IniciarMinigame(guild);
                        }
                    }

                    await Task.Delay(TimeSpan.FromMinutes(5));
                }
            });

            return Task.CompletedTask;
        }

        private async Task _client_MessageReceived(SocketMessage arg)
        {
            var guild = _client.GetGuild(arg.GetGuildId());

            if (guild == null || arg.Channel.Id != guild.SystemChannel.Id)
                return;

            if (arg.Author == null || string.IsNullOrEmpty(arg.Content))
                return;

            if (!MsgCounter.ContainsKey(guild.Id))
                MsgCounter[guild.Id] = 0;

            if (arg.Author.IsBot)
            {
                if (arg.Author.Id == _client.CurrentUser.Id)
                {
                    MsgCounter[guild.Id] = 0;
                }
                return;
            }
            MsgCounter[guild.Id]++;

            if (StartedMinigame[guild.Id] != null)
            {
                var minigame = StartedMinigame[guild.Id];
                if (minigame.Finished)
                    return;

                if (arg.Content.ToLower().Contains(minigame.CharadeData().Aswer.ToLower()))
                {
                    minigame.Finished = true;
                    minigame.Winner = arg.Author.Id;
                    await _db.minigames.UpsertAsync(minigame, a => a.GuildId == guild.Id && a.MinigameId == minigame.MinigameId);
                }
            }
        }

        private async Task IniciarMinigame(SocketGuild guild)
        {
            try
            {
                (string charada, string resposta) = await ObterCharada();
                var minigame = new MinigameMatch
                {
                    MinigameId = Guid.NewGuid(),
                    GuildId = guild.Id,
                    Data = new CharadeData
                    {
                        Charade = charada,
                        Aswer = resposta
                    },
                    MinigameType = Domain.Enum.MinigameType.Charada
                };
                await _db.minigames.AddAsync(minigame);


                if (!StartedMinigame.ContainsKey(guild.Id))
                    StartedMinigame[guild.Id] = null;

                StartedMinigame[guild.Id] = minigame;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro ao iniciar minigame.");
            }
        }

        private async Task<(string charada, string resposta)> ObterCharada()
        {
            while (true)
            {
                var result = await openAI.ChatCompletion.CreateCompletion(new OpenAI.ObjectModels.RequestModels.ChatCompletionCreateRequest
                {
                    Messages = new[]
                    {
                    new ChatMessage("system", @"
Crie um jogo de adivinhação de uma única palavra não-composta e aleatória, que não seja muito fácil e que seja comprovado como verdade, sempre dê três dicas e dê a resposta em seguida no formato:
Dicas: 
- {dica 1}
- {dica 2}
- {dica 3}

R: {resposta}")
                },
                    Model = "gpt-4",
                    MaxTokens = 1000,
                    Temperature = 0.5f
                });

                if (result.Successful && result.Choices.Count > 0 && result.Choices[0].Message.Content is string message && message.Matches(@"R: (\w+)"))
                {
                    var charada = message.Match(@"([\S\s]*)R: \w+").Groups[1].Value.Trim();
                    var resposta = message.Match(@"R: (\w+)$").Groups[1].Value;
                    Console.WriteLine($"{charada}\n\n{resposta}");
                    return (charada, resposta);
                }
            }
        }

        private async Task<bool> VerificarInicioMinigame(SocketGuild guild)
        {
            if (!MsgCounter.ContainsKey(guild.Id))
                MsgCounter[guild.Id] = 0;

            return MsgCounter[guild.Id] > 50;
        }
    }
}
