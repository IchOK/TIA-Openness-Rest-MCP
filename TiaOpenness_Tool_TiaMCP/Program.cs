using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaMCP;

// appsettings.json liegt neben der Anwendung; das Arbeitsverzeichnis kann beim Start durch einen MCP-Client beliebig sein.
// Die Argumente gehen nur an AddCommandLine mit Mappings, weil der Standard-Parser unbekannte Kurzschalter (-k) ablehnt.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddCommandLine(args, McpSettings.SwitchMappings);

McpSettings settings;
try {
  settings = McpSettings.Load(builder.Configuration);
} catch (InvalidOperationException ex) {
  Console.Error.WriteLine($"Error: {ex.Message}");
  return 1;
}
string apiKey = settings.ApiKey;
string restPath = TiaRestManager.ResolveRestExePath(settings.RestPath);

// REST-Client initialisieren
TiaRestClient.Initialize(apiKey, settings.RestHost, settings.RestPort, settings.RestTimeoutSeconds, settings.LongRunningTimeoutMinutes);

// MCP Server registrieren und Tools laden
builder.Services.AddMcpServer()
  .WithHttpTransport()
  .WithToolsFromAssembly(); // Lädt automatisch McpServerToolType-Klasse

var app = builder.Build();

// Logger für statische Tools und Hilfsklassen; Level und Format kommen aus appsettings (Logging)
AppLog.Initialize(app.Services.GetRequiredService<ILoggerFactory>());

// REST-API-App starten, falls sie nicht läuft
await TiaRestManager.EnsureSidecarIsRunningAsync(restPath, apiKey, settings.RestPort, settings.RestStartTimeoutSeconds);

// Middleware: accept X-API-Key (Cursor) or Authorization: Bearer (Hermes / OAuth-style clients)
app.Use(async (context, next) => {
  if (!IsAuthorized(context.Request, apiKey)) {
    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
    context.Response.ContentType = "text/plain";
    await context.Response.WriteAsync(
      $"Error: Unauthorized access. Provide header '{Network.TiaRestApiKeyHeader}' or 'Authorization: Bearer <token>'.");
    return;
  }

  await next();
});

app.MapMcp("/mcp");
app.Run($"http://{settings.BindAddress}:{settings.Port}");
return 0;

static bool IsAuthorized(HttpRequest request, string expectedApiKey) {
  if (request.Headers.TryGetValue(Network.TiaRestApiKeyHeader, out var apiKeyHeader)
      && string.Equals(apiKeyHeader.ToString(), expectedApiKey, StringComparison.Ordinal)) {
    return true;
  }

  if (request.Headers.TryGetValue("Authorization", out var authorization)) {
    string value = authorization.ToString().Trim();
    const string bearerPrefix = "Bearer ";
    if (value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)) {
      string token = value.Substring(bearerPrefix.Length).Trim();
      if (string.Equals(token, expectedApiKey, StringComparison.Ordinal)) {
        return true;
      }
    }
  }

  return false;
}