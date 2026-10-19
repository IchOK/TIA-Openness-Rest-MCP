using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaMCP.HMI;
using Tophinke.TiaOpenness.Tool.Types.TopCtrl.HMI.Message;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.TopCtrl.HMI;

[McpServerToolType]
public class HmiMessageTools {
  [McpServerTool(Name = "tia_topctrl_hmi_messages_sync"), Description(
    "Creates or updates the discrete alarms (Bitmeldungen) of a WinCC Unified device from the TopCtrl PLC message list " +
    "(same data as tia_topctrl_plc_messages_list, only messages of Unified drivers tbMsg_Drv16Unified/tbMsg_Drv16xUnified). " +
    "The PLC is the partner of the HMI connection (connectionName, or the only connection with a partner). " +
    "For every driver variable (tbMsg_Drv16Unified_T, e.g. \"pvHmiMsgArea_HMI_Uni\".PN1_Diag[0]) three Word tags are created with symbolic access: " +
    "<path>_stHmi_wTrigger, <path>_stHmi_wState and <path>_stHmi_wQuitt (path with '.', '[' and ']' replaced by '_', e.g. pvHmiMsgArea_HMI_Uni_PN1_Diag_0_stHmi_wTrigger). " +
    "New tags go to tagTableName (default \"tbMsg_Unified\"); existing tags stay in their table. " +
    "For every message a discrete alarm <member>_<bit+1> is created (member = path inside the DB, e.g. PN1_Diag_0_1, DI_001_2; " +
    "the full path is used if two DBs have the same member) with RaisedStateTag = wTrigger, AcknowledgmentControlTag = wQuitt, " +
    "AcknowledgmentStateTag = wState, all with bit number = HmiBit, TriggerMode OnRisingEdge, EventText per language from the comment of the message variable. " +
    "AlarmClass is mapped from the message type stConfig.iType: 1/2/3 = \"Alarm Unit/Phase/Modul\", 17/18/19 = \"Warnung Unit/Phase/Modul\", " +
    "33/34/35 = \"Information Unit/Phase/Modul\", 49 = \"Betriebsmodus\", 50 = \"Betriebszustand\", 52 = \"Test Done\", 53 = \"Test Failed\", 57 = \"Bedienanforderung\"; " +
    "alarmClassMap adds or overrides entries (key = type number or constant name such as \"MSG_TYPE_OPER_MAINT\"). " +
    "Unmapped types use defaultAlarmClass, otherwise the alarm class stays unchanged (warning). Classes missing on the device are reported and not set. " +
    "Existing objects are only changed where they differ. Discrete alarms on the generated trigger tags without a PLC message are reported, " +
    "or deleted with deleteObsolete. Use dryRun first to review the plan. Scanning the PLC may take a minute. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "Data is a JSON object with DeviceName, DeviceItemName, HmiName, Connection, PlcDeviceName, PlcDeviceItemName, TagTableName, DryRun, " +
    "MessageCount, TagCount, AlarmCount, UnchangedCount, Tags and Alarms (planned objects, only with dryRun), " +
    "Changes (created/updated/deleted objects; with dryRun Action \"Create\", \"Update\" or \"Delete\"), Warnings. " +
    "If some entries failed, IsSuccess is false and Data still lists all changes. Changes are not saved; use tia_projects_save.")]
  public static async Task<CallToolResult> SyncMessages(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional Unified device name (see tia_hmi_devices_list); may be omitted if there is only one")] string? deviceName = null,
    [Description("Optional WinCC Unified device item name (DeviceItemName from tia_hmi_devices_list)")] string? deviceItemName = null,
    [Description("Optional HMI connection to the PLC; default: the only connection with a partner PLC")] string? connectionName = null,
    [Description("Optional PLC device name; default: station of the connection")] string? plcDeviceName = null,
    [Description("Optional PLC device item name; default: partner of the connection")] string? plcDeviceItemName = null,
    [Description("Optional tag table for new tags (default \"tbMsg_Unified\")")] string? tagTableName = null,
    [Description("Optional filter on the driver variable with * and ?, e.g. \"*PN1_Diag*\"")] string? hmiVariablePattern = null,
    [Description("Optional message type to alarm class map, e.g. {\"58\": \"Information Modul\"}")] Dictionary<string, string>? alarmClassMap = null,
    [Description("Optional alarm class for message types without mapping")] string? defaultAlarmClass = null,
    [Description("Optional: also messages with stConfig.xSelect = FALSE")] bool includeUnselected = false,
    [Description("Optional: delete discrete alarms on the generated trigger tags that have no PLC message")] bool deleteObsolete = false,
    [Description("Optional: only plan, change nothing")] bool dryRun = false) {
    var activity = ToolActivity.Start<HmiMessageTools>(dryRun ? "Meldungsabgleich wird geplant" : "Unified-Meldungen werden abgeglichen");
    var body = new SyncRequest {
      ConnectionName = connectionName,
      PlcDeviceName = plcDeviceName,
      PlcDeviceItemName = plcDeviceItemName,
      TagTableName = tagTableName,
      HmiVariablePattern = hmiVariablePattern,
      AlarmClassMap = alarmClassMap,
      DefaultAlarmClass = defaultAlarmClass,
      IncludeUnselected = includeUnselected,
      DeleteObsolete = deleteObsolete,
      DryRun = dryRun
    };
    return activity.Complete(await HmiTools.SendAsync(HttpMethod.Post,
      HmiTools.BuildRoute(MessageRoutes.HmiSync, processId, projectName, deviceName, deviceItemName), body));
  }
}
