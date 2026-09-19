using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaMCP;

var switchMappings = new Dictionary<string, string>
{
    { "-k", "apiKey" },
    { "--key", "apiKey" },
    { "--apikey", "apiKey" },
    { "-p", "port" },
    { "--port", "port" },
    { "-pr", "restport" },
    { "--restport", "restport" },
    { "-v", "version" },
    { "--version", "version" },
    { "--restpath", "restpath" }
};

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddCommandLine(args, switchMappings);

// API-Key aus den Parametern verwenden, sondt mit Default-KEY starten
string? apiKey = builder.Configuration["apiKey"];
if (string.IsNullOrWhiteSpace(apiKey)) {
  apiKey = Network.TiaRestApiKeyDefault;
  Console.ForegroundColor = ConsoleColor.Red;
  Console.WriteLine("ACHTUNG: MCP-Server läuft mit Default-Key");
  Console.ResetColor();
}
string? portStr = builder.Configuration["port"];
if (string.IsNullOrWhiteSpace(portStr)) {
  portStr = Network.TiaMcpPort.ToString();
}
string? restPortStr = builder.Configuration["restport"];
if (string.IsNullOrWhiteSpace(restPortStr)) {
  restPortStr = Network.TiaRestPort.ToString();
}
string? restPath = builder.Configuration["restpath"];
if (string.IsNullOrWhiteSpace(restPath)) {
  restPath = "c:\\Users\\TopAdmin\\source\\repos\\Tophinke-IT\\TiaOpenness_Tools\\TiaOpenness_Tool_TiaREST\\bin\\Debug\\TiaOpenness_Tool_TiaREST.exe";
}
string? tiaVersion = builder.Configuration["version"];
if (string.IsNullOrWhiteSpace(tiaVersion)) {
  tiaVersion = Network.TiaVersionDefault;
}

// REST-Client initialisieren
TiaRestClient.Initialize(apiKey, restPortStr);

// MCP Server registrieren und Tools laden
builder.Services.AddMcpServer()
  .WithHttpTransport()
  .WithToolsFromAssembly(); // Lädt automatisch McpServerToolType-Klasse

var app = builder.Build();

// REST-API-App starten, falls sie nicht läuft
await TiaRestManager.EnsureSidecarIsRunningAsync(restPath, apiKey, restPortStr, tiaVersion);

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
app.Run($"http://0.0.0.0:{portStr}");

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