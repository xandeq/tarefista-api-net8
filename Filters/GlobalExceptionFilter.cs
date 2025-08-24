using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Serilog;

public class GlobalExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        Log.Error(context.Exception, "Erro não tratado");

        context.Result = new ObjectResult(new
        {
            error = "Erro interno no servidor",
            details = context.Exception.Message
        })
        {
            StatusCode = 500
        };
    }
}
