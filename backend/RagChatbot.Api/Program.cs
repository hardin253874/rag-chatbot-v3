using RagChatbot.Core.Configuration;
using RagChatbot.Infrastructure.DependencyInjection;

// Load .env file from the project root (parent of backend/)
var envPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env");
var resolvedEnvPath = Path.GetFullPath(envPath);
if (File.Exists(resolvedEnvPath))
{
    DotNetEnv.Env.Load(resolvedEnvPath);
}

// Build configuration from environment variables
var appConfig = AppConfig.FromEnvironment();

var builder = WebApplication.CreateBuilder(args);

// Register strongly-typed configuration
builder.Services.Configure<AppConfig>(_ =>
{
    _.OpenAiApiKey = appConfig.OpenAiApiKey;
    _.PineconeApiKey = appConfig.PineconeApiKey;
    _.Port = appConfig.Port;
    _.RewriteLlmBaseUrl = appConfig.RewriteLlmBaseUrl;
    _.RewriteLlmModel = appConfig.RewriteLlmModel;
    _.RewriteLlmApiKey = appConfig.RewriteLlmApiKey;
    _.ChunkSize = appConfig.ChunkSize;
    _.ChunkOverlap = appConfig.ChunkOverlap;
    _.PineconeHost = appConfig.PineconeHost;
    _.PineconeNamespace = appConfig.PineconeNamespace;
});

// CORS — allow any origin (ADR-012)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();

// Register Infrastructure services (document processing, etc.)
builder.Services.AddInfrastructureServices(appConfig);

// Configure Kestrel to listen on the configured port
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(appConfig.Port);
});

var app = builder.Build();

app.UseCors();
app.MapControllers();

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
