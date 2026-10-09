using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Tophinke.TiaOpenness.Tool.TiaMCP;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

public static class TiaRestManager {
  public const string RestExeName = "TiaOpenness_Tool_TiaREST.exe";

  /// <summary>
  /// Ermittelt den Pfad der REST-API-Anwendung. Ein relativer Pfad wird zum Programmordner des MCP-Servers aufgelöst.
  /// Ohne Angabe wird gesucht in: ..\TiaREST\ (Installation), im Programmordner selbst
  /// und im Build-Ordner des Repositorys (..\..\..\..\TiaOpenness_Tool_TiaREST\bin\&lt;Konfiguration&gt;\).
  /// </summary>
  /// <returns>Gefundener Pfad; sonst der erste Kandidat, damit die Fehlermeldung einen sinnvollen Pfad zeigt</returns>
  public static string ResolveRestExePath(string? configuredPath) {
    string baseDir = AppContext.BaseDirectory;
    if (!string.IsNullOrWhiteSpace(configuredPath)) {
      return Path.GetFullPath(configuredPath, baseDir);
    }

    string configuration = new DirectoryInfo(baseDir).Parent?.Name ?? "Release";
    string[] candidates = {
      Path.GetFullPath(Path.Combine(baseDir, "..", "TiaREST", RestExeName)),
      Path.GetFullPath(Path.Combine(baseDir, RestExeName)),
      Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "TiaOpenness_Tool_TiaREST", "bin", configuration, RestExeName))
    };
    return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
  }

  /// <summary>
  /// Prüft ob das Sidecar läuft. Wenn nicht, wird die .exe gestartet.
  /// API-Key und Port werden übergeben, damit beide Seiten zusammenpassen; TIA-Version und Siemens-Pfad
  /// stehen nur in der appsettings.json der REST-API.
  /// </summary>
  public static async Task EnsureSidecarIsRunningAsync(string restExePath, string restApiKey, int restPort, int startTimeoutSeconds) {
    ILogger logger = AppLog.For(typeof(TiaRestManager));
    if (await IsRestApiAliveAsync()) {
      logger.LogInformation("REST-API läuft bereits und ist erreichbar.");
      return;
    }

    logger.LogWarning("REST-API antwortet nicht. Versuche Prozess zu starten...");

    if (!File.Exists(restExePath)) {
      logger.LogError("FEHLER: .exe wurde unter '{RestExePath}' nicht gefunden!", restExePath);
      return;
    }

    // Prozess-Startkonfiguration festlegen
    string arguments = $"--apikey \"{restApiKey}\" --port {restPort}";
    var startInfo = new ProcessStartInfo {
      FileName = restExePath,
      Arguments = arguments,
      WorkingDirectory = Path.GetDirectoryName(restExePath),
      UseShellExecute = true,  // Zeigt ein eigenes Konsolenfenster an
      CreateNoWindow = false   // Auf 'true' stellen, falls das Sidecar komplett unsichtbar im Hintergrund laufen soll
    };

    logger.LogInformation("Starte REST-API mit Befehl: {FileName} {Arguments}", startInfo.FileName, startInfo.Arguments);
    try {
      Process.Start(startInfo);

      // Warten bis der HTTP-Port der REST-App antwortet
      for (int i = 0; i < startTimeoutSeconds; i++) {
        await Task.Delay(1000);
        if (await IsRestApiAliveAsync()) {
          logger.LogInformation("REST-API wurde erfolgreich gestartet!");
          return;
        }
      }

      logger.LogWarning("Prozess wurde gestartet, antwortet aber noch nicht.");
    } catch (Exception ex) {
      logger.LogError("FEHLER beim Starten der REST-App: {Message}", ex.Message);
    }
  }

  private static async Task<bool> IsRestApiAliveAsync() {
    try {
      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

      // Sendet eine Testanfrage über den bereits konfigurierten TiaRestClient
      using var response = await TiaRestClient.Client.GetAsync(Network.RouteHealth, cts.Token);

      // Sobald der HTTP-Server mit irgendeinem Statuscode antwortet (auch 401), läuft er
      return true;
    } catch {
      return false;
    }
  }
}