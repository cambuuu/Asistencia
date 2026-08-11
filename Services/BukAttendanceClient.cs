using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiscordAsistenciaBot.Services
{
    /// <summary>
    /// Marca asistencia en Buk (https://silva.buk.cl) replicando lo que hace el navegador:
    /// login en dos pasos (correo -> contraseña) y luego el POST que dispara el botón
    /// de Entrada/Salida del formulario #web-marking-form.
    /// </summary>
    public class BukAttendanceClient
    {
        private readonly HttpClient _httpClient;
        private readonly CookieContainer _cookies;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BukAttendanceClient> _logger;

        // Buk usa un solo marcaje a la vez; además el re-login concurrente pisaría cookies.
        private readonly SemaphoreSlim _gate = new(1, 1);

        private readonly string _baseUrl;
        private bool _loggedIn;

        public BukAttendanceClient(IConfiguration configuration, ILogger<BukAttendanceClient> logger)
        {
            _configuration = configuration;
            _logger = logger;

            _baseUrl = (_configuration["Buk:BaseUrl"] ?? "https://silva.buk.cl").TrimEnd('/');

            _cookies = new CookieContainer();
            var handler = new SocketsHttpHandler
            {
                CookieContainer = _cookies,
                UseCookies = true,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All,
                // El bot vive semanas; refrescar conexiones evita DNS obsoleto.
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            };

            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };

            // Buk responde 400 si no parece un navegador real.
            var h = _httpClient.DefaultRequestHeaders;
            h.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            h.Add("Accept-Language", "es-CL,es;q=0.9,en;q=0.8");
        }

        /// <summary>
        /// Verifica credenciales y acceso al formulario de marcaje SIN registrar entrada ni salida.
        /// Útil para probar la configuración sin ensuciar la ficha de asistencia.
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            await _gate.WaitAsync();
            try
            {
                var email = _configuration["Buk:Email"];
                var password = _configuration["Buk:Password"];

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                    return (false, "Faltan las credenciales de Buk (Buk:Email / Buk:Password).");

                var (ok, err) = await LoginAsync(email!, password!);
                if (!ok) return (false, err);

                var form = await LoadMarkingFormAsync();
                if (form is null)
                {
                    _loggedIn = false;
                    return (false, "Login OK, pero no se encontró #web-marking-form en el portal.");
                }

                return (true, $"Login OK y formulario de marcaje encontrado (job_id={form.JobId}).");
            }
            catch (Exception ex)
            {
                _loggedIn = false;
                return (false, $"Excepción: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        public Task<(bool Success, string Message)> MarkEntryAsync() => MarkAsync("ENTRADA");

        public Task<(bool Success, string Message)> MarkExitAsync() => MarkAsync("SALIDA");

        public async Task<(bool Success, string Message)> MarkAsync(string sentido)
        {
            await _gate.WaitAsync();
            try
            {
                var email = _configuration["Buk:Email"];
                var password = _configuration["Buk:Password"];

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                    return (false, "Faltan las credenciales de Buk (Buk:Email / Buk:Password).");

                // Primer intento con la sesión que tengamos; si está vencida, relogin y reintento.
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    if (!_loggedIn)
                    {
                        var (ok, err) = await LoginAsync(email!, password!);
                        if (!ok) return (false, err);
                    }

                    var form = await LoadMarkingFormAsync();
                    if (form is null)
                    {
                        // El portal no trajo el formulario => la sesión caducó.
                        _logger.LogWarning("No se encontró #web-marking-form (intento {Attempt}). Sesión vencida.", attempt);
                        _loggedIn = false;
                        if (attempt == 1) continue;
                        return (false, "No se pudo cargar el formulario de marcaje en el portal de Buk.");
                    }

                    return await PostMarcajeAsync(form, sentido);
                }

                return (false, "No se pudo marcar tras reintentar.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excepción al marcar {Sentido}", sentido);
                _loggedIn = false;
                return (false, $"Excepción: {ex.Message}");
            }
            finally
            {
                _gate.Release();
            }
        }

        // ---------------------------------------------------------------- login

        private async Task<(bool Success, string Error)> LoginAsync(string email, string password)
        {
            _logger.LogInformation("Iniciando sesión en Buk como {Email}...", email);

            // Sesión limpia: cookies viejas de Rails hacen fallar el authenticity_token.
            ClearCookies();

            // Paso 0: GET del formulario de login para obtener el CSRF.
            var loginPage = await GetStringAsync($"{_baseUrl}/users/login");
            var token = ExtractAuthenticityToken(loginPage);
            if (token is null) return (false, "No se encontró authenticity_token en la página de login.");

            // Paso 1: enviar el correo. Buk responde con el formulario de contraseña.
            var step2 = await PostFormAsync($"{_baseUrl}/users/login", $"{_baseUrl}/users/login", new Dictionary<string, string>
            {
                ["authenticity_token"] = token,
                ["user[email]"] = email
            });

            if (!step2.IsSuccess) return (false, $"Paso 1 del login falló (HTTP {(int)step2.StatusCode}).");

            var token2 = ExtractAuthenticityToken(step2.Body);
            if (token2 is null) return (false, "No se encontró el formulario de contraseña (¿correo no reconocido?).");

            // Paso 2: enviar la contraseña.
            var result = await PostFormAsync($"{_baseUrl}/users/sign_in", $"{_baseUrl}/users/login", new Dictionary<string, string>
            {
                ["authenticity_token"] = token2,
                ["login_email"] = email,
                ["user[email]"] = email,
                ["user[password]"] = password
            });

            if (!result.IsSuccess)
                return (false, $"Login rechazado (HTTP {(int)result.StatusCode}).");

            // Un login correcto termina en el portal; uno fallido vuelve a mostrar el form.
            var landedOnLogin = result.FinalUrl.Contains("/users/sign_in", StringComparison.OrdinalIgnoreCase)
                             || result.FinalUrl.Contains("/users/login", StringComparison.OrdinalIgnoreCase);

            if (landedOnLogin)
                return (false, "Credenciales de Buk inválidas o se requiere verificación adicional.");

            _loggedIn = true;
            _logger.LogInformation("Sesión de Buk iniciada. URL final: {Url}", result.FinalUrl);
            return (true, string.Empty);
        }

        private void ClearCookies()
        {
            foreach (Cookie cookie in _cookies.GetAllCookies())
                cookie.Expired = true;
        }

        // ------------------------------------------------------------- marcaje

        private sealed record MarkingForm(string Token, string JobId, string DefaultJobId, string Html)
        {
            /// <summary>
            /// ic-id del boton del sentido pedido. Normalmente intercooler.js lo asigna
            /// en el navegador, asi que el HTML servido puede no traerlo.
            /// </summary>
            public string IcIdFor(string sentido)
            {
                var btn = Regex.Match(Html, $@"<button[^>]*sentido={Regex.Escape(sentido)}[^>]*>", RegexOptions.IgnoreCase);
                if (!btn.Success) return string.Empty;

                var icId = Regex.Match(btn.Value, @"ic-id=""([^""]+)""");
                return icId.Success ? icId.Groups[1].Value : string.Empty;
            }
        }

        /// <summary>Carga el portal y extrae los campos de #web-marking-form.</summary>
        private async Task<MarkingForm?> LoadMarkingFormAsync()
        {
            var portal = await GetStringAsync($"{_baseUrl}/static_pages/portal");

            var formIdx = portal.IndexOf("id=\"web-marking-form\"", StringComparison.OrdinalIgnoreCase);
            if (formIdx < 0) return null;

            // El portal tiene varios authenticity_token; queremos el de este formulario.
            var scope = portal[formIdx..];
            var endIdx = scope.IndexOf("</form>", StringComparison.OrdinalIgnoreCase);
            if (endIdx > 0) scope = scope[..endIdx];

            var token = ExtractAuthenticityToken(scope);
            if (token is null) return null;

            var jobId = ExtractHiddenValue(scope, "job_id") ?? string.Empty;
            var defaultJobId = ExtractHiddenValue(scope, "default_job_id") ?? jobId;

            return new MarkingForm(token, jobId, defaultJobId, scope);
        }

        private async Task<(bool Success, string Message)> PostMarcajeAsync(MarkingForm form, string sentido)
        {
            var url = $"{_baseUrl}/employee_portal/web_marking/marcaje?sentido={Uri.EscapeDataString(sentido)}";

            // Mismos campos que intercooler.js envía vía ic-include="#web-marking-form".
            // La geolocalización es opcional: el propio portal avisa "Puedes marcar igualmente".
            var fields = new Dictionary<string, string>
            {
                // intercooler.js antepone esto a los campos del formulario; el backend
                // lo usa para reconocer la peticion como parcial.
                ["ic-request"] = "true",
                ["utf8"] = "✓",
                ["authenticity_token"] = form.Token,
                ["latitude"] = _configuration["Buk:Latitude"] ?? string.Empty,
                ["longitude"] = _configuration["Buk:Longitude"] ?? string.Empty,
                ["job_id"] = form.JobId,
                ["default_job_id"] = form.DefaultJobId,
                // El boton clickeado se serializa con su name y su value vacio.
                ["button"] = string.Empty,
                ["ic-element-name"] = "button",
                ["_method"] = "POST"
            };

            // ic-id lo asigna intercooler.js en runtime; solo lo mandamos si el HTML lo trae.
            var icId = form.IcIdFor(sentido);
            if (!string.IsNullOrEmpty(icId))
                fields["ic-id"] = icId;

            fields["ic-current-url"] = $"{_baseUrl}/static_pages/portal";

            // Se registran los campos enviados (sin el CSRF) para poder diagnosticar un
            // fallo desde los logs, sin tener que reproducir un marcaje real.
            var enviados = string.Join("&", fields
                .Where(f => f.Key != "authenticity_token")
                .Select(f => $"{f.Key}={f.Value}"));

            _logger.LogInformation("Marcando {Sentido} en {Url}. Campos: {Campos}", sentido, url, enviados);

            var result = await PostFormAsync(url, $"{_baseUrl}/static_pages/portal", fields, isIntercooler: true, csrfToken: form.Token);

            var text = HtmlToText(result.Body);
            if (text.Length > 300) text = text[..300] + "…";

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Marcaje {Sentido} falló. HTTP {Status}. Content-Type: {Type}. Cuerpo: {Body}",
                    sentido, (int)result.StatusCode, result.ContentType, text);

                // 401/403/422 suelen significar sesión o CSRF vencidos.
                if (result.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    or HttpStatusCode.UnprocessableEntity)
                    _loggedIn = false;

                return (false, string.IsNullOrWhiteSpace(text) ? $"Error HTTP {(int)result.StatusCode}" : text);
            }

            _logger.LogInformation("Marcaje {Sentido} OK. Respuesta: {Body}", sentido, text);
            return (true, string.IsNullOrWhiteSpace(text) ? $"{sentido} registrada." : text);
        }

        // --------------------------------------------------------------- HTTP

        private sealed record HttpResult(HttpStatusCode StatusCode, string Body, string FinalUrl, string ContentType = "")
        {
            public bool IsSuccess => (int)StatusCode is >= 200 and < 400;
        }

        private async Task<string> GetStringAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            using var response = await _httpClient.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }

        private async Task<HttpResult> PostFormAsync(
            string url,
            string referer,
            Dictionary<string, string> fields,
            bool isIntercooler = false,
            string? csrfToken = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(fields)
            };

            request.Headers.TryAddWithoutValidation("Origin", _baseUrl);
            request.Headers.TryAddWithoutValidation("Referer", referer);

            if (csrfToken is not null)
                request.Headers.TryAddWithoutValidation("X-CSRF-Token", csrfToken);

            if (isIntercooler)
            {
                // Cabeceras exactas de intercooler.js. El Accept importa: Buk usa
                // intercooler-rails, que registra el MIME text/html-partial. Pidiendo
                // text/html normal, Rails busca una vista completa que no existe para
                // esta accion y responde 500.
                request.Headers.TryAddWithoutValidation("Accept", "text/html-partial, */*; q=0.9");
                request.Headers.TryAddWithoutValidation("X-IC-Request", "true");
                request.Headers.TryAddWithoutValidation("X-HTTP-Method-Override", "POST");
            }
            else
            {
                request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            }

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;

            var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;

            return new HttpResult(response.StatusCode, body, finalUrl, contentType);
        }

        // -------------------------------------------------------------- parsing

        private static string? ExtractAuthenticityToken(string html)
        {
            var m = Regex.Match(html, @"name=""authenticity_token""\s+value=""([^""]+)""");
            if (!m.Success)
                m = Regex.Match(html, @"name=""csrf-token""\s+content=""([^""]+)""");
            return m.Success ? HttpUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        private static string? ExtractHiddenValue(string html, string name)
        {
            var m = Regex.Match(html, $@"name=""{Regex.Escape(name)}""[^>]*?value=""([^""]*)""");
            return m.Success ? HttpUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        /// <summary>Convierte el fragmento HTML que devuelve Buk en un texto legible para el DM.</summary>
        private static string HtmlToText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var text = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = HttpUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }
    }
}
