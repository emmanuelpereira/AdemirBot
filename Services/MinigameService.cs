using Amazon.Runtime.Internal.Endpoints.StandardLibrary;
using Discord;
using Discord.WebSocket;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.ValueObjects;
using DiscordBot.Utils;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using OpenAI.Builders;
using OpenAI.Managers;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels.ResponseModels.ModelResponseModels;
using OpenAI.ObjectModels.SharedModels;
using System.Text.RegularExpressions;

namespace DiscordBot.Services
{
    public class MinigameService : Service
    {
        private ILogger<MinigameService> _log;
        private Context _db;
        private DiscordShardedClient _client;
        private readonly OpenAIService openAI;

        public Dictionary<ulong, MinigameMatch> StartedMinigame { get; }
        public Dictionary<ulong, bool> MinigameTrigger { get; }
        public Dictionary<ulong, DateTime?> LastMessageKnown { get; }
        public Dictionary<ulong, int> MsgCounter { get; }

        public MinigameService(ILogger<MinigameService> log, Context context, DiscordShardedClient client, OpenAIService openAi)
        {
            _log = log;
            _db = context;
            _client = client;
            this.openAI = openAi;
            StartedMinigame = new Dictionary<ulong, MinigameMatch>();
            LastMessageKnown = new Dictionary<ulong, DateTime?>();
            MsgCounter = new Dictionary<ulong, int>();
        }

        public override void Activate()
        {
            var types = new [] { typeof(CharadeData) };
            var objectSerializer = new ObjectSerializer(type => ObjectSerializer.DefaultAllowedTypes(type) || types.Contains(type));
            BsonSerializer.RegisterSerializer(objectSerializer);
            BindEventListeners();
        }

        public void BindEventListeners()
        {
            _client.MessageReceived += _client_MessageReceived;
            _client.ShardReady += _client_ShardReady;
        }

        public void Trigger(IInteractionContext ctx)
        {
            if (!MinigameTrigger.ContainsKey(ctx.Guild.Id))
                MinigameTrigger[ctx.Guild.Id] = false;

            MinigameTrigger[ctx.Guild.Id] = true;
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

        public async Task GiveUp(SocketGuild guild)
        {
            if (!StartedMinigame.ContainsKey(guild.Id))
            {
                StartedMinigame[guild.Id] = null;
            }

            var startedGame = await _db.minigames.Find(a => a.GuildId == guild.Id && a.Finished == false)
                .SortByDescending(a => a.StartDate)
                .FirstOrDefaultAsync();

            StartedMinigame[guild.Id] = startedGame;

            if (StartedMinigame[guild.Id] != null && !StartedMinigame[guild.Id].Finished)
            {
                await guild.SystemChannel.SendMessageAsync(" ",
                    embed: new EmbedBuilder()
                    .WithAuthor("Parece que tá difícil..")
                    .WithDescription($"Tudo bem. A resposta da charada é: {StartedMinigame[guild.Id].Data.Aswer}")
                    .Build());

                startedGame.Finished = true;
                startedGame.Winner = _client.CurrentUser.Id;
                await _db.minigames.UpsertAsync(startedGame, a => a.GuildId == guild.Id && a.MinigameId == startedGame.MinigameId);
            }

            StartedMinigame[guild.Id] = null;
            await IniciarMinigame(guild);
        }

        private Task _client_MessageReceived(SocketMessage arg)
        {
            var _ = Task.Run(() => VerificarSeMinigame(arg));
            return Task.CompletedTask;
        }

        private async Task VerificarSeMinigame(SocketMessage arg)
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

            if (arg.Content == ">>minigame")
            {
                await IniciarMinigame(guild);
                return;
            }

            if (arg.Content == ">>giveup")
            {
                await GiveUp(guild);
                return;
            }

            if (!LastMessageKnown.ContainsKey(guild.Id))
            {
                LastMessageKnown[guild.Id] = null;
            }

            LastMessageKnown[guild.Id] = DateTime.UtcNow;

            MsgCounter[guild.Id]++;

            if (StartedMinigame[guild.Id] != null)
            {
                var minigame = StartedMinigame[guild.Id];
                if (minigame.Finished)
                {
                    StartedMinigame[guild.Id] = null;
                    return;
                }

                if (arg.Content.RemoverAcentos().Replace(" ", "").ToLower().Contains(minigame.Data.Aswer.RemoverAcentos().Replace(" ", "").ToLower()))
                {
                    minigame.Finished = true;
                    minigame.Winner = arg.Author.Id;
                    await _db.minigames.UpsertAsync(minigame, a => a.GuildId == guild.Id && a.MinigameId == minigame.MinigameId);

                    await guild.SystemChannel.SendMessageAsync(" ",
                        embed: new EmbedBuilder()
                        .WithColor(Color.Green)
                        .WithAuthor("Resposta certa!")
                        .WithDescription($"Isso aí. A resposta é {minigame.Data.Aswer}")
                        .Build(), messageReference: new MessageReference(arg.Id));
                    StartedMinigame[guild.Id] = null;
                }
            }
        }

        public async Task IniciarMinigame(SocketGuild guild)
        {
            try
            {
                if (!StartedMinigame.ContainsKey(guild.Id))
                {
                    StartedMinigame[guild.Id] = null;
                }

                var startedGame = await _db.minigames.Find(a => a.GuildId == guild.Id && a.Finished == false)
                    .SortByDescending(a => a.StartDate)
                    .FirstOrDefaultAsync();

                StartedMinigame[guild.Id] = startedGame;

                if (StartedMinigame[guild.Id] != null && !StartedMinigame[guild.Id].Finished)
                {
                    await guild.SystemChannel.SendMessageAsync(" ",
                        embed: new EmbedBuilder()
                        .WithAuthor($"Minigame atual [{startedGame.Science}]:")
                        .WithDescription(StartedMinigame[guild.Id].Data.Charade)
                        .Build());

                    return;
                }

                (string ciencia, string charada, string resposta) = await ObterCharada();
                var minigame = new MinigameMatch
                {
                    MinigameId = Guid.NewGuid(),
                    GuildId = guild.Id,
                    Science = ciencia,
                    Data = new CharadeData
                    {
                        Charade = charada,
                        Aswer = resposta
                    },
                    StartDate = DateTime.UtcNow,
                    MinigameType = Domain.Enum.MinigameType.Charada
                };
                
                await guild.SystemChannel.SendMessageAsync(" ", 
                    embed: new EmbedBuilder()
                    .WithAuthor($"Minigame [{ciencia}]:")
                    .WithDescription(charada)
                    .Build());

                await _db.minigames.AddAsync(minigame);

                StartedMinigame[guild.Id] = minigame;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Erro ao iniciar minigame.");
            }
        }

        private async Task<(string ciencia, string charada, string resposta)> ObterCharada()
        {
            while (true)
            {
                string[] ciencias = new string[]
                {
                    // Ciências Naturais
                    "Física", "Química", "Biologia", "Geologia", "Geografia", "História", "Astronomia",

                    // Ciências Sociais
                    "Psicologia", "Sociologia", "Antropologia", "Economia", "Política", 
                    "Animes", "Doramas", "K-POP", "J-POP", "Metal", "Rock n' Roll", "Hark Rock", "Rock Inglês", "Pop inglês",
                    "Cultura Internacional", "Fofocas de celebridades", "Cinema", "Cultura Popular da Internet",
                    "Novelas brasileiras", "Novelas mexicanas", "Programas de auditório", "Memes da internet", "HQs Marvel", "HQs DC",
                    "Atividades de Boteco", "Carteado", "Trends do Twitter, Instagram e TikTok", "Futebol brasileiro e europeu",
                    "Fórmula 1", "Filmes de terror", "Filmes e séries de serviço de streaming", "Culinária regional",
                    "Língua inglesa",

                    // Ciências Formais
                    "Matemática", "Lógica", "Estatística", "Conhecimentos Gerais",  "Ciência da Computação",

                    // Ciências Aplicadas
                    "Engenharia", "Medicina", "Arquitetura", "Genética", "Citologia",

                    // Ciências Humanas
                    "Filosofia", "História", "Literatura", "Arte", "Música", "Dança", "Anatomia", "Educação física", "Esportes",
                    "Jogos retrô", "Jogos contemporâneos", "Cultura pop", "Culinária"
                };

                var r = new Random().Next(0, ciencias.Length - 1);
                var ciencia = ciencias[r];
                var ralpha = (char)new Random().Next('A', 'Z');

                var prompt = $@"
Crie um jogo de adivinhação de uma palavra que comece com a letra {ralpha}, que seja de nível aleatório que varia de fácil a muito difícil em {ciencia} e que seja indiscutivelmente verdade em relação às dicas.
Sempre dê três dicas para o usuário conseguir acertar.";

                var fn1 = new FunctionDefinitionBuilder("criar_jogo", "Cria o jogo")
                   .AddParameter("pergunta", PropertyDefinition.DefineString("A pergunta. ex.: Família do reino animal"))
                   .AddParameter("dica_1", PropertyDefinition.DefineString("Dica 1. Ex.: frequentemente associada ao medo"))
                   .AddParameter("dica_2", PropertyDefinition.DefineNumber("Dica 2. Ex.: tem uma força enorme comparada ao seu tamanho"))
                   .AddParameter("dica_3", PropertyDefinition.DefineNumber("Dica 3. Ex.: tem uma fobia que virou nome de filme"))
                   .AddParameter("resposta", PropertyDefinition.DefineString("A resposta. Ex.: Aracnídeos"))
                .Validate()
                .Build();

                var fns = new List<ToolDefinition> { ToolDefinition.DefineFunction(fn1), ToolDefinition.DefineFunction(fn1) };
                var result = await openAI.ChatCompletion.CreateCompletion (new ChatCompletionCreateRequest
                {
                    Messages = new[] { new ChatMessage ("system", prompt)},
                    N = 1,
                    MaxTokens = 1000,
                    Model = "gpt-4o",
                    Temperature = 0.2f,
                    Tools = fns
                });

                if (result.Successful && result.Choices.Count > 0)
                {
                    var tools = result.Choices[0].Message.ToolCalls;
                    if (tools != null)
                    {
                        foreach (var toolCall in tools)
                        {
                            Console.WriteLine($"  {toolCall.Id}: {toolCall.FunctionCall}");

                            var fn = toolCall?.FunctionCall;
                            if (fn != null)
                            {
                                if (fn.Name == "criar_jogo")
                                {
                                    try
                                    {
                                        var argms = fn.ParseArguments();
                                        var pergunta = argms["pergunta"].ToString();
                                        var dica_1 = argms["dica_1"].ToString();
                                        var dica_2 = argms["dica_2"].ToString();
                                        var dica_3 = argms["dica_3"].ToString();
                                        var respostaa = argms["resposta"].ToString();
                                        Console.WriteLine($"{pergunta}\n\n{respostaa}");
                                        return (ciencia, $"{pergunta}\n\nDicas:\n\n - {dica_1}\n - {dica_2}\n - {dica_3}", respostaa);
                                    }
                                    catch {
                                        continue;
                                    }
                                }
                                
                            }
                        }
                    }
                }
                continue;
            }
        }

        private async Task<bool> VerificarInicioMinigame(SocketGuild guild)
        {
            if (!LastMessageKnown.ContainsKey(guild.Id))
            {
                LastMessageKnown[guild.Id] = null;
            }

            if(LastMessageKnown[guild.Id] == null)
                return false;

            if (!MsgCounter.ContainsKey(guild.Id))
                MsgCounter[guild.Id] = 0;

            return MsgCounter[guild.Id] > 10 && DateTime.UtcNow - LastMessageKnown[guild.Id] > TimeSpan.FromMinutes(10);
        }
    }
}
