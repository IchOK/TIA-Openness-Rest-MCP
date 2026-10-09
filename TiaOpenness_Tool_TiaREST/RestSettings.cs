using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Einstellungen der REST-API aus appsettings.json (Abschnitt "TiaRest") neben der EXE.
  /// Kommandozeilenparameter überschreiben die Werte aus der Datei; eingebaute Standardwerte gibt es nicht.
  /// </summary>
  internal class RestSettings {
    public const string FileName = "appsettings.json";
    public const string Section = "TiaRest";

    public string ApiKey { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }
    public string TiaVersion { get; private set; }
    public string SiemensPath { get; private set; }     // Ordner mit den "Portal V<Version>"-Installationen
    public string WorkDirectory { get; private set; }   // Ordner für temporäre Export-/Importdateien

    public static RestSettings Current { get; private set; }

    private static readonly Dictionary<string, string> SwitchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      { "-k", "ApiKey" }, { "--key", "ApiKey" }, { "--apikey", "ApiKey" },
      { "--host", "Host" },
      { "-p", "Port" }, { "--port", "Port" },
      { "-v", "TiaVersion" }, { "--version", "TiaVersion" },
      { "--siemenspath", "SiemensPath" },
      { "--workdir", "WorkDirectory" }
    };

    /// <summary>
    /// Lädt die Einstellungen und setzt <see cref="Current"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Datei ungültig oder ein Wert fehlt bzw. ist ungültig</exception>
    public static RestSettings Load(string[] args) {
      string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);
      var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      if (File.Exists(file)) {
        JObject root;
        try {
          root = JObject.Parse(File.ReadAllText(file));
        } catch (Exception ex) {
          throw new InvalidOperationException($"Invalid settings file {file}: {ex.Message}");
        }
        if (root[Section] is JObject section) {
          foreach (JProperty property in section.Properties()) {
            values[property.Name] = property.Value.Type == JTokenType.Null ? null : property.Value.ToString();
          }
        }
      }
      for (int i = 0; i + 1 < args.Length; i++) {
        if (SwitchMappings.TryGetValue(args[i], out string key)) {
          values[key] = args[++i];
        }
      }

      string portText = Required(values, "Port", file);
      if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535) {
        throw new InvalidOperationException($"Setting {Section}:Port '{portText}' is not a valid port.");
      }

      Current = new RestSettings {
        ApiKey = Required(values, "ApiKey", file),
        Host = Required(values, "Host", file),
        Port = port,
        TiaVersion = Required(values, "TiaVersion", file),
        SiemensPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(Required(values, "SiemensPath", file))),
        WorkDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(Required(values, "WorkDirectory", file)))
      };
      return Current;
    }

    private static string Required(Dictionary<string, string> values, string key, string file) {
      if (!values.TryGetValue(key, out string value) || string.IsNullOrWhiteSpace(value)) {
        throw new InvalidOperationException($"Setting {Section}:{key} is missing. Set it in {file} or pass it on the command line.");
      }
      return value.Trim();
    }
  }
}
