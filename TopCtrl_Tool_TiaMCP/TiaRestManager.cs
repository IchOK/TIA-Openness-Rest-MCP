using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Tophinke.TopCtrl.Tool.TiaMCP;
using Tophinke.TopCtrl.Tool.Consts;

namespace Tophinke.TopCtrl.Tool.TiaMCP;

public static class TiaRestManager {
  /// <summary>
  /// Prüft ob das Sidecar läuft. Wenn nicht, wird die .exe gestartet.
  /// </summary>
  public static async Task EnsureSidecarIsRunningAsync(string restExePath, string restApiKey, string restPort, string tiaVersion) {
    if (await IsRestApiAliveAsync()) {
      Console.WriteLine("[TiaRestManager] REST-API läuft bereits und ist erreichbar.");
      return;
    }

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("[TiaRestManager] REST-API antwortet nicht. Versuche Prozess zu starten...");
    Console.ResetColor();

    if (!File.Exists(restExePath)) {
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine($"[TiaRestManager] FEHLER: .exe wurde unter '{restExePath}' nicht gefunden!");
      Console.ResetColor();
      return;
    }

    // Prozess-Startkonfiguration festlegen
    string arguments = "";
    if (restApiKey != null && restApiKey != "") {
      arguments += $"--apikey \"{restApiKey}\" ";
    }
    if (restPort != null && restPort != "") {
      arguments += $"--port {restPort} ";
    }
    if (tiaVersion != null && tiaVersion != "") {
      arguments += $"--version {tiaVersion} ";
    }
    var startInfo = new ProcessStartInfo {
      FileName = restExePath,
      Arguments = arguments,
      UseShellExecute = true,  // Zeigt ein eigenes Konsolenfenster an
      CreateNoWindow = false   // Auf 'true' stellen, falls das Sidecar komplett unsichtbar im Hintergrund laufen soll
    };

    Console.WriteLine($"[TiaRestManager] Starte REST-API mit Befehl: {startInfo.FileName} {startInfo.Arguments}");
    try {
      Process.Start(startInfo);

      // Warten bis der HTTP-Port der REST-App antwortet (max. 10 Sekunden)
      for (int i = 0; i < 10; i++) {
        await Task.Delay(1000);
        if (await IsRestApiAliveAsync()) {
          Console.ForegroundColor = ConsoleColor.Green;
          Console.WriteLine("[TiaRestManager] REST-API wurde erfolgreich gestartet!");
          Console.ResetColor();
          return;
        }
      }

      Console.WriteLine("[TiaRestManager] WARNUNG: Prozess wurde gestartet, antwortet aber noch nicht.");
    } catch (Exception ex) {
      Console.WriteLine($"[TiaRestManager] FEHLER beim Starten der REST-App: {ex.Message}");
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