using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiscordAsistenciaBot.Services
{
    public class AttendanceClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AttendanceClient> _logger;

        public AttendanceClient(HttpClient httpClient, IConfiguration configuration, ILogger<AttendanceClient> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; DiscordBot/1.0)");
        }

        public async Task<(bool Success, string Message)> MarkEntryAsync()
        {
            var url = _configuration["Attendance:EntryUrl"];
            if (string.IsNullOrEmpty(url)) return (false, "URL no configurada");

            return await CallUrlAsync(url, "ENTRADA");
        }

        public async Task<(bool Success, string Message)> MarkExitAsync()
        {
            var url = _configuration["Attendance:ExitUrl"];
            if (string.IsNullOrEmpty(url)) return (false, "URL no configurada");

            return await CallUrlAsync(url, "SALIDA");
        }

        private async Task<(bool Success, string Message)> CallUrlAsync(string url, string type)
        {
            try
            {
                _logger.LogInformation("Marcando {Type} en URL: {Url}", type, url);
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                
                // La validación original era IsSuccessStatusCode, pero el usuario menciona que la página retorna "exito" en el texto.
                // Mantendremos IsSuccessStatusCode como base, pero el contenido es lo importante ahora.
                
                 if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("{Type} respuesta: {Content}", type, content);
                    return (true, content);
                }
                else
                {
                    _logger.LogWarning("Error HTTP al marcar {Type}. Status: {Status}", type, response.StatusCode);
                    return (false, $"Error HTTP {response.StatusCode}: {content}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al marcar {Type}", type);
                return (false, $"Excepción: {ex.Message}");
            }
        }
    }
}
