using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DiscordAsistenciaBot.Services
{
    public interface IHolidayService
    {
        Task<bool> IsHolidayAsync(DateTime date);
    }

    public class HolidayService : IHolidayService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HolidayService> _logger;
        private readonly List<DateTime> _cachedHolidays = new();
        private DateTime _lastFetch = DateTime.MinValue;

        public HolidayService(IHttpClientFactory httpClientFactory, ILogger<HolidayService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<bool> IsHolidayAsync(DateTime date)
        {
            await EnsureHolidaysLoadedAsync(date.Year);
            return _cachedHolidays.Contains(date.Date);
        }

        private async Task EnsureHolidaysLoadedAsync(int year)
        {
            // Refrescar caché si es un año diferente o ha pasado mucho tiempo (ej. 1 día)
            if (_cachedHolidays.Any(d => d.Year == year) && (DateTime.Now - _lastFetch).TotalHours < 24)
            {
                return;
            }

            try
            {
                _logger.LogInformation("Consultando API de feriados para el año {Year}...", year);
                var client = _httpClientFactory.CreateClient("FeriadosAPI");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordAsistenciaBot/1.0"); // Usar ParseAdd es más seguro                

                var response = await client.GetStringAsync($"https://apis.digital.gob.cl/fl/feriados/{year}");
                
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    _cachedHolidays.RemoveAll(d => d.Year == year); // Limpiar previos de ese año
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("fecha", out var fechaProp) && DateTime.TryParse(fechaProp.GetString(), out var fecha))
                        {
                            _cachedHolidays.Add(fecha.Date);
                        }
                    }
                    _lastFetch = DateTime.Now;
                    _logger.LogInformation("Feriados cargados: {Count}", _cachedHolidays.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar API de feriados. Usando caché o asumiendo día hábil.");
                // En caso de fallo critico, evitar bloquear. Si la lista está vacía, asumirá FALSE (No feriado),
                // lo cual es preferible a no marcar asistencia.
            }
        }
    }
}
