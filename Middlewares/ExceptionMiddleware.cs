using System.Net;
using System.Text.Json;
using Serilog;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context); // continua pipeline
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro não tratado");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var result = JsonSerializer.Serialize(new
            {
                error = "Erro interno no servidor",
                details = ex.Message
            });

            await context.Response.WriteAsync(result);
        }
    }
}
