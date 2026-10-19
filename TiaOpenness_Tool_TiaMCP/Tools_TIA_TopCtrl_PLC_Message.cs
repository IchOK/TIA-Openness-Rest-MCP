using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.TopCtrl.PLC;

[McpServerToolType]
public class MessageTools {
  const string TraceDescription =
    "Starting at every HMI driver variable of type tbMsg_Drv16Unified_T, tbMsg_Drv16SCADA_T or tbMsg_Drv16CP_T, each of its 16 bits is traced " +
    "through the driver call (tbMsg_Drv16... / tbMsg_Drv16x... for arrays), the message bus (tbMsg_Bus16_T, tbMsg_Bus32_T or Array of tbMsg_BusElement_T) " +
    "and the message handler call (tbMsg_Handler_...) back to the message variable of type tbMsg_Element_T. " +
    "The message text is the comment of the message variable; the configuration comes from the start values of its stConfig " +
    "(instance DB value, otherwise FB default, otherwise UDT default; if the code copies stConfig from another message variable, that one is used). " +
    "By default only messages with stConfig.xSelect = TRUE are returned. " +
    "Optional deviceName/deviceItemName limit the scan to one PLC; omit them to scan all PLCs in the project. " +
    "The scan exports the involved blocks and may take a while on large projects. ";

  [McpServerTool(Name = "tia_topctrl_plc_messages_list"), Description(
    "Lists the PLC messages (Meldungen) of all TopCtrl HMI message drivers. " +
    TraceDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with Messages and Warnings (links that could not be resolved). " +
    "Each message has: DeviceName, DeviceItemName, PlcName, " +
    "Driver (Unified, SCADA or CP), DriverFunction (called driver FC), DriverBlock (block with the driver call), " +
    "HmiVariable (driver variable, e.g. \"pvHmiMsgArea_HMI_Uni\".PN1_Diag[3]), HmiBit (0..15 within the words of the driver variable), " +
    "BusElement (e.g. \"DI_Msg\".DI_001.aMsg[2]), Element (message variable, e.g. \"DI_001\".SstMsg.stHwFault), " +
    "ElementBlock (DB holding the message variable), ElementFb (FB of the instance DB, if any), " +
    "Handler and HandlerBlock (handler FC and the block calling it), ConfigSource (stConfig the configuration was copied from, if any), " +
    "Text (de-DE or first language), Texts (text per language), " +
    "Select, AutoQuit, Sync, DoReset, IsFault (stConfig flags), Type (stConfig.iType as number) and TypeConstant (constant name used for iType, if any).")]
  public static async Task<CallToolResult> ListMessages(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional device / station name to limit the scan")] string? deviceName = null,
    [Description("Optional CPU / device item name to limit the scan")] string? deviceItemName = null,
    [Description("Optional: true also returns messages with stConfig.xSelect = FALSE")] bool includeUnselected = false) {
    var activity = ToolActivity.Start<MessageTools>("Meldungen werden gesammelt");
    try {
      string route = BuildRoute(MessageRoutes.List, processId, projectName, deviceName, deviceItemName, includeUnselected);

      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_topctrl_plc_messages_get_file"), Description(
    "Exports the PLC messages (Meldungen) of all TopCtrl HMI message drivers to a CSV file, same data as tia_topctrl_plc_messages_list. " +
    TraceDescription +
    "The file is written to <rootDirectory>\\<project name>\\<fileName> (UTF-8 with BOM, CRLF, header row, one row per message); an existing file is replaced. " +
    "Columns: PlcName, Driver, HmiVariable, HmiBit, Element, Text_<language> (one column per project language, e.g. Text_de-DE), " +
    "Type, TypeConstant, Select, AutoQuit, Sync, DoReset, IsFault (TRUE/FALSE), ConfigSource, ElementBlock, ElementFb, BusElement, " +
    "Handler, HandlerBlock, DriverFunction, DriverBlock, DeviceName, DeviceItemName. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: Format (\"CSV\"), Directory, Files (full path of the CSV file), MessageCount, Warnings.")]
  public static async Task<CallToolResult> GetMessagesFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Absolute filesystem path on the TIA Portal/REST machine used as export base, e.g. \"C:\\\\TiaFiles\"; CSV is written under <rootDirectory>\\<project name>\\")] string rootDirectory,
    [Description("Optional file name without path (default Messages.csv; .csv is appended if missing)")] string? fileName = null,
    [Description("Optional column separator: \";\" (default, Excel with German locale), \",\" or \"tab\"")] string? separator = null,
    [Description("Optional device / station name to limit the scan")] string? deviceName = null,
    [Description("Optional CPU / device item name to limit the scan")] string? deviceItemName = null,
    [Description("Optional: true also exports messages with stConfig.xSelect = FALSE")] bool includeUnselected = false) {
    var activity = ToolActivity.Start<MessageTools>("Meldungen werden in eine CSV-Datei exportiert");
    try {
      string route = BuildRoute(MessageRoutes.GetFile, processId, projectName, deviceName, deviceItemName, includeUnselected)
        + $"&rootDirectory={Uri.EscapeDataString(rootDirectory)}";
      if (!string.IsNullOrWhiteSpace(fileName)) {
        route += $"&fileName={Uri.EscapeDataString(fileName)}";
      }
      if (!string.IsNullOrEmpty(separator)) {
        route += $"&separator={Uri.EscapeDataString(separator)}";
      }

      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  static string BuildRoute(string baseRoute, int processId, string projectName, string? deviceName, string? deviceItemName, bool includeUnselected) {
    string route = $"{baseRoute}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}";
    if (!string.IsNullOrWhiteSpace(deviceName)) {
      route += $"&deviceName={Uri.EscapeDataString(deviceName)}";
    }
    if (!string.IsNullOrWhiteSpace(deviceItemName)) {
      route += $"&deviceItemName={Uri.EscapeDataString(deviceItemName)}";
    }
    if (includeUnselected) {
      route += "&includeUnselected=true";
    }
    return route;
  }
}
