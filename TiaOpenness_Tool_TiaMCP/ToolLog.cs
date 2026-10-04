using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text.Json;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

/// <summary>
/// Stellt Logger für Code ohne Dependency Injection bereit (statische Tools, Start-Hilfen).
/// Bis Initialize aufgerufen wurde, wird nichts geloggt.
/// </summary>
public static class AppLog {
  private static ILoggerFactory _factory = NullLoggerFactory.Instance;

  public static void Initialize(ILoggerFactory factory) {
    _factory = factory;
  }

  public static ILogger For<T>() => _factory.CreateLogger<T>();

  public static ILogger For(Type type) => _factory.CreateLogger(type);
}

/// <summary>
/// Protokolliert Beginn und Ende eines Tool-Aufrufs inklusive der verstrichenen Zeit.
/// </summary>
public sealed class ToolActivity {
  private readonly ILogger _logger;
  private readonly string _action;
  private readonly Stopwatch _stopwatch;

  private ToolActivity(ILogger logger, string action) {
    _logger = logger;
    _action = action;
    _stopwatch = Stopwatch.StartNew();
  }

  /// <summary>
  /// Startet die Zeitmessung und loggt den Beginn, z. B. "Baustein wird abgerufen...".
  /// </summary>
  /// <typeparam name="T">Tool-Klasse; bestimmt die Log-Kategorie</typeparam>
  /// <param name="action">Beschreibung der Bearbeitung ohne Auslassungspunkte</param>
  public static ToolActivity Start<T>(string action) {
    var activity = new ToolActivity(AppLog.For<T>(), action);
    activity._logger.LogInformation("{Action}...", action);
    return activity;
  }

  /// <summary>
  /// Loggt das Ende mit der verstrichenen Zeit (bei Fehlern als Warnung mit Status und Fehlertext)
  /// und gibt das Ergebnis unverändert zurück.
  /// </summary>
  public CallToolResult Complete(CallToolResult result) {
    _stopwatch.Stop();
    string elapsed = FormatElapsed(_stopwatch.Elapsed);
    if (result.IsError == true) {
      GetError(result, out int? statusCode, out string? error);
      _logger.LogWarning("{Action}: fehlgeschlagen nach {Elapsed} (Status {StatusCode}): {Error}",
        _action, elapsed, statusCode?.ToString() ?? "?", error ?? "(no details)");
    } else {
      _logger.LogInformation("{Action}: abgeschlossen nach {Elapsed}", _action, elapsed);
    }
    return result;
  }

  private static string FormatElapsed(TimeSpan elapsed) {
    return elapsed.TotalSeconds < 1
      ? $"{elapsed.TotalMilliseconds:0} ms"
      : $"{elapsed.TotalSeconds:0.0} s";
  }

  /// <summary>
  /// Liest StatusCode und Error aus dem McpApiResponse-JSON des Tool-Ergebnisses.
  /// </summary>
  private static void GetError(CallToolResult result, out int? statusCode, out string? error) {
    statusCode = null;
    error = null;
    string? text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
    if (string.IsNullOrEmpty(text)) {
      return;
    }
    try {
      using JsonDocument document = JsonDocument.Parse(text);
      if (document.RootElement.TryGetProperty("statusCode", out JsonElement status) && status.TryGetInt32(out int code)) {
        statusCode = code;
      }
      if (document.RootElement.TryGetProperty("error", out JsonElement errorElement)) {
        error = errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() : errorElement.GetRawText();
      }
    } catch (JsonException) {
      error = text;
    }
    if (error != null && error.Length > MaxErrorLength) {
      error = error.Substring(0, MaxErrorLength) + " …";
    }
  }

  private const int MaxErrorLength = 500;
}
