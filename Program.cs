using Amazon.SecretsManager;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.OpenApi.Models;
using Serilog;
using Tarefista.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// após var builder = WebApplication.CreateBuilder(args);
Directory.CreateDirectory("logs");

var configuration = builder.Configuration;

// -----------------------------
// Logging (Serilog)
// -----------------------------
builder.Host.UseSerilog((context, services, loggerConfig) =>
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)   // pega config do serilog.json
        .ReadFrom.Services(services)                     // injeta dependências
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
);

// -----------------------------
// Services (Dependency Injection)
// -----------------------------
builder.Services.AddControllers(options =>
{
    //options.Filters.Add<GlobalExceptionFilter>(); // filtro global
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Tarefista API",
        Version = "v1",
        Description = "Documentação da API Tarefista"
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

builder.Services.AddSingleton<FirebaseService>();
builder.Services.AddSingleton<TokenBlacklistService>();

builder.Configuration.AddUserSecrets<Program>();

// -----------------------------
// App Pipeline
// -----------------------------
var app = builder.Build();

// permite reler o body depois que o MVC já o consumiu
app.Use(async (ctx, next) =>
{
    ctx.Request.EnableBuffering(bufferThreshold: 1024 * 60, bufferLimit: 1024 * 1024); // 60KB / 1MB
    await next();
});

app.UseSerilogRequestLogging();          // já existe

// ADICIONE / MANTENHA o ExceptionMiddleware AQUI, antes de Swagger/CORS/HTTPS
app.UseMiddleware<ExceptionMiddleware>();

// Swagger etc (já existentes)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Tarefista API V1");
    c.RoutePrefix = string.Empty;
});

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.MapControllers();


// -----------------------------
// Firebase Initialization
// -----------------------------
using (var scope = app.Services.CreateScope())
{
    try
    {
        var firebaseService = scope.ServiceProvider.GetRequiredService<FirebaseService>();
        await firebaseService.InitializeAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Falha ao inicializar Firebase (app continua rodando).");
    }
}

app.MapGet("/healthz", () => Results.Ok(new { ok = true, ts = DateTimeOffset.UtcNow }));
app.MapGet("/healthz/env", (IConfiguration cfg) =>
    Results.Ok(new
    {
        AWS_AccessKeyId = !string.IsNullOrEmpty(cfg["AWS:AccessKeyId"]),
        AWS_Secret = !string.IsNullOrEmpty(cfg["AWS:SecretAccessKey"]),
        JWT_Secret = !string.IsNullOrEmpty(cfg["JWT:Secret"])
    })
);


app.Run();
