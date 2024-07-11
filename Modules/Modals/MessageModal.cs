using Discord;
using Discord.Interactions;

namespace DiscordBot.Modules.Modals
{
    public class MessageModal : IModal
    {
        public string Title { get; set; } = "Editar Mensagem";

        [InputLabel("Mensagem")]
        [ModalTextInput("mensagem", TextInputStyle.Paragraph, placeholder: "Texto da mensagem.")]
        public string Mensagem { get; set; }
    }
}
