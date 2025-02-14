using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MongoDB.Driver;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels;
using OpenAI.Managers;
using DiscordBot.Utils;
using AngleSharp.Browser;
using YoutubeExplode.Videos.ClosedCaptions;
using System.Text.Unicode;
using System.Text;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Modules
{
    public class ChatGPTModule : InteractionModuleBase
    {
        private readonly OpenAIService openAI;
        private readonly Context db;

        public ChatGPTModule(OpenAIService openAI, Context ctx)
        {
            this.openAI = openAI;
            this.db = ctx;
        }

        [SlashCommand("dall-e", "Pedir ao Dall-e uma imagem com a descrição.", runMode: RunMode.Async)]
        public async Task DallE([Summary(description: "Comando do DALL-E")] string comando)
        {
            var guild = ((SocketTextChannel)Context.Channel).Guild;
            var me = guild.Users.First(a => a.Id == Context.User.Id);
            if (guild.Id != 1055161583841595412)
            {
                await RespondAsync($"Funcionalidade premium. Booste o servidor {guild.Name} para usar.", ephemeral: true);
                return;
            }

            await DeferAsync();
            
            var imageResult = await openAI.Image.CreateImage(new ImageCreateRequest
            {
                Prompt = comando!,
                Model = Models.Dall_e_3,
                N = 1,                
                Size = StaticValues.ImageStatics.Size.Size1024,
                ResponseFormat = StaticValues.ImageStatics.ResponseFormat.Url,
                
            });

            var attachments = new List<FileAttachment>();
            if (imageResult.Successful)
            {
                foreach (var img in imageResult.Results)
                {
                    var stream = await new HttpClient().GetStreamAsync(img.Url);
                    attachments.Add(new FileAttachment(stream, $"{Context.Interaction.Id}.jpg"));
                }
                await ModifyOriginalResponseAsync(a =>
                {
                    a.Content = comando;
                    a.Attachments = attachments.ToArray();
                });
            }
            else
            {
                await ModifyOriginalResponseAsync(a => a.Content = $"Erro ao processar o comando \"{comando}\"");
            }
        }

        [SlashCommand("completar", "Pedir ao GPT uma completude de texto.", runMode: RunMode.Async)]
        public async Task GPTText([Summary(description: "Comando")] string comando)
        {
            var guild = ((SocketTextChannel)Context.Channel).Guild;
            var me = guild.Users.First(a => a.Id == Context.User.Id);
            if (guild.Id != 1055161583841595412 && !(Context.Guild.IsPremium() && (me.PremiumSince.HasValue || me.GuildPermissions.Administrator)))
            {
                await RespondAsync($"Funcionalidade premium. Não disponível para este servidor.", ephemeral: true);
                return;
            }

            await DeferAsync();

            var imageResult = await openAI.Completions.CreateCompletion(new CompletionCreateRequest
            {
                Prompt = comando!,
                N = 1,
                MaxTokens = 1000,
                Model = Models.TextDavinciV3,
            });

            var msg = await ((SocketSlashCommand)Context.Interaction).GetOriginalResponseAsync();

            if (imageResult.Successful)
            {
                foreach (var choice in imageResult.Choices)
                {
                    await ModifyOriginalResponseAsync(a => a.Content = $"Comando: \"{comando}\"");
                    await Context.Channel.Responder(choice.Text, new MessageReference(msg.Id));
                }
            }
            else
            {
                await ModifyOriginalResponseAsync(a => a.Content = $"Erro ao processar o comando \"{comando}\"");
            }
        }

        [SlashCommand("thread", "Criar uma tread privada com o Ademir.", runMode: RunMode.Async)]
        public async Task NewThread([Summary(description: "Nome da nova Thread")] string nome)
        {
            var guild = ((SocketTextChannel)Context.Channel).Guild;
            var me = guild.Users.First(a => a.Id == Context.User.Id);
            if (guild.Id != 1055161583841595412 && !(Context.Guild.IsPremium() && (me.PremiumSince.HasValue || me.GuildPermissions.Administrator)))
            {
                await RespondAsync($"Funcionalidade premium. Não disponível para este servidor.", ephemeral: true);
                return;
            }

            await DeferAsync();

           var channel = await ((ITextChannel)Context.Channel).CreateThreadAsync(nome, ThreadType.PublicThread);

            await db.threads.UpsertAsync(new ThreadChannel
            {
                ThreadId = channel.Id,

                GuildId = channel.Guild.Id,
                MemberId = Context.Client.CurrentUser.Id,
                LastMessageTime = channel.CreatedAt.UtcDateTime,
            });
            await ((ISlashCommandInteraction)Context.Interaction).DeleteOriginalResponseAsync();
        }
    }
}
