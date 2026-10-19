using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using Siemens.Engineering.HmiUnified.LoggingTags;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.HMI;
using static Tophinke.TiaOpenness.Tool.TiaREST.HMI.cTiaHmi;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;
using LoggingAggregationMode = Siemens.Engineering.HmiUnified.LoggingTags.HmiAggregationMode;

namespace Tophinke.TiaOpenness.Tool.TiaREST.HMI {
  /// <summary>
  /// HMI-Variablen und Archivvariablen von WinCC Unified-Geräten.
  /// </summary>
  static internal class cTiaHmiTags {
    /// <summary>
    /// Listet die HMI-Variablen eines Unified-Geräts.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional, wenn nur eines vorhanden ist</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <query name="tagTableName">Nur Variablen dieser Tabelle (optional)</query>
    /// <query name="namePattern">Namensfilter mit * und ? (optional)</query>
    /// <returns>JSON-String mit ListResult von TagInfo oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.TagsList)]
    static public string List(HttpListenerContext context) {
      string tagTableName = context.Request.QueryString["tagTableName"];
      Func<string, bool> filter = NameFilter(context.Request.QueryString["namePattern"]);
      return ExecuteOnHmi(context, (project, target) => {
        Language language = project.LanguageSettings.EditingLanguage;
        var items = target.Software.Tags
          .Where(t => filter(t.Name))
          .Where(t => string.IsNullOrEmpty(tagTableName) || string.Equals(Safe(() => t.TagTableName), tagTableName, StringComparison.OrdinalIgnoreCase))
          .Select(t => ToInfo(t, language))
          .ToList();
        return ListResponse(context, target, items);
      });
    }

    /// <summary>
    /// Legt HMI-Variablen an oder ändert sie (Felder mit null bleiben unverändert), inkl. Archivvariablen.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>TagPutRequest</body>
    /// <returns>JSON-String mit ChangeResult oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.TagsPut, "PUT")]
    static public string Put(HttpListenerContext context) {
      TagPutRequest request = ReadRequest<TagPutRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.Tags == null || request.Tags.Count == 0 || request.Tags.Any(t => string.IsNullOrWhiteSpace(t?.Name))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Tags must contain at least one tag and every tag needs a Name.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        Language language = project.LanguageSettings.EditingLanguage;
        var changes = new List<Change>();
        foreach (TagInfo info in request.Tags) {
          changes.AddRange(Upsert(target.Software, info, language));
        }
        return ChangeResponse(context, target, changes);
      });
    }

    /// <summary>
    /// Löscht HMI-Variablen über ihren Namen.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>DeleteRequest</body>
    /// <returns>JSON-String mit ChangeResult oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.TagsDelete, "POST")]
    static public string Delete(HttpListenerContext context) {
      DeleteRequest request = ReadRequest<DeleteRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.Names == null || request.Names.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Names must contain at least one name.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        var changes = request.Names.Select(name => DeleteObject("Tag", name, () => target.Software.Tags.Find(name), t => t.Delete())).ToList();
        return ChangeResponse(context, target, changes);
      });
    }

    /// <summary>
    /// Exportiert die HMI-Variablen je Variablentabelle als Dateien nach Root\Projekt\HmiName\Tags\Tabelle.
    /// Openness unterstützt den Export nur je Tabelle, nicht für das ganze Gerät.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="rootDirectory">Absoluter Dateisystem-Pfad auf dem Rechner mit TIA Portal/REST als Export-Basis; Dateien unter &lt;rootDirectory&gt;\&lt;Projektname&gt;\&lt;HmiName&gt;\Tags\&lt;Tabelle&gt;</query>
    /// <query name="tagTableName">Nur diese Tabelle (optional, Standard: alle Tabellen)</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <returns>JSON-String mit TagFileData oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.TagsGetFile)]
    static public string GetFile(HttpListenerContext context) {
      string rootDirectory = context.Request.QueryString["rootDirectory"];
      string tagTableName = context.Request.QueryString["tagTableName"];
      if (string.IsNullOrWhiteSpace(rootDirectory) || !Path.IsPathRooted(rootDirectory)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: rootDirectory must be an absolute path.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        List<HmiTagTable> tables = AllTagTables(target.Software)
          .Where(t => string.IsNullOrEmpty(tagTableName) || string.Equals(t.Name, tagTableName, StringComparison.OrdinalIgnoreCase))
          .ToList();
        if (tables.Count == 0) {
          return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Tag table {tagTableName} not found.");
        }
        var directory = new DirectoryInfo(TagDirectory(rootDirectory, project, target));
        foreach (HmiTagTable table in tables) {
          var tableDirectory = new DirectoryInfo(Path.Combine(directory.FullName, SafeFileName(table.Name)));
          if (tableDirectory.Exists) {
            tableDirectory.Delete(true);
          }
          tableDirectory.Create();
          table.Tags.Export(tableDirectory);
        }
        return FileResponse(context, target, directory);
      });
    }

    /// <summary>
    /// Importiert HMI-Variablen aus Root\Projekt\HmiName\Tags\Tabelle; je Unterordner eine Tabelle (wird bei Bedarf angelegt).
    /// Vorhandene Variablen werden überschrieben.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="tagTableName">Nur diese Tabelle (optional, Standard: alle Unterordner)</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>TagPutFileRequest</body>
    /// <returns>JSON-String mit TagFileData oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.TagsPutFile, "PUT")]
    static public string PutFile(HttpListenerContext context) {
      string tagTableName = context.Request.QueryString["tagTableName"];
      TagPutFileRequest request = ReadRequest<TagPutFileRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (string.IsNullOrWhiteSpace(request.RootDirectory) || !Path.IsPathRooted(request.RootDirectory)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: RootDirectory must be an absolute path.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        var directory = new DirectoryInfo(TagDirectory(request.RootDirectory, project, target));
        List<DirectoryInfo> tableDirectories = directory.Exists
          ? directory.GetDirectories()
              .Where(d => string.IsNullOrEmpty(tagTableName) || string.Equals(d.Name, SafeFileName(tagTableName), StringComparison.OrdinalIgnoreCase))
              .Where(d => d.GetFiles().Length > 0)
              .Select(d => new DirectoryInfo(d.FullName))
              .ToList()
          : new List<DirectoryInfo>();
        if (tableDirectories.Count == 0) {
          return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: No tag table folders with files found in {directory.FullName}.");
        }
        foreach (DirectoryInfo tableDirectory in tableDirectories) {
          HmiTagTable table = FindTagTable(target.Software, tableDirectory.Name) ?? target.Software.TagTables.Create(tableDirectory.Name);
          if (!table.Tags.Import(tableDirectory)) {
            return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Import from {tableDirectory.FullName} failed.");
          }
        }
        return FileResponse(context, target, directory);
      });
    }

    #region Lesen

    static TagInfo ToInfo(HmiTag tag, Language language) {
      var info = new TagInfo {
        Name = tag.Name,
        TagTableName = Safe(() => tag.TagTableName),
        Connection = Reference(Safe(() => tag.Connection)),
        PlcTag = NullIfEmpty(Safe(() => tag.PlcTag)),
        Address = NullIfEmpty(Safe(() => tag.Address)),
        AccessMode = Safe(() => tag.AccessMode.ToString()),
        DataType = Safe(() => tag.DataType),
        HmiDataType = Safe(() => tag.HmiDataType),
        AcquisitionMode = Safe(() => tag.AcquisitionMode.ToString()),
        AcquisitionCycle = NullIfEmpty(Safe(() => tag.AcquisitionCycle)),
        InitialValue = Safe(() => ValueText(tag.InitialValue)),
        Persistent = Safe(() => (bool?)tag.Persistent),
        LinearScaling = Safe(() => (bool?)tag.LinearScaling),
        MinValue = RangeText(Safe(() => tag.InitialMinValue)),
        MaxValue = RangeText(Safe(() => tag.InitialMaxValue)),
        Comment = NullIfEmpty(GetText(Safe(() => tag.Comment), language))
      };
      if (info.LinearScaling == true) {
        info.PlcStartValue = Safe(() => ValueText(tag.PlcStartValue));
        info.PlcEndValue = Safe(() => ValueText(tag.PlcEndValue));
        info.HmiStartValue = Safe(() => ValueText(tag.HmiStartValue));
        info.HmiEndValue = Safe(() => ValueText(tag.HmiEndValue));
      }
      HmiLoggingTagComposition loggingTags = Safe(() => tag.LoggingTags);
      if (loggingTags != null && loggingTags.Count > 0) {
        info.LoggingTags = loggingTags.Select(ToInfo).ToList();
      }
      return info;
    }

    static LoggingTagInfo ToInfo(HmiLoggingTag tag) {
      return new LoggingTagInfo {
        Name = tag.Name,
        DataLog = Safe(() => tag.DataLog),
        LoggingMode = Safe(() => tag.LoggingMode.ToString()),
        Cycle = NullIfEmpty(Safe(() => tag.Cycle)),
        CycleFactor = Safe(() => (uint?)tag.CycleFactor),
        AggregationMode = Safe(() => tag.AggregationMode.ToString()),
        SmoothingMode = Safe(() => tag.SmoothingMode.ToString()),
        SmoothingDeltaValue = Safe(() => (double?)tag.SmoothingDeltaValue),
        LimitScope = Safe(() => tag.LimitScope.ToString()),
        LowLimit = Safe(() => ValueText(tag.LowLimit)),
        HighLimit = Safe(() => ValueText(tag.HighLimit)),
        TriggerMode = Safe(() => tag.TriggerMode.ToString()),
        TriggerTag = Reference(Safe(() => tag.TriggerTag)),
        TriggerTagBitNumber = Reference(Safe(() => tag.TriggerTag)) != null ? Safe(() => (uint?)tag.TriggerTagBitNumber) : null
      };
    }

    static string RangeText(Range range) {
      if (range == null || Safe(() => range.ValueType) != HmiLimitValueType.Constant) {
        return null;
      }
      return Safe(() => ValueText(range.Value));
    }
    #endregion

    #region Schreiben

    static internal IEnumerable<Change> Upsert(HmiSoftware hmi, TagInfo info, Language language) {
      HmiTag tag = hmi.Tags.Find(info.Name);
      bool created = tag == null;
      if (created) {
        try {
          tag = CreateTag(hmi, info.Name, info.TagTableName);
        } catch (Exception ex) {
          return new[] { new Change { Kind = "Tag", Name = info.Name, Action = "Created", Error = ex.Message } };
        }
      }
      var changes = new List<Change> {
        Apply("Tag", info.Name, created, tracker => {
          if (!created && info.TagTableName != null && !string.Equals(tag.TagTableName, info.TagTableName, StringComparison.OrdinalIgnoreCase)) {
            tracker.Note($"TagTableName: stays {tag.TagTableName} (moving tags is not supported)");
          }
          tracker.Set("Connection", info.Connection, () => tag.Connection, v => tag.Connection = v);
          tracker.Set("AccessMode", info.AccessMode, () => tag.AccessMode.ToString(), v => tag.AccessMode = ParseEnum<HmiAccessMode>(v, "AccessMode"));
          tracker.Set("DataType", info.DataType, () => tag.DataType, v => tag.DataType = v);
          tracker.Set("PlcTag", info.PlcTag, () => tag.PlcTag, v => tag.PlcTag = v);
          tracker.Set("Address", info.Address, () => tag.Address, v => tag.Address = v);
          tracker.Set("HmiDataType", info.HmiDataType, () => tag.HmiDataType, v => tag.HmiDataType = v);
          tracker.Set("AcquisitionMode", info.AcquisitionMode, () => tag.AcquisitionMode.ToString(), v => tag.AcquisitionMode = ParseEnum<HmiAcquisitionMode>(v, "AcquisitionMode"));
          tracker.Set("AcquisitionCycle", info.AcquisitionCycle, () => tag.AcquisitionCycle, v => tag.AcquisitionCycle = v);
          tracker.Set("InitialValue", info.InitialValue, () => ValueText(tag.InitialValue), v => tag.InitialValue = ParseValue(v, tag.InitialValue));
          tracker.Set("Persistent", info.Persistent?.ToString(), () => tag.Persistent.ToString(), v => tag.Persistent = bool.Parse(v));
          tracker.Set("LinearScaling", info.LinearScaling?.ToString(), () => tag.LinearScaling.ToString(), v => tag.LinearScaling = bool.Parse(v));
          tracker.Set("PlcStartValue", info.PlcStartValue, () => ValueText(tag.PlcStartValue), v => tag.PlcStartValue = ParseValue(v, tag.PlcStartValue));
          tracker.Set("PlcEndValue", info.PlcEndValue, () => ValueText(tag.PlcEndValue), v => tag.PlcEndValue = ParseValue(v, tag.PlcEndValue));
          tracker.Set("HmiStartValue", info.HmiStartValue, () => ValueText(tag.HmiStartValue), v => tag.HmiStartValue = ParseValue(v, tag.HmiStartValue));
          tracker.Set("HmiEndValue", info.HmiEndValue, () => ValueText(tag.HmiEndValue), v => tag.HmiEndValue = ParseValue(v, tag.HmiEndValue));
          tracker.Set("MinValue", info.MinValue, () => RangeText(tag.InitialMinValue), v => SetRange(tag.InitialMinValue, v));
          tracker.Set("MaxValue", info.MaxValue, () => RangeText(tag.InitialMaxValue), v => SetRange(tag.InitialMaxValue, v));
          tracker.Set("Comment", info.Comment, () => GetText(tag.Comment, language) ?? "", v => SetText(tag.Comment, language, v));
        })
      };
      if (info.LoggingTags != null && changes[0].Error == null) {
        foreach (LoggingTagInfo loggingInfo in info.LoggingTags) {
          changes.Add(UpsertLoggingTag(tag, loggingInfo));
        }
      }
      return changes;
    }

    static Change UpsertLoggingTag(HmiTag tag, LoggingTagInfo info) {
      string name = $"{tag.Name}:{info.Name}";
      if (string.IsNullOrWhiteSpace(info.Name)) {
        return new Change { Kind = "LoggingTag", Name = name, Action = "Updated", Error = "LoggingTag needs a Name." };
      }
      HmiLoggingTag loggingTag = tag.LoggingTags.Find(info.Name);
      bool created = loggingTag == null;
      if (created) {
        try {
          loggingTag = tag.LoggingTags.Create(info.Name);
        } catch (Exception ex) {
          return new Change { Kind = "LoggingTag", Name = name, Action = "Created", Error = ex.Message };
        }
      }
      return Apply("LoggingTag", name, created, tracker => {
        tracker.Set("DataLog", info.DataLog, () => loggingTag.DataLog, v => loggingTag.DataLog = v);
        tracker.Set("LoggingMode", info.LoggingMode, () => loggingTag.LoggingMode.ToString(), v => loggingTag.LoggingMode = ParseEnum<HmiLoggingMode>(v, "LoggingMode"));
        tracker.Set("Cycle", info.Cycle, () => loggingTag.Cycle, v => loggingTag.Cycle = v);
        tracker.Set("CycleFactor", info.CycleFactor?.ToString(), () => loggingTag.CycleFactor.ToString(), v => loggingTag.CycleFactor = uint.Parse(v));
        tracker.Set("AggregationMode", info.AggregationMode, () => loggingTag.AggregationMode.ToString(), v => loggingTag.AggregationMode = ParseEnum<LoggingAggregationMode>(v, "AggregationMode"));
        tracker.Set("SmoothingMode", info.SmoothingMode, () => loggingTag.SmoothingMode.ToString(), v => loggingTag.SmoothingMode = ParseEnum<HmiSmoothingMode>(v, "SmoothingMode"));
        tracker.Set("SmoothingDeltaValue", ValueText(info.SmoothingDeltaValue), () => ValueText(loggingTag.SmoothingDeltaValue), v => loggingTag.SmoothingDeltaValue = (double)ParseValue(v, 0.0));
        tracker.Set("LimitScope", info.LimitScope, () => loggingTag.LimitScope.ToString(), v => loggingTag.LimitScope = ParseEnum<HmiLimitScope>(v, "LimitScope"));
        tracker.Set("LowLimit", info.LowLimit, () => ValueText(loggingTag.LowLimit), v => loggingTag.LowLimit = ParseValue(v, loggingTag.LowLimit));
        tracker.Set("HighLimit", info.HighLimit, () => ValueText(loggingTag.HighLimit), v => loggingTag.HighLimit = ParseValue(v, loggingTag.HighLimit));
        tracker.Set("TriggerMode", info.TriggerMode, () => loggingTag.TriggerMode.ToString(), v => loggingTag.TriggerMode = ParseEnum<HmiTriggerMode>(v, "TriggerMode"));
        tracker.Set("TriggerTag", info.TriggerTag, () => loggingTag.TriggerTag, v => loggingTag.TriggerTag = v);
        tracker.Set("TriggerTagBitNumber", info.TriggerTagBitNumber?.ToString(), () => loggingTag.TriggerTagBitNumber.ToString(), v => loggingTag.TriggerTagBitNumber = uint.Parse(v));
      });
    }

    /// <summary>
    /// Legt eine Variable an; die Tabelle wird bei Bedarf auf oberster Ebene angelegt. Ohne Tabelle: Standardtabelle.
    /// </summary>
    static internal HmiTag CreateTag(HmiSoftware hmi, string name, string tagTableName) {
      if (string.IsNullOrWhiteSpace(tagTableName)) {
        return hmi.Tags.Create(name);
      }
      if (FindTagTable(hmi, tagTableName) == null) {
        hmi.TagTables.Create(tagTableName);
      }
      return hmi.Tags.Create(name, tagTableName);
    }

    static internal HmiTagTable FindTagTable(HmiSoftware hmi, string name) {
      return AllTagTables(hmi).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    static IEnumerable<HmiTagTable> AllTagTables(HmiSoftware hmi) {
      foreach (HmiTagTable table in hmi.TagTables) {
        yield return table;
      }
      var groups = new Stack<HmiTagTableGroup>(hmi.TagTableGroups);
      while (groups.Count > 0) {
        HmiTagTableGroup group = groups.Pop();
        foreach (HmiTagTable table in group.TagTables) {
          yield return table;
        }
        foreach (HmiTagTableGroup child in group.Groups) {
          groups.Push(child);
        }
      }
    }

    static void SetRange(Range range, string value) {
      if (value.Length == 0) {
        range.ValueType = HmiLimitValueType.None;
        return;
      }
      object current = range.ValueType == HmiLimitValueType.Constant ? range.Value : null;
      range.ValueType = HmiLimitValueType.Constant;
      range.Value = ParseValue(value, current);
    }

    #endregion

    #region Dateien

    static string TagDirectory(string rootDirectory, ProjectBase project, UnifiedTarget target) {
      return Path.Combine(rootDirectory, project.Name, SafeFileName(target.Software.Name), "Tags");
    }

    static string SafeFileName(string name) {
      foreach (char c in Path.GetInvalidFileNameChars()) {
        name = name.Replace(c, '_');
      }
      return name;
    }

    static string FileResponse(HttpListenerContext context, UnifiedTarget target, DirectoryInfo directory) {
      context.Response.ContentType = "application/json";
      return JsonConvert.SerializeObject(new TagFileData {
        DeviceName = target.Device.Name,
        DeviceItemName = target.DeviceItem.Name,
        HmiName = target.Software.Name,
        Directory = directory.FullName,
        Files = directory.GetFiles("*", SearchOption.AllDirectories)
          .Select(f => f.FullName.Substring(directory.FullName.Length).TrimStart(Path.DirectorySeparatorChar))
          .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
          .ToArray()
      });
    }

    #endregion
  }
}
