using DiscordAsistenciaBot.Services;

namespace DiscordAsistenciaBot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DiscordService _discordService;
        private readonly IHolidayService _holidayService;
        
        // Zona Horaria Chile
        private readonly TimeZoneInfo _chileTimeZone;

        // Evitar múltiples ejecuciones en el mismo minuto
        private DateTime _lastRunDate = DateTime.MinValue;

        public Worker(ILogger<Worker> logger, DiscordService discordService, IHolidayService holidayService)
        {
            _logger = logger;
            _discordService = discordService;
            _holidayService = holidayService;
            
            // Intentar obtener zona horaria. Windows: "Pacific SA Standard Time", Linux: "America/Santiago"
            try 
            {
                _chileTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific SA Standard Time");
            }
            catch 
            {
                try 
                {
                    _chileTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
                }
                catch
                {
                    logger.LogWarning("No se encontró zona horaria de Chile. Usando Local.");
                    _chileTimeZone = TimeZoneInfo.Local;
                }
            }
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            // Iniciar conexión a Discord al arrancar
            await _discordService.StartAsync();
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker iniciado a las: {Time}", DateTimeOffset.Now);

            while (!stoppingToken.IsCancellationRequested)
            {
                var nowChile = TimeZoneInfo.ConvertTime(DateTime.Now, _chileTimeZone);
                
                // Verificar horario solo si no hemos ejecutado ya hoy para ese bloque
                // Simplificación: Revisamos cada minuto.
                
                // Lógica de "Lunes a Viernes"
                if (nowChile.DayOfWeek != DayOfWeek.Saturday && nowChile.DayOfWeek != DayOfWeek.Sunday)
                {
                    // ENTRADA: 08:29
                    if (nowChile.Hour == 8 && nowChile.Minute == 29 && ShouldRun("ENTRADA", nowChile))
                    {
                        await CheckAndNotifyAsync(nowChile, "ENTRADA");
                    }
                    
                    // SALIDA: 18:00
                    if (nowChile.Hour == 18 && nowChile.Minute == 0 && ShouldRun("SALIDA", nowChile))
                    {
                        await CheckAndNotifyAsync(nowChile, "SALIDA");
                    }
                }

                // Esperar 20 segundos antes de volver a verificar para no saturar CPU, 
                // pero ser precisos con el minuto
                await Task.Delay(20000, stoppingToken);
            }
        }

        private DateTime? _lastEntryRun;
        private DateTime? _lastExitRun;

        private bool ShouldRun(string type, DateTime now)
        {
            // Retorna true si NO se ha ejecutado hoy
            if (type == "ENTRADA")
            {
                if (_lastEntryRun.HasValue && _lastEntryRun.Value.Date == now.Date) return false;
                return true;
            }
            if (type == "SALIDA")
            {
                if (_lastExitRun.HasValue && _lastExitRun.Value.Date == now.Date) return false;
                return true;
            }
            return false;
        }

        private async Task CheckAndNotifyAsync(DateTime now, string type)
        {
            // Validar Feriado
            if (await _holidayService.IsHolidayAsync(now))
            {
                _logger.LogInformation("Hoy es feriado ({Date}), saltando {Type}.", now.Date.ToShortDateString(), type);
                MarkRun(type, now);
                return;
            }

            _logger.LogInformation("Ejecutando notificación de {Type}...", type);
            
            if (type == "ENTRADA")
                await _discordService.SendEntryMessageAsync();
            else
                await _discordService.SendExitMessageAsync();

            MarkRun(type, now);
        }

        private void MarkRun(string type, DateTime now)
        {
            if (type == "ENTRADA") _lastEntryRun = now;
            else _lastExitRun = now;
        }
    }
}
