using Discord;
using Discord.WebSocket;
using DiscordAsistenciaBot;
using DiscordAsistenciaBot.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configuración de Servicios
// Registrar IHttpClientFactory
builder.Services.AddHttpClient("FeriadosAPI");

// Registrar AttendanceClient como Typed Client (Transient automáticamente)
builder.Services.AddHttpClient<AttendanceClient>();

// Configurar DiscordSocketClient como Singleton para mantener conexión
builder.Services.AddSingleton<DiscordSocketClient>(sp => 
{
    var config = new DiscordSocketConfig
    {
        GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
    };
    return new DiscordSocketClient(config);
});

// Registrar nuestros servicios
builder.Services.AddSingleton<DiscordService>();
builder.Services.AddSingleton<IHolidayService, HolidayService>();
// Removemos la linea anterior de AttendanceClient que estaba manual, ya lo hicimos con AddHttpClient<AttendanceClient>

// El Worker principal
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();