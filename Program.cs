using Amazon.SecretsManager;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
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
    options.Filters.Add<GlobalExceptionFilter>(); // filtro global
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

builder.Configuration.AddUserSecrets<Program>();

// -----------------------------
// App Pipeline
// -----------------------------
var app = builder.Build();

// Swagger (sempre habilitado)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Tarefista API V1");
    c.RoutePrefix = string.Empty; // Swagger na raiz "/"
});

// Middleware global de exceções (Serilog loga tudo)
app.UseMiddleware<ExceptionMiddleware>();

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.MapControllers();

// -----------------------------
// Firebase Initialization
// -----------------------------
using (var scope = app.Services.CreateScope())
{
    var firebaseService = scope.ServiceProvider.GetRequiredService<FirebaseService>();
    await firebaseService.InitializeAsync();
}

app.Run();
