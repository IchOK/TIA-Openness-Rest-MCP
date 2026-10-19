using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiConnections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.HMI;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.HMI {
  /// <summary>
  /// Fundstelle einer WinCC Unified-Software im Projekt.
  /// </summary>
  internal sealed class UnifiedTarget {
    public Device Device;
    public DeviceItem DeviceItem;
    public HmiSoftware Software;
  }

  /// <summary>
  /// Setzt Eigenschaften nur bei Abweichung und merkt sich die Änderungen ("Feld: alt -> neu").
  /// </summary>
  internal sealed class ChangeTracker {
    public readonly List<string> Details = new List<string>();

    /// <summary>
    /// Setzt einen Wert, wenn value nicht null ist und vom bisherigen Wert abweicht.
    /// </summary>
    public void Set(string field, string value, Func<string> get, Action<string> set) {
      if (value == null) {
        return;
      }
      string old = cTiaHmi.Safe(get);
      if (string.Equals(old, value, StringComparison.Ordinal)) {
        return;
      }
      try {
        set(value);
      } catch (Exception ex) {
        throw new InvalidOperationException($"{field}: {ex.Message}", ex);
      }
      Details.Add($"{field}: {old ?? "(none)"} -> {value}");
    }

    public void Note(string text) {
      Details.Add(text);
    }
  }

  /// <summary>
  /// Zugriff auf WinCC Unified-Geräte (Panels und PC-Runtimes).
  /// </summary>
  static internal class cTiaHmi {
    /// <summary>
    /// Listet alle Geräte mit WinCC Unified-Software mit ihren Verbindungen und der Anzahl der Variablen, Meldungen, Archive und Bilder.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <returns>JSON-String mit einer Liste von DeviceInfo oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.DevicesList)]
    static public string DevicesList(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      return Execute(context, processIdStr, projectName, project => {
        var list = new List<DeviceInfo>();
        foreach (UnifiedTarget target in FindAll(project)) {
          HmiSoftware hmi = target.Software;
          list.Add(new DeviceInfo {
            DeviceName = target.Device.Name,
            DeviceItemName = target.DeviceItem.Name,
            HmiName = hmi.Name,
            TypeIdentifier = target.Device.TypeIdentifier
              ?? target.Device.DeviceItems.Select(i => Safe(() => i.TypeIdentifier)).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
            Connections = hmi.Connections.Select(ToInfo).ToList(),
            TagTableCount = CountTagTables(hmi),
            TagCount = hmi.Tags.Count,
            DiscreteAlarmCount = hmi.DiscreteAlarms.Count,
            AnalogAlarmCount = hmi.AnalogAlarms.Count,
            AlarmClassCount = hmi.AlarmClasses.Count,
            DataLogCount = hmi.DataLogs.Count,
            AlarmLogCount = hmi.AlarmLogs.Count,
            ScreenCount = CountScreens(hmi)
          });
        }
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(list);
      });
    }

    static ConnectionInfo ToInfo(HmiConnection connection) {
      return new ConnectionInfo {
        Name = connection.Name,
        CommunicationDriver = connection.CommunicationDriver,
        Partner = connection.Partner,
        Station = connection.Station,
        Node = connection.Node,
        Comment = connection.Comment
      };
    }

    static int CountTagTables(HmiSoftware hmi) {
      int count = hmi.TagTables.Count;
      var groups = new Stack<Siemens.Engineering.HmiUnified.HmiTags.HmiTagTableGroup>(hmi.TagTableGroups);
      while (groups.Count > 0) {
        var group = groups.Pop();
        count += group.TagTables.Count;
        foreach (var child in group.Groups) {
          groups.Push(child);
        }
      }
      return count;
    }

    static int CountScreens(HmiSoftware hmi) {
      int count = hmi.Screens.Count;
      var groups = new Stack<Siemens.Engineering.HmiUnified.UI.ScreenGroup.HmiScreenGroup>(hmi.ScreenGroups);
      while (groups.Count > 0) {
        var group = groups.Pop();
        count += group.Screens.Count;
        foreach (var child in group.Groups) {
          groups.Push(child);
        }
      }
      return count;
    }

    #region Hilfsmethoden

    /// <summary>
    /// Liest einen Wert; Eigenschaften, die für ein Objekt nicht gelten, werfen in Openness eine Ausnahme und liefern hier den Standardwert.
    /// </summary>
    static internal T Safe<T>(Func<T> getter) {
      try {
        return getter();
      } catch {
        return default(T);
      }
    }

    static internal string NullIfEmpty(string text) {
      return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Verweis auf eine Variable oder Verbindung; leere Werte und lokalisierte Platzhalter wie "&lt;Keine Variable&gt;" werden null.
    /// </summary>
    static internal string Reference(string text) {
      return string.IsNullOrEmpty(text) || (text.StartsWith("<") && text.EndsWith(">")) ? null : text;
    }

    /// <summary>
    /// Wandelt einen Openness-Wert in Text (invariante Kultur); null bleibt null.
    /// </summary>
    static internal string ValueText(object value) {
      if (value == null) {
        return null;
      }
      return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Wandelt Text in den Typ des bisherigen Werts; ohne bisherigen Wert in bool, Zahl oder Text.
    /// </summary>
    static internal object ParseValue(string text, object current) {
      var culture = System.Globalization.CultureInfo.InvariantCulture;
      if (current != null && !(current is string)) {
        Type type = current.GetType();
        if (type.IsEnum) {
          return Enum.Parse(type, text, true);
        }
        return Convert.ChangeType(text, type, culture);
      }
      if (current is string) {
        return text;
      }
      if (bool.TryParse(text, out bool b)) {
        return b;
      }
      if (long.TryParse(text, System.Globalization.NumberStyles.Integer, culture, out long l)) {
        return l;
      }
      if (double.TryParse(text, System.Globalization.NumberStyles.Float, culture, out double d)) {
        return d;
      }
      return text;
    }

    /// <summary>
    /// Liest einen Enum-Wert aus Text (Groß-/Kleinschreibung egal).
    /// </summary>
    static internal TEnum ParseEnum<TEnum>(string text, string field) where TEnum : struct {
      if (Enum.TryParse(text, true, out TEnum value)) {
        return value;
      }
      throw new ArgumentException($"{field}: unknown value '{text}'. Allowed: {string.Join(", ", Enum.GetNames(typeof(TEnum)))}.");
    }

    /// <summary>
    /// Alle Sprachen eines mehrsprachigen Texts mit nicht leerem Inhalt.
    /// </summary>
    static internal Dictionary<string, string> Texts(MultilingualText text) {
      var result = new Dictionary<string, string>();
      if (text == null) {
        return result;
      }
      foreach (MultilingualTextItem item in text.Items) {
        string value = Safe(() => item.Text);
        if (!string.IsNullOrEmpty(value)) {
          result[item.Language.Culture.Name] = value;
        }
      }
      return result;
    }

    /// <summary>
    /// Setzt einen mehrsprachigen Text je Sprache (Schlüssel z. B. "de-DE").
    /// </summary>
    static internal void SetTexts(ProjectBase project, MultilingualText text, Dictionary<string, string> values) {
      foreach (var entry in values) {
        Language language = project.LanguageSettings.ActiveLanguages
          .FirstOrDefault(l => string.Equals(l.Culture.Name, entry.Key, StringComparison.OrdinalIgnoreCase));
        if (language == null) {
          throw new InvalidOperationException($"Language {entry.Key} is not an active project language.");
        }
        SetText(text, language, entry.Value);
      }
    }

    /// <summary>
    /// Platzhaltersuche mit * und ? (Groß-/Kleinschreibung egal); ohne Muster passt alles.
    /// </summary>
    static internal Func<string, bool> NameFilter(string pattern) {
      if (string.IsNullOrWhiteSpace(pattern)) {
        return name => true;
      }
      var regex = new System.Text.RegularExpressions.Regex(
        "^" + System.Text.RegularExpressions.Regex.Escape(pattern.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
      return name => regex.IsMatch(name ?? "");
    }

    /// <summary>
    /// Führt eine Aktion im Projekt aus und setzt bei Fehlern Statuscode und Fehlertext wie die übrigen Handler.
    /// </summary>
    static internal string Execute(HttpListenerContext context, string processIdStr, string projectName, Func<ProjectBase, string> action) {
      try {
        string kind = context.Request.QueryString["kind"];
        return TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, action, kind);
      } catch (TiaProjectNotFoundException ex) {
        return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: {ex.Message}");
      } catch (EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        return ErrorResponse(context, HttpStatusCode.ServiceUnavailable, "Error: TIA session unavailable. Please re-open TIA Portal.");
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error accessing TIA project: {ex.Message}");
      }
    }

    /// <summary>
    /// Führt eine Aktion mit der Unified-Software aus, die über die Query-Parameter deviceName/deviceItemName gewählt ist.
    /// </summary>
    static internal string ExecuteOnHmi(HttpListenerContext context, Func<ProjectBase, UnifiedTarget, string> action) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];
      return Execute(context, processIdStr, projectName, project => {
        string error = Find(project, deviceName, deviceItemName, out UnifiedTarget target);
        if (error != null) {
          return ErrorResponse(context, HttpStatusCode.NotFound, error);
        }
        return action(project, target);
      });
    }

    /// <summary>
    /// Antwort eines Schreib- oder Löschauftrags; 400, wenn ein Eintrag fehlgeschlagen ist.
    /// </summary>
    static internal string ChangeResponse(HttpListenerContext context, UnifiedTarget target, List<Change> changes) {
      if (changes.Any(c => c.Error != null)) {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
      }
      context.Response.ContentType = "application/json";
      return JsonConvert.SerializeObject(new ChangeResult {
        DeviceName = target.Device.Name,
        DeviceItemName = target.DeviceItem.Name,
        HmiName = target.Software.Name,
        Changes = changes
      }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }

    static internal string ListResponse<T>(HttpListenerContext context, UnifiedTarget target, List<T> items) {
      context.Response.ContentType = "application/json";
      return JsonConvert.SerializeObject(new ListResult<T> {
        DeviceName = target.Device.Name,
        DeviceItemName = target.DeviceItem.Name,
        HmiName = target.Software.Name,
        Items = items
      }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }

    /// <summary>
    /// Liest den JSON-Body; null bei Fehler (Fehlerantwort ist dann gesetzt).
    /// </summary>
    static internal T ReadRequest<T>(HttpListenerContext context, out string errorResponse) where T : class {
      errorResponse = null;
      try {
        T request = ReadBody<T>(context);
        if (request == null) {
          errorResponse = ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body is missing.");
        }
        return request;
      } catch (Exception ex) {
        errorResponse = ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Führt eine Änderung für ein Objekt aus und protokolliert sie als Change.
    /// </summary>
    static internal Change Apply(string kind, string name, bool created, Action<ChangeTracker> apply) {
      var change = new Change { Kind = kind, Name = name, Action = created ? "Created" : "Updated" };
      var tracker = new ChangeTracker();
      try {
        apply(tracker);
      } catch (Exception ex) {
        change.Error = ex.Message;
      }
      if (!created && tracker.Details.Count == 0 && change.Error == null) {
        change.Action = "Unchanged";
      }
      if (tracker.Details.Count > 0) {
        change.Details = string.Join("; ", tracker.Details);
      }
      return change;
    }

    /// <summary>
    /// Sucht und löscht ein Objekt und protokolliert das Ergebnis.
    /// </summary>
    static internal Change DeleteObject<T>(string kind, string name, Func<T> find, Action<T> delete) where T : class {
      var change = new Change { Kind = kind, Name = name, Action = "Deleted" };
      try {
        T item = find();
        if (item == null) {
          change.Error = $"{kind} {name} not found.";
        } else {
          delete(item);
        }
      } catch (Exception ex) {
        change.Error = ex.Message;
      }
      return change;
    }

    /// <summary>
    /// Alle Geräte mit WinCC Unified-Software, auch in Geräteordnern und als Unterelement (z. B. PC-Station).
    /// </summary>
    static internal List<UnifiedTarget> FindAll(ProjectBase project) {
      var targets = new List<UnifiedTarget>();
      foreach (Device device in AllDevices(project)) {
        foreach (DeviceItem item in device.DeviceItems) {
          Collect(device, item, targets);
        }
      }
      return targets;
    }

    /// <summary>
    /// Sucht die WinCC Unified-Software eines Geräts. Ohne deviceName/deviceItemName wird das einzige Unified-Gerät verwendet.
    /// </summary>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static internal string Find(ProjectBase project, string deviceName, string deviceItemName, out UnifiedTarget target) {
      target = null;
      List<UnifiedTarget> all = FindAll(project);
      if (all.Count == 0) {
        return "Error: No WinCC Unified device found in the project.";
      }
      IEnumerable<UnifiedTarget> matches = all;
      if (!string.IsNullOrEmpty(deviceName)) {
        matches = matches.Where(t => t.Device.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
      }
      if (!string.IsNullOrEmpty(deviceItemName)) {
        matches = matches.Where(t => t.DeviceItem.Name.Equals(deviceItemName, StringComparison.OrdinalIgnoreCase));
      }
      List<UnifiedTarget> list = matches.ToList();
      if (list.Count == 1) {
        target = list[0];
        return null;
      }
      string available = string.Join(", ", all.Select(t => $"{t.Device.Name}/{t.DeviceItem.Name}"));
      return list.Count == 0
        ? $"Error: WinCC Unified device {deviceName}/{deviceItemName} not found. Available: {available}."
        : $"Error: Several WinCC Unified devices found; pass deviceName and deviceItemName. Available: {available}.";
    }

    static void Collect(Device device, DeviceItem item, List<UnifiedTarget> targets) {
      SoftwareContainer container = item.GetService<SoftwareContainer>();
      if (container?.Software is HmiSoftware hmi) {
        targets.Add(new UnifiedTarget { Device = device, DeviceItem = item, Software = hmi });
        return;
      }
      foreach (DeviceItem child in item.DeviceItems) {
        Collect(device, child, targets);
      }
    }

    static IEnumerable<Device> AllDevices(ProjectBase project) {
      foreach (Device device in project.Devices) {
        yield return device;
      }
      var groups = new Stack<DeviceUserGroup>(project.DeviceGroups);
      while (groups.Count > 0) {
        DeviceUserGroup group = groups.Pop();
        foreach (Device device in group.Devices) {
          yield return device;
        }
        foreach (DeviceUserGroup child in group.Groups) {
          groups.Push(child);
        }
      }
    }

    #endregion
  }
}
