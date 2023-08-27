﻿using Discord;
using Discord.Interactions;
using DiscordBot.Domain.Entities;
using DiscordBot.Modules.Modals;
using DiscordBot.Services;
using DiscordBot.Utils;
using MongoDB.Driver;
using System.Text.RegularExpressions;

namespace DiscordBot.Modules
{
    public class MacroModule : InteractionModuleBase
    {
        private readonly Context db;
        private readonly PaginationService paginator;

        public MacroModule(Context context, PaginationService paginationService)
        {
            db = context;
            paginator = paginationService;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("macro", "Adiciona uma macro de prefixo '%'")]
        public async Task Macro()
            => await Context.Interaction.RespondWithModalAsync<MacroModal>("macro");

        [ModalInteraction("macro")]
        public async Task MacroResponse(MacroModal modal)
        {
            var macro = await db.macros.Count(a => a.GuildId == Context.Guild.Id && a.Nome == modal.Nome);

            if (macro > 0)
            {
                await RespondAsync($"Já existe uma macro com o nome {modal.Nome} no server.", ephemeral: true);
            }

            await db.macros.AddAsync(new Macro
            {
                MacroId = Guid.NewGuid(),
                GuildId = Context.Guild.Id,
                Nome = modal.Nome,
                Mensagem = modal.Mensagem
            });

            await RespondAsync($"Lembre-se que para acionar a macro você deve digitar %{modal.Nome}", ephemeral: true);
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("excluir-macro", "Excluir a macro especificada", runMode: RunMode.Async)]
        public async Task ExcluirMacro([Summary(description: "Nome da macro")] string nome)
        {
            await DeferAsync(ephemeral: true);
            var macro = await db.macros.DeleteAsync(a => a.GuildId == Context.Guild.Id && a.Nome == nome);

            if (macro == null)
                await ModifyOriginalResponseAsync(a => a.Content = "Essa macro não existe.");
            else
                await ModifyOriginalResponseAsync(a => a.Content = "Macro excluída.");
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("list-macros", "Listar as macros cadastradas", runMode: RunMode.Async)]
        public async Task ListMacros()
        {
            await DeferAsync();
            var pages = await db.macros.Find(a => a.GuildId == Context.Guild.Id).ToListAsync();
            var message = new PaginatedMessage(pages.Select(a => new Page
            {
                Fields = GetFields(a.Nome, a.Mensagem)
            }),
            "Macros", Color.Default, Context.User, new AppearanceOptions { });

            await paginator.SendPaginatedMessageAsync(Context.Channel, message);
            await DeleteOriginalResponseAsync();
        }

        private List<EmbedFieldBuilder> GetFields(string nome, string mensagem)
        {
            var embedFieldBuilders = new List<EmbedFieldBuilder>();
            embedFieldBuilders.Add(new EmbedFieldBuilder().WithIsInline(true).WithName($"Nome ({mensagem.Length} caracteres)").WithValue(nome));
            embedFieldBuilders.AddRange(mensagem.SplitInChunksOf(1024).Select(a => new EmbedFieldBuilder().WithName($"Mensagem").WithValue(a)));
            return embedFieldBuilders;
        }

        [RequireUserPermission(GuildPermission.Administrator)]
        [SlashCommand("editar-macro", "Editar a macro", runMode: RunMode.Async)]
        public async Task EditMacro([Summary(description: "Nome da macro")] string nome)
        {
            var macro = await db.macros.FindOneAsync(a => a.GuildId == Context.Guild.Id && a.Nome == nome);

            if (macro == null)
            {
                await RespondAsync($"Não existe uma macro com o nome {nome} no server.", ephemeral: true);
                return;
            }

            await Context.Interaction.RespondWithModalAsync($"edit-macro:{nome}", new EditMacroModal
            {
                Title = $"Editar macro: %{nome}",
                Mensagem = macro.Mensagem
            });
        }

        [ModalInteraction(@"edit-macro:*", TreatAsRegex = true)]
        public async Task MacroEditResponse(EditMacroModal modal)
        {
            string id = ((IModalInteraction)Context.Interaction).Data.CustomId;
            var nome = Regex.Match(id, @"edit-macro:(\w+)").Groups[1].Value;
            var macro = await db.macros.FindOneAsync(a => a.GuildId == Context.Guild.Id && a.Nome == nome);

            if (macro == null)
            {
                await RespondAsync($"Não existe uma macro com o nome {nome} no server.", ephemeral: true);
                return;
            }
            macro.Mensagem = modal.Mensagem;
            await db.macros.UpsertAsync(macro);

            await RespondAsync($"Lembre-se que para acionar a macro você deve digitar %{nome}", ephemeral: true);
        }
    }
}
