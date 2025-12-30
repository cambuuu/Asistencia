using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiscordAsistenciaBot.Services
{
    public class DiscordService
    {
        private readonly DiscordSocketClient _client;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiscordService> _logger;
        private readonly AttendanceClient _attendanceClient;

        // IDs de Botones
        private const string BTN_ENTRY = "btn_entry_mark";
        private const string BTN_EXIT = "btn_exit_mark";

        public DiscordService(DiscordSocketClient client, IConfiguration configuration, ILogger<DiscordService> logger, AttendanceClient attendanceClient)
        {
            _client = client;
            _configuration = configuration;
            _logger = logger;
            _attendanceClient = attendanceClient;

            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.ButtonExecuted += ButtonHandler;
        }

        public async Task StartAsync()
        {
            var token = _configuration["Discord:Token"];
            if (string.IsNullOrEmpty(token) || token == "PON_TU_TOKEN_AQUI")
            {
                _logger.LogWarning("El Token de Discord no está configurado correctamente en appsettings.json");
                return;
            }

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
        }

        private Task LogAsync(LogMessage log)
        {
            _logger.LogInformation(log.ToString());
            return Task.CompletedTask;
        }

        private Task ReadyAsync()
        {
            _logger.LogInformation("Bot conectado como {User}", _client.CurrentUser);
            return Task.CompletedTask;
        }

        public async Task SendEntryMessageAsync()
        {
            await SendAttendanceCardAsync("¡Buen día! ☀️", "Es hora de marcar ENTRADA.", BTN_ENTRY, ButtonStyle.Success);
        }

        public async Task SendExitMessageAsync()
        {
            await SendAttendanceCardAsync("¡Jornada terminada! 🌙", "Es hora de marcar SALIDA.", BTN_EXIT, ButtonStyle.Primary); // Azul para salida
        }

        private async Task SendAttendanceCardAsync(string title, string description, string buttonId, ButtonStyle style)
        {
            var channelIdStr = _configuration["Discord:TargetChannelId"];
            if (!ulong.TryParse(channelIdStr, out var channelId))
            {
                _logger.LogError("Channel ID inválido en configuración.");
                return;
            }

            if (_client.GetChannel(channelId) is not IMessageChannel channel)
            {
                // Intentar obtenerlo del API si no está en caché
                channel = await _client.GetChannelAsync(channelId) as IMessageChannel;
            }

            if (channel == null)
            {
                _logger.LogError("No se pudo encontrar el canal {Id}", channelId);
                return;
            }

            var builder = new ComponentBuilder()
                .WithButton("MARCAR AHORA", buttonId, style, new Emoji("🕒"));

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(style == ButtonStyle.Success ? Color.Green : Color.Blue)
                .WithCurrentTimestamp()
                .Build();

            await channel.SendMessageAsync(text: "@everyone", embed: embed, components: builder.Build());
            _logger.LogInformation("Mensaje de {Type} enviado al canal {Channel}", buttonId, channel.Name);
        }

        private async Task ButtonHandler(SocketMessageComponent component)
        {
            // Solo responder a nuestros botones
            if (component.Data.CustomId != BTN_ENTRY && component.Data.CustomId != BTN_EXIT)
                return;

            await component.DeferAsync(); // Indicar que estamos procesando

            bool success = false;
            string actionName = "";

            if (component.Data.CustomId == BTN_ENTRY)
            {
                actionName = "ENTRADA";
                success = await _attendanceClient.MarkEntryAsync();
            }
            else if (component.Data.CustomId == BTN_EXIT)
            {
                actionName = "SALIDA";
                success = await _attendanceClient.MarkExitAsync();
            }

            if (success)
            {
                await component.FollowupAsync($"✅ **{actionName}** marcada correctamente a las {DateTime.Now:HH:mm}.", ephemeral: false);
            }
            else
            {
                await component.FollowupAsync($"❌ Hubo un error al marcar {actionName}. Por favor revisa la URL manualmente.", ephemeral: true);
            }
        }
    }
}
