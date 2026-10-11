using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.HMI;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.HMI;

[McpServerToolType]
public class HmiTools {
  const string TargetDescription =
    "Works on one WinCC Unified device (panel or PC runtime); deviceName/deviceItemName select it as returned by tia_hmi_devices_list " +
    "and may be omitted if the project has only one Unified device. ";

  const string ChangeDescription =
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "Data is a JSON object with DeviceName, DeviceItemName, HmiName and Changes (array of { Kind, Name, Action (\"Created\", \"Updated\", \"Deleted\" or \"Unchanged\"), " +
    "Details (changed fields as \"Field: old -> new\"), Error (null on success) }). " +
    "If some entries failed, IsSuccess is false and Data still lists all entries; the other entries are applied. " +
    "Changes are made in the open project and are not saved; use tia_projects_save to save. ";

  const string UpsertDescription =
    "Existing objects (found by Name) are updated, missing ones are created. Fields that are null or omitted stay unchanged; " +
    "only fields that differ are written. ";

  [McpServerTool(Name = "tia_hmi_devices_list"), Description(
    "Lists all WinCC Unified devices (panels and PC runtimes) of a TIA project, including devices in device folders. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array with one object per device: DeviceName and DeviceItemName (use for the other tia_hmi_* tools), " +
    "HmiName (HMI software name), TypeIdentifier (order number/version), " +
    "Connections (array of { Name, CommunicationDriver, Partner (PLC name), Station (PLC device name), Node, Comment }), " +
    "TagTableCount, TagCount, DiscreteAlarmCount, AnalogAlarmCount, AlarmClassCount, DataLogCount, AlarmLogCount, ScreenCount.")]
  public static async Task<CallToolResult> ListDevices(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName) {
    var activity = ToolActivity.Start<HmiTools>("Unified-Geräte werden abgerufen");
    try {
      string route = $"{HmiRoutes.DevicesList}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}";
      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  #region Variablen

  [McpServerTool(Name = "tia_hmi_tags_list"), Description(
    "Lists the HMI tags (Variablen) of a WinCC Unified device. " + TargetDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with DeviceName, DeviceItemName, HmiName and Items (array of tags): " +
    "Name, TagTableName, Connection (null = internal tag), PlcTag (symbolic PLC tag, e.g. \"DB.Struct.Member\"), Address (absolute access), " +
    "AccessMode, DataType, HmiDataType, AcquisitionMode, AcquisitionCycle, InitialValue, Persistent, LinearScaling, " +
    "PlcStartValue/PlcEndValue/HmiStartValue/HmiEndValue (only with LinearScaling), MinValue/MaxValue (constant limits), " +
    "Comment (editing language), LoggingTags (logging tags with Name, DataLog, LoggingMode, Cycle, CycleFactor, AggregationMode, SmoothingMode, " +
    "SmoothingDeltaValue, LimitScope, LowLimit, HighLimit, TriggerMode, TriggerTag, TriggerTagBitNumber). " +
    "Items have the same shape as the tags expected by tia_hmi_tags_put.")]
  public static async Task<CallToolResult> ListTags(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null,
    [Description("Optional: only tags of this tag table")] string? tagTableName = null,
    [Description("Optional name filter with * and ?, e.g. \"Motor*\"")] string? namePattern = null) {
    var activity = ToolActivity.Start<HmiTools>("HMI-Variablen werden abgerufen");
    try {
      string route = BuildRoute(HmiRoutes.TagsList, processId, projectName, deviceName, deviceItemName)
        + Query("tagTableName", tagTableName) + Query("namePattern", namePattern);
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_hmi_tags_put"), Description(
    "Creates or updates HMI tags (Variablen) of a WinCC Unified device, including their logging tags (Archivvariablen). " + TargetDescription +
    UpsertDescription +
    "Tag fields: Name (required), TagTableName (only for new tags: target table, created if missing; default table if omitted; existing tags are not moved), " +
    "Connection (HMI connection name, e.g. \"Conn_PLC_LIB\"), AccessMode (\"SymbolicAccess\" or \"AbsoluteAccess\"), " +
    "PlcTag (symbolic PLC tag without quotes, e.g. \"DB_Name.Struct.Member\" or \"DB_Name.Array[0].Member\"; the data type is taken from the PLC), " +
    "Address (absolute access), DataType (for internal tags, e.g. \"Int\", \"Real\", \"Bool\", \"WString\"), HmiDataType, " +
    "AcquisitionMode (\"OnDemand\", \"CyclicOnUse\", \"CyclicContinuous\"), AcquisitionCycle (e.g. \"T1s\", \"T100ms\"), " +
    "InitialValue, Persistent, LinearScaling, PlcStartValue, PlcEndValue, HmiStartValue, HmiEndValue, MinValue, MaxValue (\"\" removes the limit), " +
    "Comment (editing language). Values are passed as text in invariant culture (decimal point). " +
    "LoggingTags: logging tags to create or update (never deleted), each with Name (required), DataLog (data log name, see tia_hmi_logs_list), " +
    "LoggingMode (\"Cyclic\", \"OnChange\", \"OnDemand\"), Cycle (e.g. \"T10s\"), CycleFactor, AggregationMode, SmoothingMode, SmoothingDeltaValue, " +
    "LimitScope, LowLimit, HighLimit, TriggerMode, TriggerTag, TriggerTagBitNumber. " +
    "Changes use Kind \"Tag\" and \"LoggingTag\" (Name \"<tag>:<logging tag>\"). " + ChangeDescription)]
  public static async Task<CallToolResult> PutTags(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Tags to create or update; omitted fields stay unchanged")] TagInfo[] tags,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("HMI-Variablen werden geschrieben");
    return activity.Complete(await SendAsync(HttpMethod.Put, BuildRoute(HmiRoutes.TagsPut, processId, projectName, deviceName, deviceItemName),
      new TagPutRequest { Tags = tags?.ToList() }));
  }

  [McpServerTool(Name = "tia_hmi_tags_delete"), Description(
    "Deletes HMI tags (Variablen) of a WinCC Unified device by name, including their logging tags. " + TargetDescription +
    "Changes use Kind \"Tag\" and Action \"Deleted\"; unknown names are reported as Error. " + ChangeDescription)]
  public static async Task<CallToolResult> DeleteTags(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Names of the tags to delete")] string[] names,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("HMI-Variablen werden gelöscht");
    return activity.Complete(await SendAsync(HttpMethod.Post, BuildRoute(HmiRoutes.TagsDelete, processId, projectName, deviceName, deviceItemName),
      new DeleteRequest { Names = names?.ToList() }));
  }

  [McpServerTool(Name = "tia_hmi_tags_get_file"), Description(
    "Exports the HMI tags of a WinCC Unified device as Openness files, one folder per tag table: " +
    "<rootDirectory>\\<project name>\\<HmiName>\\Tags\\<tag table>\\. Existing table folders are replaced. " + TargetDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with DeviceName, DeviceItemName, HmiName, Directory (the Tags folder) and Files (paths relative to Directory).")]
  public static async Task<CallToolResult> GetTagsFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Absolute root directory for the export, e.g. \"C:\\\\TiaFiles\"")] string rootDirectory,
    [Description("Optional: export only this tag table")] string? tagTableName = null,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("HMI-Variablen werden exportiert");
    try {
      string route = BuildRoute(HmiRoutes.TagsGetFile, processId, projectName, deviceName, deviceItemName)
        + Query("rootDirectory", rootDirectory) + Query("tagTableName", tagTableName);
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_hmi_tags_put_file"), Description(
    "Imports HMI tags of a WinCC Unified device from Openness files as written by tia_hmi_tags_get_file: " +
    "every subfolder of <rootDirectory>\\<project name>\\<HmiName>\\Tags is imported into the tag table of the same name (created if missing). " +
    "Existing tags are overwritten. " + TargetDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with DeviceName, DeviceItemName, HmiName, Directory and Files.")]
  public static async Task<CallToolResult> PutTagsFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Absolute root directory of the export, e.g. \"C:\\\\TiaFiles\"")] string rootDirectory,
    [Description("Optional: import only the folder of this tag table")] string? tagTableName = null,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("HMI-Variablen werden importiert");
    return activity.Complete(await SendAsync(HttpMethod.Put,
      BuildRoute(HmiRoutes.TagsPutFile, processId, projectName, deviceName, deviceItemName) + Query("tagTableName", tagTableName),
      new TagPutFileRequest { RootDirectory = rootDirectory }));
  }

  #endregion

  #region Meldungen

  [McpServerTool(Name = "tia_hmi_alarms_list"), Description(
    "Lists the discrete alarms (Bitmeldungen) and analog alarms (Analogmeldungen) of a WinCC Unified device. " + TargetDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with DeviceName, DeviceItemName, HmiName and Items (array of alarms): " +
    "Kind (\"Discrete\" or \"Analog\"), Name, Id, AlarmClass, Priority, Area, Origin, RaisedStateTag (trigger tag), " +
    "discrete only: RaisedStateTagBitNumber, TriggerMode, AcknowledgmentControlTag/BitNumber, AcknowledgmentStateTag/BitNumber; " +
    "analog only: Condition, ConditionValue; " +
    "EventText (editing language, Unified rich text such as \"<body><p>Text</p></body>\"), EventTexts (per language, e.g. \"de-DE\"), InfoText. " +
    "Items have the same shape as the alarms expected by tia_hmi_alarms_put.")]
  public static async Task<CallToolResult> ListAlarms(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null,
    [Description("Optional: \"Discrete\" or \"Analog\"; default both")] string? kind = null,
    [Description("Optional name filter with * and ?")] string? namePattern = null) {
    var activity = ToolActivity.Start<HmiTools>("HMI-Meldungen werden abgerufen");
    try {
      string route = BuildRoute(HmiRoutes.AlarmsList, processId, projectName, deviceName, deviceItemName)
        + Query("kind", kind) + Query("namePattern", namePattern);
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_hmi_alarms_put"), Description(
    "Creates or updates discrete alarms (Bitmeldungen) and analog alarms (Analogmeldungen) of a WinCC Unified device. " + TargetDescription +
    UpsertDescription +
    "Alarm fields: Kind (\"Discrete\" default or \"Analog\"; only used to create, an existing alarm keeps its kind), Name (required), Id, " +
    "AlarmClass (see tia_hmi_alarmclasses_list), Priority (0..16), Area, Origin, RaisedStateTag (HMI trigger tag), " +
    "discrete: RaisedStateTagBitNumber (bit in the trigger tag, 0 = least significant bit), TriggerMode (\"OnRisingEdge\" or \"OnFallingEdge\"), " +
    "AcknowledgmentControlTag and AcknowledgmentControlTagBitNumber (acknowledge from PLC), AcknowledgmentStateTag and AcknowledgmentStateTagBitNumber (acknowledged state to PLC); " +
    "analog: Condition (\"UpperLimit\", \"LowerLimit\", \"Equal\", \"NotEqual\", \"UpperLimitOrEqual\", \"LowerLimitOrEqual\"), ConditionValue; " +
    "EventText (editing language) and EventTexts (per language, e.g. {\"de-DE\": \"...\"}): plain text is wrapped as Unified rich text <body><p>...</p></body>; " +
    "InfoText (editing language). " +
    "Changes use Kind \"DiscreteAlarm\" or \"AnalogAlarm\". " + ChangeDescription)]
  public static async Task<CallToolResult> PutAlarms(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Alarms to create or update; omitted fields stay unchanged")] AlarmInfo[] alarms,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("HMI-Meldungen werden geschrieben");
    return activity.Complete(await SendAsync(HttpMethod.Put, BuildRoute(HmiRoutes.AlarmsPut, processId, projectName, deviceName, deviceItemName),
      new AlarmPutRequest { Alarms = alarms?.ToList() }));
  }

  [McpServerTool(Name = "tia_hmi_alarms_delete"), Description(
    "Deletes discrete or analog alarms of a WinCC Unified device by name. " + TargetDescription +
    "Unknown names are reported as Error. " + ChangeDescription)]
  public static async Task<CallToolResult> DeleteAlarms(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Names of the alarms to delete")] string[] names,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("HMI-Meldungen werden gelöscht");
    return activity.Complete(await SendAsync(HttpMethod.Post, BuildRoute(HmiRoutes.AlarmsDelete, processId, projectName, deviceName, deviceItemName),
      new DeleteRequest { Names = names?.ToList() }));
  }

  [McpServerTool(Name = "tia_hmi_alarmclasses_list"), Description(
    "Lists the alarm classes (Meldeklassen) of a WinCC Unified device, including system classes. " + TargetDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with DeviceName, DeviceItemName, HmiName and Items (array of { Name, Id, Priority, " +
    "StateMachine (\"Raise\", \"RaiseClear\", \"RaiseRequiresAcknowledgement\", \"RaiseClearOptionalAcknowledgement\", " +
    "\"RaiseClearRequiresAcknowledgement\", \"RaiseClearRequiresAcknowledgementAndReset\"), Log (alarm log), IsSystem, CommonAlarmClass }).")]
  public static async Task<CallToolResult> ListAlarmClasses(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("Meldeklassen werden abgerufen");
    try {
      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(BuildRoute(HmiRoutes.AlarmClassesList, processId, projectName, deviceName, deviceItemName));
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_hmi_alarmclasses_put"), Description(
    "Creates or updates alarm classes (Meldeklassen) of a WinCC Unified device. " + TargetDescription + UpsertDescription +
    "Fields: Name (required), Priority (0..16), StateMachine (see tia_hmi_alarmclasses_list), Log (alarm log name, see tia_hmi_logs_list). " +
    "Id, IsSystem and CommonAlarmClass are read-only. Changes use Kind \"AlarmClass\". " + ChangeDescription)]
  public static async Task<CallToolResult> PutAlarmClasses(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Alarm classes to create or update; omitted fields stay unchanged")] AlarmClassInfo[] alarmClasses,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("Meldeklassen werden geschrieben");
    return activity.Complete(await SendAsync(HttpMethod.Put, BuildRoute(HmiRoutes.AlarmClassesPut, processId, projectName, deviceName, deviceItemName),
      new AlarmClassPutRequest { AlarmClasses = alarmClasses?.ToList() }));
  }

  [McpServerTool(Name = "tia_hmi_alarmclasses_delete"), Description(
    "Deletes user-defined alarm classes (Meldeklassen) of a WinCC Unified device by name; system classes cannot be deleted. " + TargetDescription +
    ChangeDescription)]
  public static async Task<CallToolResult> DeleteAlarmClasses(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Names of the alarm classes to delete")] string[] names,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("Meldeklassen werden gelöscht");
    return activity.Complete(await SendAsync(HttpMethod.Post, BuildRoute(HmiRoutes.AlarmClassesDelete, processId, projectName, deviceName, deviceItemName),
      new DeleteRequest { Names = names?.ToList() }));
  }

  #endregion

  #region Archive

  [McpServerTool(Name = "tia_hmi_logs_list"), Description(
    "Lists the data logs (Datenarchive) and alarm logs (Meldearchive) of a WinCC Unified device. " + TargetDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with DeviceName, DeviceItemName, HmiName and Items (array of { Kind (\"Data\" or \"Alarm\"), Name, " +
    "StorageDevice (e.g. \"Default\", \"Local\", \"SDX51\", \"USBX61\"), StorageFolder, LogMaxSize (MB), LogTimePeriod (\"d.hh:mm:ss\"), " +
    "SegmentMaxSize (MB), SegmentTimePeriod (\"d.hh:mm:ss\"), BackupMode (\"NoBackup\" or \"PrimaryPath\"), BackupPath }).")]
  public static async Task<CallToolResult> ListLogs(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("Archive werden abgerufen");
    try {
      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(BuildRoute(HmiRoutes.LogsList, processId, projectName, deviceName, deviceItemName));
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_hmi_logs_put"), Description(
    "Creates or updates data logs (Datenarchive) and alarm logs (Meldearchive) of a WinCC Unified device. " + TargetDescription + UpsertDescription +
    "Fields: Kind (\"Data\" default or \"Alarm\"; only used to create), Name (required), StorageDevice (must be supported by the device, " +
    "e.g. panels only support \"SDX51\"/\"USBX61\"), StorageFolder, LogMaxSize (MB), LogTimePeriod (\"d.hh:mm:ss\", e.g. \"30.00:00:00\"), " +
    "SegmentMaxSize (MB), SegmentTimePeriod (\"d.hh:mm:ss\"), BackupMode, BackupPath. " +
    "Fields are applied in this order and the first failing field stops the entry. Changes use Kind \"DataLog\" or \"AlarmLog\". " + ChangeDescription)]
  public static async Task<CallToolResult> PutLogs(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Logs to create or update; omitted fields stay unchanged")] LogInfo[] logs,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("Archive werden geschrieben");
    return activity.Complete(await SendAsync(HttpMethod.Put, BuildRoute(HmiRoutes.LogsPut, processId, projectName, deviceName, deviceItemName),
      new LogPutRequest { Logs = logs?.ToList() }));
  }

  [McpServerTool(Name = "tia_hmi_logs_delete"), Description(
    "Deletes data logs or alarm logs of a WinCC Unified device by name. " + TargetDescription + ChangeDescription)]
  public static async Task<CallToolResult> DeleteLogs(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Names of the logs to delete")] string[] names,
    [Description("Optional Unified device name")] string? deviceName = null,
    [Description("Optional Unified device item name")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<HmiTools>("Archive werden gelöscht");
    return activity.Complete(await SendAsync(HttpMethod.Post, BuildRoute(HmiRoutes.LogsDelete, processId, projectName, deviceName, deviceItemName),
      new DeleteRequest { Names = names?.ToList() }));
  }

  #endregion

  internal static string BuildRoute(string baseRoute, int processId, string projectName, string? deviceName, string? deviceItemName) {
    return $"{baseRoute}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}"
      + Query("deviceName", deviceName) + Query("deviceItemName", deviceItemName);
  }

  static string Query(string name, string? value) {
    return string.IsNullOrWhiteSpace(value) ? "" : $"&{name}={Uri.EscapeDataString(value)}";
  }

  internal static async Task<CallToolResult> SendAsync<T>(HttpMethod method, string route, T body) {
    try {
      using var request = new HttpRequestMessage(method, route) {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
      };
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.SendAsync(request);
      return await TiaRestClient.FromHttpResponseAsync(response);
    } catch (Exception ex) {
      return TiaRestClient.FromException(ex);
    }
  }
}
