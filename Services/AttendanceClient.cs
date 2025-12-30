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

        public async Task<bool> MarkEntryAsync()
        {
            var url = _configuration["Attendance:EntryUrl"];
            if (string.IsNullOrEmpty(url)) return false;

            return await CallUrlAsync(url, "ENTRADA");
        }

        public async Task<bool> MarkExitAsync()
        {
            var url = _configuration["Attendance:ExitUrl"];
            if (string.IsNullOrEmpty(url)) return false;

            return await CallUrlAsync(url, "SALIDA");
        }

        private async Task<bool> CallUrlAsync(string url, string type)
        {
            try
            {
                _logger.LogInformation("Marcando {Type} en URL: {Url}", type, url);
                // Nota: Las URLs provistas son GET o POST? Generalmente clicks en navegador son GET.
                // Asumiremos GET. Si falla, el usuario puede confirmar.
                var response = await _httpClient.GetAsync(url);
                
                // Opcional: leer contenido para verificar éxito
                var content = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("{Type} marcada exitosamente. Respuesta: {Status}", type, response.StatusCode);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Error al marcar {Type}. Status: {Status}. Content: {Content}", type, response.StatusCode, content);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al marcar {Type}", type);
                return false;
            }
        }
    }
}
