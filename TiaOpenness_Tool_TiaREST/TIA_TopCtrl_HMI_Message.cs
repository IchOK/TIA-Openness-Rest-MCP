using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiConnections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaREST.HMI;
using Tophinke.TiaOpenness.Tool.TiaREST.TopCtrl.PLC;
using Tophinke.TiaOpenness.Tool.Types.HMI;
using Tophinke.TiaOpenness.Tool.Types.TopCtrl.HMI.Message;
using Tophinke.TiaOpenness.Tool.Types.TopCtrl.PLC.Message;
using static Tophinke.TiaOpenness.Tool.TiaREST.HMI.cTiaHmi;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.TopCtrl.HMI {
  /// <summary>
  /// Erzeugt die Unified-Bitmeldungen aus der PLC-Meldungsliste der TopCtrl-Meldetreiber.
  /// </summary>
  static internal class cTiaHmiMessages {
    const string DefaultTagTable = "tbMsg_Unified";
    const string UnifiedDriver = "Unified";

    /// <summary>
    /// Standardzuordnung Meldeart (stConfig.iType, Konstanten aus tcMsg) zu Meldeklasse.
    /// </summary>
    static readonly Dictionary<string, string> DefaultAlarmClasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      ["1"] = "Alarm Unit",
      ["2"] = "Alarm Phase",
      ["3"] = "Alarm Modul",
      ["17"] = "Warnung Unit",
      ["18"] = "Warnung Phase",
      ["19"] = "Warnung Modul",
      ["33"] = "Information Unit",
      ["34"] = "Information Phase",
      ["35"] = "Information Modul",
      ["49"] = "Betriebsmodus",
      ["50"] = "Betriebszustand",
      ["52"] = "Test Done",
      ["53"] = "Test Failed",
      ["57"] = "Bedienanforderung"
    };

    /// <summary>
    /// Gleicht die Bitmeldungen eines Unified-Geräts mit der PLC-Meldungsliste ab. Je Treibervariable (tbMsg_Drv16Unified_T) werden die
    /// HMI-Variablen &lt;Pfad&gt;_stHmi_wTrigger, _wState und _wQuitt angelegt, je Meldung eine Bitmeldung &lt;Element&gt;_&lt;Bit+1&gt;
    /// mit Trigger-, Quittier- und Zustandsbit, Meldetext aus dem Kommentar der Meldevariablen und Meldeklasse aus der Meldeart.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional, wenn nur eines vorhanden ist</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>SyncRequest</body>
    /// <returns>JSON-String mit SyncResult oder Fehlermeldung</returns>
    [RestRoute(MessageRoutes.HmiSync, "POST")]
    static public string Sync(HttpListenerContext context) {
      SyncRequest request = ReadRequest<SyncRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      return ExecuteOnHmi(context, (project, target) => {
        HmiSoftware hmi = target.Software;
        string connectionError = ResolveConnection(hmi, request, out HmiConnection connection);
        if (connectionError != null) {
          return ErrorResponse(context, HttpStatusCode.BadRequest, connectionError);
        }
        var result = new SyncResult {
          DeviceName = target.Device.Name,
          DeviceItemName = target.DeviceItem.Name,
          HmiName = hmi.Name,
          Connection = connection.Name,
          PlcDeviceName = string.IsNullOrEmpty(request.PlcDeviceName) ? connection.Station : request.PlcDeviceName,
          PlcDeviceItemName = string.IsNullOrEmpty(request.PlcDeviceItemName) ? connection.Partner : request.PlcDeviceItemName,
          TagTableName = string.IsNullOrWhiteSpace(request.TagTableName) ? DefaultTagTable : request.TagTableName.Trim(),
          DryRun = request.DryRun
        };
        if (string.IsNullOrEmpty(result.PlcDeviceItemName)) {
          return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Connection {connection.Name} has no partner PLC. Specify PlcDeviceName and PlcDeviceItemName.");
        }

        ListResult messages = cTiaMessages.Collect(project, result.PlcDeviceName, result.PlcDeviceItemName, request.IncludeUnselected);
        if (messages == null) {
          return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: No PLC software found for {result.PlcDeviceName}/{result.PlcDeviceItemName}.");
        }
        result.Warnings.AddRange(messages.Warnings);
        Func<string, bool> filter = NameFilter(request.HmiVariablePattern);
        List<Info> unified = messages.Messages.Where(m => m.Driver == UnifiedDriver && filter(m.HmiVariable)).ToList();
        result.MessageCount = unified.Count;

        Plan(hmi, request, result, unified, out List<TagInfo> tags, out List<AlarmInfo> alarms, out List<string> obsolete);
        result.TagCount = tags.Count;
        result.AlarmCount = alarms.Count;

        if (request.DryRun) {
          result.Tags = tags;
          result.Alarms = alarms;
          result.Changes.AddRange(tags.Select(t => Planned("Tag", t.Name, hmi.Tags.Find(t.Name) != null)));
          result.Changes.AddRange(alarms.Select(a => Planned("DiscreteAlarm", a.Name, hmi.DiscreteAlarms.Find(a.Name) != null)));
        } else {
          Language language = project.LanguageSettings.EditingLanguage;
          var changes = new List<Change>();
          foreach (TagInfo tag in tags) {
            changes.AddRange(cTiaHmiTags.Upsert(hmi, tag, language));
          }
          foreach (AlarmInfo alarm in alarms) {
            changes.Add(cTiaHmiAlarms.Upsert(project, hmi, alarm, language));
          }
          result.UnchangedCount = changes.Count(c => c.Action == "Unchanged");
          result.Changes.AddRange(changes.Where(c => c.Action != "Unchanged"));
        }
        if (request.DeleteObsolete) {
          result.Changes.AddRange(obsolete.Select(name => request.DryRun
            ? new Change { Kind = "DiscreteAlarm", Name = name, Action = "Delete" }
            : DeleteObject("DiscreteAlarm", name, () => hmi.DiscreteAlarms.Find(name), a => a.Delete())));
        } else if (obsolete.Count > 0) {
          result.Warnings.Add($"Discrete alarms on the driver tags without PLC message (set DeleteObsolete to remove): {string.Join(", ", obsolete)}");
        }

        if (result.Changes.Any(c => c.Error != null)) {
          context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
      });
    }

    /// <summary>
    /// Plant Variablen und Bitmeldungen; obsolete enthält vorhandene Bitmeldungen an den Triggervariablen ohne PLC-Meldung.
    /// </summary>
    static void Plan(HmiSoftware hmi, SyncRequest request, SyncResult result, List<Info> messages,
                     out List<TagInfo> tags, out List<AlarmInfo> alarms, out List<string> obsolete) {
      tags = new List<TagInfo>();
      alarms = new List<AlarmInfo>();
      var classMap = new Dictionary<string, string>(DefaultAlarmClasses, StringComparer.OrdinalIgnoreCase);
      if (request.AlarmClassMap != null) {
        foreach (var entry in request.AlarmClassMap) {
          classMap[entry.Key.Trim()] = entry.Value;
        }
      }
      var alarmClasses = new HashSet<string>(hmi.AlarmClasses.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
      var missingClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var unmappedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      var groups = messages.GroupBy(m => m.HmiVariable).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
      var memberNames = groups.ToDictionary(g => g.Key, g => MemberName(g.Key));
      var duplicateMembers = new HashSet<string>(memberNames.Values.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

      foreach (var group in groups) {
        string plcPath = group.Key.Replace("\"", "");
        string tagBase = Sanitize(plcPath);
        string alarmBase = duplicateMembers.Contains(memberNames[group.Key]) ? tagBase : memberNames[group.Key];
        string trigger = tagBase + "_stHmi_wTrigger";
        string state = tagBase + "_stHmi_wState";
        string quitt = tagBase + "_stHmi_wQuitt";
        foreach (string member in new[] { "wTrigger", "wState", "wQuitt" }) {
          string name = tagBase + "_stHmi_" + member;
          tags.Add(new TagInfo {
            Name = name,
            TagTableName = hmi.Tags.Find(name) == null ? result.TagTableName : null,
            Connection = result.Connection,
            AccessMode = "SymbolicAccess",
            PlcTag = plcPath + ".stHmi." + member
          });
        }
        foreach (Info message in group.OrderBy(m => m.HmiBit)) {
          string alarmClass = ResolveAlarmClass(message, classMap);
          if (alarmClass == null) {
            unmappedTypes.Add(message.TypeConstant ?? message.Type?.ToString() ?? "(none)");
            alarmClass = request.DefaultAlarmClass;
          }
          if (alarmClass != null && !alarmClasses.Contains(alarmClass)) {
            missingClasses.Add(alarmClass);
            alarmClass = null;
          }
          alarms.Add(new AlarmInfo {
            Kind = cTiaHmiAlarms.Discrete,
            Name = $"{alarmBase}_{message.HmiBit + 1}",
            AlarmClass = alarmClass,
            RaisedStateTag = trigger,
            RaisedStateTagBitNumber = (uint)message.HmiBit,
            TriggerMode = "OnRisingEdge",
            AcknowledgmentControlTag = quitt,
            AcknowledgmentControlTagBitNumber = message.HmiBit,
            AcknowledgmentStateTag = state,
            AcknowledgmentStateTagBitNumber = message.HmiBit,
            EventTexts = RichTexts(message)
          });
        }
      }

      if (unmappedTypes.Count > 0) {
        result.Warnings.Add($"No alarm class for message type(s) {string.Join(", ", unmappedTypes.OrderBy(t => t))}"
          + (request.DefaultAlarmClass != null ? $"; used {request.DefaultAlarmClass}." : "; alarm class left unchanged (set AlarmClassMap or DefaultAlarmClass)."));
      }
      if (missingClasses.Count > 0) {
        result.Warnings.Add($"Alarm class(es) not found on {hmi.Name}, alarm class left unchanged: {string.Join(", ", missingClasses.OrderBy(c => c))}");
      }

      var triggerTags = new HashSet<string>(tags.Where(t => t.Name.EndsWith("_wTrigger", StringComparison.Ordinal)).Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
      var alarmNames = new HashSet<string>(alarms.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
      obsolete = hmi.DiscreteAlarms
        .Where(a => triggerTags.Contains(Safe(() => a.RaisedStateTag) ?? "") && !alarmNames.Contains(a.Name))
        .Select(a => a.Name)
        .ToList();
    }

    static string ResolveAlarmClass(Info message, Dictionary<string, string> classMap) {
      if (message.TypeConstant != null && classMap.TryGetValue(message.TypeConstant, out string byConstant)) {
        return byConstant;
      }
      if (message.Type.HasValue && classMap.TryGetValue(message.Type.Value.ToString(), out string byType)) {
        return byType;
      }
      return null;
    }

    static Dictionary<string, string> RichTexts(Info message) {
      if (message.Texts == null || message.Texts.Count == 0) {
        return null;
      }
      return message.Texts
        .Where(t => !string.IsNullOrEmpty(t.Value))
        .ToDictionary(t => t.Key, t => cTiaHmiAlarms.RichText(t.Value));
    }

    /// <summary>
    /// Pfad innerhalb des Datenbausteins als Name, z. B. "pvHmiMsgArea_HMI_Uni".PN1_Diag[0] -> PN1_Diag_0.
    /// </summary>
    static string MemberName(string hmiVariable) {
      string path = hmiVariable.Replace("\"", "");
      int dot = path.IndexOf('.');
      return Sanitize(dot >= 0 ? path.Substring(dot + 1) : path);
    }

    /// <summary>
    /// PLC-Pfad als HMI-Name: Punkte, Kommas und Klammern werden zu Unterstrichen, z. B. DB.Arr[1,2].x -> DB_Arr_1_2_x.
    /// </summary>
    static string Sanitize(string path) {
      return path.Replace("].", "_").Replace("]", "").Replace("[", "_").Replace(",", "_").Replace(".", "_");
    }

    static Change Planned(string kind, string name, bool exists) {
      return new Change { Kind = kind, Name = name, Action = exists ? "Update" : "Create" };
    }

    static string ResolveConnection(HmiSoftware hmi, SyncRequest request, out HmiConnection connection) {
      List<HmiConnection> connections = hmi.Connections.ToList();
      string available = connections.Count == 0 ? "(none)" : string.Join(", ", connections.Select(c => $"{c.Name} (partner {c.Partner ?? "-"})"));
      if (!string.IsNullOrWhiteSpace(request.ConnectionName)) {
        connection = connections.FirstOrDefault(c => string.Equals(c.Name, request.ConnectionName, StringComparison.OrdinalIgnoreCase));
        return connection == null ? $"Error: Connection {request.ConnectionName} not found on {hmi.Name}. Available: {available}" : null;
      }
      List<HmiConnection> candidates = connections
        .Where(c => !string.IsNullOrEmpty(c.Partner))
        .Where(c => string.IsNullOrEmpty(request.PlcDeviceItemName) || string.Equals(c.Partner, request.PlcDeviceItemName, StringComparison.OrdinalIgnoreCase))
        .ToList();
      connection = candidates.Count == 1 ? candidates[0] : null;
      return connection == null ? $"Error: Specify ConnectionName. Available connections on {hmi.Name}: {available}" : null;
    }
  }
}
