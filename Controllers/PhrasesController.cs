using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class PhrasesController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<PhrasesController> _logger;

    public PhrasesController(IConfiguration config, ILogger<PhrasesController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetPhrase()
    {
        try
        {
            var phrases = _config.GetSection("Phrases").Get<string[]>();

            if (phrases == null || phrases.Length == 0)
                throw new InvalidOperationException("Nenhuma frase configurada no appsettings.");

            var today = DateTime.Now.Day;
            var phraseIndex = (today - 1) % phrases.Length;

            return Ok(new { phrase = phrases[phraseIndex] });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no endpoint GetPhrase");

            var errorResponse = new
            {
                message = ex.Message,
                type = ex.GetType().FullName,
                stackTrace = ex.StackTrace,
                innerException = ex.InnerException?.Message,
                source = ex.Source,
                path = HttpContext?.Request?.Path.Value,
                method = HttpContext?.Request?.Method,
                queryString = HttpContext?.Request?.QueryString.Value,
                timestamp = DateTime.UtcNow
            };

            return StatusCode(500, errorResponse);
        }
    }
}
