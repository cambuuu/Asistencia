using Discord;
using Discord.WebSocket;
using DiscordAsistenciaBot;
using DiscordAsistenciaBot.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configuración de Servicios
// Registrar IHttpClientFactory
builder.Services.AddHttpClient("FeriadosAPI");

// Cliente de Buk: Singleton para conservar las cookies de sesión entre marcajes.
// Maneja su propio HttpClient porque necesita un CookieContainer persistente.
builder.Services.AddSingleton<BukAttendanceClient>();

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

// El Worker principal
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Modo diagnóstico: `dotnet run -- --test-buk` valida credenciales y acceso al
// formulario de marcaje sin registrar entrada ni salida, y sin conectar a Discord.
if (args.Contains("--test-buk"))
{
    var buk = host.Services.GetRequiredService<BukAttendanceClient>();
    var (ok, message) = await buk.TestConnectionAsync();
    Console.WriteLine($"{(ok ? "OK" : "FALLO")}: {message}");
    return ok ? 0 : 1;
}

// `dotnet run -- --preview-marcaje ENTRADA` imprime el body que se enviaria,
// sin enviarlo, para contrastarlo con el request real del navegador.
if (args.Contains("--preview-marcaje"))
{
    var buk = host.Services.GetRequiredService<BukAttendanceClient>();
    var idx = Array.IndexOf(args, "--preview-marcaje");
    var sentido = idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : "ENTRADA";

    var (ok, message) = await buk.PreviewMarcajeAsync(sentido);
    Console.WriteLine(ok ? message : $"FALLO: {message}");
    return ok ? 0 : 1;
}

host.Run();
return 0;