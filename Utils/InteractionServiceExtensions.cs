using Discord.Interactions;
using DiscordBot.Modules;
using Discord.WebSocket;
using Discord.Net;
using Newtonsoft.Json;
using Discord;
using System.Reflection;
using DiscordBot.Services;

namespace DiscordBot.Utils
{
    public static class InteractionServiceExtensions
    {
        static bool initialized = false;
        public static async Task InitializeInteractionModulesAsync(this IServiceProvider provider)
        {
            try
            {
                var shard = provider.GetRequiredService<DiscordShardedClient>();
                var _interactionService = provider.GetRequiredService<InteractionService>();

                var _log = provider.GetRequiredService<ILogger<Program>>();

                provider.ActivateAllDiscordServices();

                shard.ShardReady += async (client) =>
                {
                    if (initialized)
                    {
                        return;
                    }
                    initialized = true;

                    var paginationService = provider.GetRequiredService<PaginationService>();
                    //await client.BulkOverwriteGlobalApplicationCommandsAsync(new ApplicationCommandProperties[] { });
                    var _interactionService = new InteractionService(client.Rest);


                    await shard.SetGameAsync($"tudo e todos [{client.ShardId}]", type: ActivityType.Listening);
                    _log.LogInformation($"Shard Number {client.ShardId} is connected and ready!");

                    try
                    {
                        await _interactionService.AddModulesAsync(Assembly.GetAssembly(typeof(ChatGPTModule)), provider);

                        await _interactionService.RegisterCommandsGloballyAsync(true);
                        paginationService.InitInteractionServicePagination(_interactionService);
                        _interactionService.SlashCommandExecuted += SlashCommandExecuted;
                        shard.InteractionCreated += async (x) =>
                        {
                            var ctx = new ShardedInteractionContext(shard, x);                            
                            var _ = await Task.Run(async () => await _interactionService.ExecuteCommandAsync(ctx, provider));
                        };

                        _log.LogInformation("Cliente conectado.");
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Erro ao carregar módulos.");
                    }
                };
            }
            catch (HttpException exception)
            {
                var json = JsonConvert.SerializeObject(exception.Errors, Formatting.Indented);
                Console.WriteLine(json);
            }
        }

        static async Task SlashCommandExecuted(SlashCommandInfo arg1, Discord.IInteractionContext arg2, Discord.Interactions.IResult arg3)
        {
            if (!arg3.IsSuccess)
            {
                switch (arg3.Error)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        await arg2.Interaction.RespondAsync($"Unmet Precondition: {arg3.ErrorReason}", ephemeral: true);
                        break;
                    case InteractionCommandError.UnknownCommand:
                        await arg2.Interaction.RespondAsync("Unknown command", ephemeral: true);
                        break;
                    case InteractionCommandError.BadArgs:
                        await arg2.Interaction.RespondAsync("Invalid number or arguments", ephemeral: true);
                        break;
                    case InteractionCommandError.Exception:
                        await arg2.Interaction.ModifyOriginalResponseAsync(a => a.Content = $"Command exception: {arg3}");
                        break;
                    case InteractionCommandError.Unsuccessful:
                        await arg2.Interaction.RespondAsync("Command could not be executed", ephemeral: true);
                        break;
                    default:
                        break;
                }
            }
        }

    }
}
