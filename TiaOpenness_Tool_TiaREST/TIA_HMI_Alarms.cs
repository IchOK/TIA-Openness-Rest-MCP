using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.HMI;
using static Tophinke.TiaOpenness.Tool.TiaREST.HMI.cTiaHmi;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.HMI {
  /// <summary>
  /// Bit- und Analogmeldungen sowie Meldeklassen von WinCC Unified-Geräten.
  /// </summary>
  static internal class cTiaHmiAlarms {
    internal const string Discrete = "Discrete";
    internal const string Analog = "Analog";

    /// <summary>
    /// Listet die Bit- und Analogmeldungen eines Unified-Geräts.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional, wenn nur eines vorhanden ist</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <query name="kind">"Discrete" oder "Analog" (optional, Standard: beide)</query>
    /// <query name="namePattern">Namensfilter mit * und ? (optional)</query>
    /// <returns>JSON-String mit ListResult von AlarmInfo oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.AlarmsList)]
    static public string List(HttpListenerContext context) {
      string kind = context.Request.QueryString["kind"];
      Func<string, bool> filter = NameFilter(context.Request.QueryString["namePattern"]);
      if (!string.IsNullOrEmpty(kind) && !IsKind(kind, Discrete) && !IsKind(kind, Analog)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: kind must be 'Discrete' or 'Analog'.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        Language language = project.LanguageSettings.EditingLanguage;
        var items = new List<AlarmInfo>();
        if (string.IsNullOrEmpty(kind) || IsKind(kind, Discrete)) {
          items.AddRange(target.Software.DiscreteAlarms.Where(a => filter(a.Name)).Select(a => ToInfo(a, language)));
        }
        if (string.IsNullOrEmpty(kind) || IsKind(kind, Analog)) {
          items.AddRange(target.Software.AnalogAlarms.Where(a => filter(a.Name)).Select(a => ToInfo(a, language)));
        }
        return ListResponse(context, target, items);
      });
    }

    /// <summary>
    /// Legt Bit- oder Analogmeldungen an oder ändert sie (Felder mit null bleiben unverändert).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>AlarmPutRequest</body>
    /// <returns>JSON-String mit ChangeResult oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.AlarmsPut, "PUT")]
    static public string Put(HttpListenerContext context) {
      AlarmPutRequest request = ReadRequest<AlarmPutRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.Alarms == null || request.Alarms.Count == 0 || request.Alarms.Any(a => string.IsNullOrWhiteSpace(a?.Name))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Alarms must contain at least one alarm and every alarm needs a Name.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        Language language = project.LanguageSettings.EditingLanguage;
        var changes = request.Alarms.Select(info => Upsert(project, target.Software, info, language)).ToList();
        return ChangeResponse(context, target, changes);
      });
    }

    /// <summary>
    /// Löscht Bit- oder Analogmeldungen über ihren Namen.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>DeleteRequest</body>
    /// <returns>JSON-String mit ChangeResult oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.AlarmsDelete, "POST")]
    static public string Delete(HttpListenerContext context) {
      DeleteRequest request = ReadRequest<DeleteRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.Names == null || request.Names.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Names must contain at least one name.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        HmiSoftware hmi = target.Software;
        var changes = request.Names.Select(name => hmi.AnalogAlarms.Find(name) != null
          ? DeleteObject("AnalogAlarm", name, () => hmi.AnalogAlarms.Find(name), a => a.Delete())
          : DeleteObject("DiscreteAlarm", name, () => hmi.DiscreteAlarms.Find(name), a => a.Delete())).ToList();
        return ChangeResponse(context, target, changes);
      });
    }

    /// <summary>
    /// Listet die Meldeklassen eines Unified-Geräts.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <returns>JSON-String mit ListResult von AlarmClassInfo oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.AlarmClassesList)]
    static public string ClassesList(HttpListenerContext context) {
      return ExecuteOnHmi(context, (project, target) => {
        var items = target.Software.AlarmClasses.Select(ToInfo).ToList();
        return ListResponse(context, target, items);
      });
    }

    /// <summary>
    /// Legt Meldeklassen an oder ändert sie (Felder mit null bleiben unverändert).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>AlarmClassPutRequest</body>
    /// <returns>JSON-String mit ChangeResult oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.AlarmClassesPut, "PUT")]
    static public string ClassesPut(HttpListenerContext context) {
      AlarmClassPutRequest request = ReadRequest<AlarmClassPutRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.AlarmClasses == null || request.AlarmClasses.Count == 0 || request.AlarmClasses.Any(c => string.IsNullOrWhiteSpace(c?.Name))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: AlarmClasses must contain at least one alarm class and every alarm class needs a Name.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        var changes = request.AlarmClasses.Select(info => Upsert(target.Software, info)).ToList();
        return ChangeResponse(context, target, changes);
      });
    }

    /// <summary>
    /// Löscht Meldeklassen über ihren Namen (Systemmeldeklassen können nicht gelöscht werden).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>DeleteRequest</body>
    /// <returns>JSON-String mit ChangeResult oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.AlarmClassesDelete, "POST")]
    static public string ClassesDelete(HttpListenerContext context) {
      DeleteRequest request = ReadRequest<DeleteRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.Names == null || request.Names.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Names must contain at least one name.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        var changes = request.Names.Select(name => DeleteObject("AlarmClass", name, () => target.Software.AlarmClasses.Find(name), c => {
          if (c.IsSystem) {
            throw new InvalidOperationException($"System alarm class {name} cannot be deleted.");
          }
          c.Delete();
        })).ToList();
        return ChangeResponse(context, target, changes);
      });
    }

    #region Lesen

    static AlarmInfo ToInfo(HmiDiscreteAlarm alarm, Language language) {
      AlarmInfo info = ToInfo((AlarmBase)alarm, Discrete, language);
      info.RaisedStateTagBitNumber = Safe(() => (uint?)alarm.RaisedStateTagBitNumber);
      info.TriggerMode = Safe(() => alarm.TriggerMode.ToString());
      info.AcknowledgmentControlTag = Reference(Safe(() => alarm.AcknowledgmentControlTag));
      if (info.AcknowledgmentControlTag != null) {
        info.AcknowledgmentControlTagBitNumber = Safe(() => (int?)alarm.AcknowledgmentControlTagBitNumber);
      }
      info.AcknowledgmentStateTag = Reference(Safe(() => alarm.AcknowledgmentStateTag));
      if (info.AcknowledgmentStateTag != null) {
        info.AcknowledgmentStateTagBitNumber = Safe(() => (int?)alarm.AcknowledgmentStateTagBitNumber);
      }
      return info;
    }

    static AlarmInfo ToInfo(HmiAnalogAlarm alarm, Language language) {
      AlarmInfo info = ToInfo((AlarmBase)alarm, Analog, language);
      info.Condition = Safe(() => alarm.Condition.ToString());
      info.ConditionValue = Safe(() => ValueText(alarm.ConditionValue));
      return info;
    }

    static AlarmInfo ToInfo(AlarmBase alarm, string kind, Language language) {
      Dictionary<string, string> texts = Texts(Safe(() => alarm.EventText));
      return new AlarmInfo {
        Kind = kind,
        Name = alarm.Name,
        Id = Safe(() => (uint?)alarm.Id),
        AlarmClass = Safe(() => alarm.AlarmClass),
        Priority = Safe(() => (byte?)alarm.Priority),
        Area = NullIfEmpty(Safe(() => alarm.Area)),
        Origin = NullIfEmpty(Safe(() => alarm.Origin)),
        RaisedStateTag = Reference(Safe(() => alarm.RaisedStateTag)),
        EventText = NullIfEmpty(GetText(Safe(() => alarm.EventText), language)),
        EventTexts = texts.Count > 0 ? texts : null,
        InfoText = NullIfEmpty(GetText(Safe(() => alarm.InfoText), language))
      };
    }

    static AlarmClassInfo ToInfo(HmiAlarmClass alarmClass) {
      return new AlarmClassInfo {
        Name = alarmClass.Name,
        Id = Safe(() => (uint?)alarmClass.Id),
        Priority = Safe(() => (byte?)alarmClass.Priority),
        StateMachine = Safe(() => alarmClass.StateMachine.ToString()),
        Log = NullIfEmpty(Safe(() => alarmClass.Log)),
        IsSystem = Safe(() => (bool?)alarmClass.IsSystem),
        CommonAlarmClass = NullIfEmpty(Safe(() => alarmClass.CommonAlarmClass))
      };
    }

    #endregion

    #region Schreiben

    static internal Change Upsert(ProjectBase project, HmiSoftware hmi, AlarmInfo info, Language language) {
      HmiDiscreteAlarm discrete = hmi.DiscreteAlarms.Find(info.Name);
      HmiAnalogAlarm analog = discrete == null ? hmi.AnalogAlarms.Find(info.Name) : null;
      string kind = discrete != null ? Discrete : analog != null ? Analog : string.IsNullOrEmpty(info.Kind) ? Discrete : info.Kind;
      string changeKind = kind + "Alarm";
      if (!IsKind(kind, Discrete) && !IsKind(kind, Analog)) {
        return new Change { Kind = "Alarm", Name = info.Name, Action = "Created", Error = "Kind must be 'Discrete' or 'Analog'." };
      }
      if (!string.IsNullOrEmpty(info.Kind) && !IsKind(info.Kind, kind)) {
        return new Change { Kind = changeKind, Name = info.Name, Action = "Updated", Error = $"Alarm {info.Name} already exists as {kind} alarm." };
      }
      bool created = discrete == null && analog == null;
      if (created) {
        try {
          if (IsKind(kind, Discrete)) {
            discrete = hmi.DiscreteAlarms.Create(info.Name);
          } else {
            analog = hmi.AnalogAlarms.Create(info.Name);
          }
        } catch (Exception ex) {
          return new Change { Kind = changeKind, Name = info.Name, Action = "Created", Error = ex.Message };
        }
      }
      AlarmBase alarm = (AlarmBase)discrete ?? analog;
      return Apply(changeKind, info.Name, created, tracker => {
        tracker.Set("Id", info.Id?.ToString(), () => alarm.Id.ToString(), v => alarm.Id = uint.Parse(v));
        tracker.Set("AlarmClass", info.AlarmClass, () => alarm.AlarmClass, v => alarm.AlarmClass = v);
        tracker.Set("Priority", info.Priority?.ToString(), () => alarm.Priority.ToString(), v => alarm.Priority = byte.Parse(v));
        tracker.Set("Area", info.Area, () => alarm.Area ?? "", v => alarm.Area = v);
        tracker.Set("Origin", info.Origin, () => alarm.Origin ?? "", v => alarm.Origin = v);
        if (discrete != null) {
          // TIA lehnt eine Bitnummer ab, solange Variable und altes Bit mit einer anderen Meldung übereinstimmen; daher Bits vor den Variablen setzen.
          tracker.Set("RaisedStateTagBitNumber", info.RaisedStateTagBitNumber?.ToString(), () => discrete.RaisedStateTagBitNumber.ToString(),
            v => SetBit(() => discrete.RaisedStateTag, t => discrete.RaisedStateTag = t, () => discrete.RaisedStateTagBitNumber = uint.Parse(v)));
          tracker.Set("AcknowledgmentControlTagBitNumber", info.AcknowledgmentControlTagBitNumber?.ToString(), () => discrete.AcknowledgmentControlTagBitNumber.ToString(),
            v => SetBit(() => discrete.AcknowledgmentControlTag, t => discrete.AcknowledgmentControlTag = t, () => discrete.AcknowledgmentControlTagBitNumber = int.Parse(v)));
          tracker.Set("AcknowledgmentStateTagBitNumber", info.AcknowledgmentStateTagBitNumber?.ToString(), () => discrete.AcknowledgmentStateTagBitNumber.ToString(),
            v => SetBit(() => discrete.AcknowledgmentStateTag, t => discrete.AcknowledgmentStateTag = t, () => discrete.AcknowledgmentStateTagBitNumber = int.Parse(v)));
        }
        tracker.Set("RaisedStateTag", info.RaisedStateTag, () => alarm.RaisedStateTag ?? "", v => alarm.RaisedStateTag = v);
        if (discrete != null) {
          tracker.Set("TriggerMode", info.TriggerMode, () => discrete.TriggerMode.ToString(), v => discrete.TriggerMode = ParseEnum<HmiDiscreteAlarmTriggerMode>(v, "TriggerMode"));
          tracker.Set("AcknowledgmentControlTag", info.AcknowledgmentControlTag, () => discrete.AcknowledgmentControlTag ?? "", v => discrete.AcknowledgmentControlTag = v);
          tracker.Set("AcknowledgmentStateTag", info.AcknowledgmentStateTag, () => discrete.AcknowledgmentStateTag ?? "", v => discrete.AcknowledgmentStateTag = v);
        } else if (info.RaisedStateTagBitNumber != null || info.TriggerMode != null || info.AcknowledgmentControlTag != null || info.AcknowledgmentStateTag != null) {
          tracker.Note("Discrete alarm fields ignored for analog alarm");
        }
        if (analog != null) {
          tracker.Set("Condition", info.Condition, () => analog.Condition.ToString(), v => analog.Condition = ParseEnum<HmiAlarmCondition>(v, "Condition"));
          tracker.Set("ConditionValue", info.ConditionValue, () => ValueText(analog.ConditionValue), v => analog.ConditionValue = ParseValue(v, analog.ConditionValue));
        } else if (info.Condition != null || info.ConditionValue != null) {
          tracker.Note("Analog alarm fields ignored for discrete alarm");
        }
        tracker.Set("EventText", RichText(info.EventText), () => GetText(alarm.EventText, language) ?? "", v => SetText(alarm.EventText, language, v));
        if (info.EventTexts != null) {
          foreach (var entry in info.EventTexts) {
            tracker.Set($"EventText[{entry.Key}]", RichText(entry.Value),
              () => Texts(alarm.EventText).TryGetValue(entry.Key, out string text) ? text : "",
              v => SetTexts(project, alarm.EventText, new Dictionary<string, string> { [entry.Key] = v }));
          }
        }
        tracker.Set("InfoText", info.InfoText, () => GetText(alarm.InfoText, language) ?? "", v => SetText(alarm.InfoText, language, v));
      });
    }

    static Change Upsert(HmiSoftware hmi, AlarmClassInfo info) {
      HmiAlarmClass alarmClass = hmi.AlarmClasses.Find(info.Name);
      bool created = alarmClass == null;
      if (created) {
        try {
          alarmClass = hmi.AlarmClasses.Create(info.Name);
        } catch (Exception ex) {
          return new Change { Kind = "AlarmClass", Name = info.Name, Action = "Created", Error = ex.Message };
        }
      }
      return Apply("AlarmClass", info.Name, created, tracker => {
        if (info.Id != null || info.IsSystem != null || info.CommonAlarmClass != null) {
          tracker.Note("Id, IsSystem and CommonAlarmClass are read-only and ignored");
        }
        tracker.Set("Priority", info.Priority?.ToString(), () => alarmClass.Priority.ToString(), v => alarmClass.Priority = byte.Parse(v));
        tracker.Set("StateMachine", info.StateMachine, () => alarmClass.StateMachine.ToString(), v => alarmClass.StateMachine = ParseEnum<HmiAlarmStateMachine>(v, "StateMachine"));
        tracker.Set("Log", info.Log, () => alarmClass.Log ?? "", v => alarmClass.Log = v);
      });
    }

    #endregion

    /// <summary>
    /// Setzt eine Bitnummer. Steht die Meldung bereits im Konflikt (gleiche Variable und gleiches Bit wie eine andere Meldung),
    /// lehnt TIA jede neue Bitnummer ab; dann wird die Variable kurz entfernt und danach wieder gesetzt.
    /// </summary>
    static void SetBit(Func<string> getTag, Action<string> setTag, Action setBit) {
      try {
        setBit();
      } catch {
        string tag = Reference(getTag());
        if (tag == null) {
          throw;
        }
        setTag("");
        try {
          setBit();
        } finally {
          setTag(tag);
        }
      }
    }

    /// <summary>
    /// Meldetexte sind in Unified Rich Text; einfacher Text wird als &lt;body&gt;&lt;p&gt;...&lt;/p&gt;&lt;/body&gt; verpackt.
    /// </summary>
    static internal string RichText(string text) {
      if (text == null || text.TrimStart().StartsWith("<body", StringComparison.OrdinalIgnoreCase)) {
        return text;
      }
      return "<body><p>" + text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") + "</p></body>";
    }

    static bool IsKind(string value, string kind) {
      return string.Equals(value, kind, StringComparison.OrdinalIgnoreCase);
    }
  }
}
