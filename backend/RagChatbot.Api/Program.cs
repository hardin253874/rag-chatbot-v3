using RagChatbot.Api.Middleware;
using RagChatbot.Core.Configuration;
using RagChatbot.Infrastructure.DependencyInjection;

// Load .env file from the project root (parent of backend/)
var envPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env");
var resolvedEnvPath = Path.GetFullPath(envPath);
if (File.Exists(resolvedEnvPath))
{
    // Real environment variables (Railway/platform, shell, tests) take precedence
    // over a committed .env — a stray .env must never override injected secrets.
    DotNetEnv.Env.Load(resolvedEnvPath, new DotNetEnv.LoadOptions(clobberExistingVars: false));
}

// Build configuration from environment variables
var appConfig = AppConfig.FromEnvironment();

var builder = WebApplication.CreateBuilder(args);

// Load appsettings.json from the app base directory (where the published DLL lives),
// so LLM profile config loads even when the runtime working directory differs from it
// (e.g., Railway starts the published DLL from a folder above /out).
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: true,
    reloadOnChange: false);

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
    _.LlmBaseUrl = appConfig.LlmBaseUrl;
    _.LlmModel = appConfig.LlmModel;
    _.LlmApiKey = appConfig.LlmApiKey;
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

// LLM profile options — bound from appsettings.json (LlmProfiles / InterfaceBindings).
// The registry itself is built and registered INSIDE AddInfrastructureServices,
// next to the keyed "bot" graph that consumes it, so the two can never drift apart.
var llmProfilesOptions = new LlmProfilesOptions
{
    LlmProfiles = builder.Configuration.GetSection("LlmProfiles").Get<List<LlmProfile>>()
        ?? new List<LlmProfile>(),
    InterfaceBindings = builder.Configuration.GetSection("InterfaceBindings").Get<Dictionary<string, InterfaceBinding>>()
        ?? new Dictionary<string, InterfaceBinding>()
};

// Register Infrastructure services (document processing, LLM profiles, bot graph, etc.)
builder.Services.AddInfrastructureServices(appConfig, llmProfilesOptions);

// Configure Kestrel to listen on the configured port
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(appConfig.Port);
});

var app = builder.Build();

app.UseCors();

// X-Api-Key auth — scoped EXCLUSIVELY to /bot/*; no existing route is gated.
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/bot"),
    branch => branch.UseMiddleware<BotApiKeyMiddleware>());

app.MapControllers();

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
