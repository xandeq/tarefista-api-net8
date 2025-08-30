using System.Text;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _cfg;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env, IConfiguration cfg)
    {
        _next = next;
        _logger = logger;
        _env = env;
        _cfg = cfg;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.TraceIdentifier;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // log rico no Serilog
            _logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId}", correlationId);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Correlation-Id"] = correlationId;

            // Só exponha detalhes quando: DEV || flag de config || header X-Debug
            bool expose =
                _env.IsDevelopment() ||
                string.Equals(_cfg["Errors:ExposeDetails"], "true", StringComparison.OrdinalIgnoreCase) ||
                context.Request.Headers.ContainsKey("X-Debug");

            string requestBody = null;

            // Captura do body (cap limitado) — só se expor detalhes
            if (expose && context.Request.ContentLength.GetValueOrDefault() <= 1024 * 256) // 256KB
            {
                try
                {
                    context.Request.Body.Position = 0;
                    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                    requestBody = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                }
                catch { /* não falhar só pq não deu pra ler o body */ }
            }

            // Monta a cadeia de inner exceptions
            static List<object> FlattenInner(Exception e, bool includeStack)
            {
                var list = new List<object>();
                var cur = e.InnerException;
                while (cur != null)
                {
                    list.Add(new
                    {
                        message = cur.Message,
                        type = cur.GetType().FullName,
                        stackTrace = includeStack ? cur.StackTrace : null
                    });
                    cur = cur.InnerException;
                }
                return list;
            }

            var errorPayload = new
            {
                correlationId,
                traceId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier,
                environment = _env.EnvironmentName,
                timestamp = DateTimeOffset.UtcNow,

                error = new
                {
                    message = ex.Message,
                    type = ex.GetType().FullName,
                    hResult = ex.HResult,
                    source = ex.Source,
                    targetSite = ex.TargetSite?.ToString(),
                    stackTrace = expose ? ex.StackTrace : null,
                    inner = expose ? FlattenInner(ex, includeStack: true) : null,
                    data = expose
                        ? ex.Data?.Cast<System.Collections.DictionaryEntry>()
                            .ToDictionary(d => d.Key?.ToString() ?? "", d => d.Value?.ToString())
                        : null
                },

                request = new
                {
                    method = context.Request.Method,
                    path = context.Request.Path.ToString(),
                    query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                    headers = expose
                        ? context.Request.Headers
                            .Where(h => h.Key is "User-Agent" or "Content-Type" or "Content-Length" or "Referer" or "Origin")
                            .ToDictionary(k => k.Key, v => v.Value.ToString())
                        : null,
                    body = expose ? requestBody : null
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(
                errorPayload,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );

            await context.Response.WriteAsync(json);
        }
    }

}
