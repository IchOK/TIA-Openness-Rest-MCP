using System.Globalization;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

/// <summary>
/// Einstellungen des MCP-Servers aus appsettings.json (Abschnitt "TiaMcp"); Kommandozeilenparameter überschreiben sie.
/// Eingebaute Standardwerte gibt es nicht: fehlt ein Wert, startet der Server nicht.
/// </summary>
public class McpSettings {
  public const string Section = "TiaMcp";

  public string ApiKey { get; private init; } = "";
  public string BindAddress { get; private init; } = "";
  public int Port { get; private init; }
  public string RestHost { get; private init; } = "";
  public int RestPort { get; private init; }
  public string? RestPath { get; private init; }          // leer = automatische Suche (siehe TiaRestManager.ResolveRestExePath)
  public int RestTimeoutSeconds { get; private init; }
  public int LongRunningTimeoutMinutes { get; private init; }
  public int RestStartTimeoutSeconds { get; private init; }

  public static readonly Dictionary<string, string> SwitchMappings = new() {
    { "-k", $"{Section}:ApiKey" },
    { "--key", $"{Section}:ApiKey" },
    { "--apikey", $"{Section}:ApiKey" },
    { "--bind", $"{Section}:BindAddress" },
    { "-p", $"{Section}:Port" },
    { "--port", $"{Section}:Port" },
    { "--resthost", $"{Section}:RestHost" },
    { "-pr", $"{Section}:RestPort" },
    { "--restport", $"{Section}:RestPort" },
    { "--restpath", $"{Section}:RestPath" }
  };

  /// <exception cref="InvalidOperationException">Ein Wert fehlt oder ist ungültig</exception>
  public static McpSettings Load(IConfiguration configuration) {
    IConfigurationSection section = configuration.GetSection(Section);

    string Required(string key) {
      string? value = section[key];
      if (string.IsNullOrWhiteSpace(value)) {
        throw new InvalidOperationException($"Setting {Section}:{key} is missing. Set it in appsettings.json or pass it on the command line.");
      }
      return value.Trim();
    }

    int RequiredInt(string key, int min, int max) {
      string text = Required(key);
      if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < min || value > max) {
        throw new InvalidOperationException($"Setting {Section}:{key} '{text}' must be a number between {min} and {max}.");
      }
      return value;
    }

    return new McpSettings {
      ApiKey = Required("ApiKey"),
      BindAddress = Required("BindAddress"),
      Port = RequiredInt("Port", 1, 65535),
      RestHost = Required("RestHost"),
      RestPort = RequiredInt("RestPort", 1, 65535),
      RestPath = section["RestPath"],
      RestTimeoutSeconds = RequiredInt("RestTimeoutSeconds", 1, int.MaxValue),
      LongRunningTimeoutMinutes = RequiredInt("LongRunningTimeoutMinutes", 1, int.MaxValue),
      RestStartTimeoutSeconds = RequiredInt("RestStartTimeoutSeconds", 1, int.MaxValue)
    };
  }
}
