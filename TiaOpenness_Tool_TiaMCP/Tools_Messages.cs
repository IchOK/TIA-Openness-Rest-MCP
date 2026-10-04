using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class MessageTools {
  [McpServerTool, Description(
    "DEPRECATED / UNDER DEVELOPMENT: Do NOT use this tool. It is currently under active development and unstable." +
    "Collects all relevant PLC messages (Meldungen) from a TIA project. " +
    "Scans data blocks for configurations of type tbMsg_ConfElement_T and returns entries where xSelect start value is true. " +
    "Optional deviceName/deviceItemName limit the scan to one PLC; omit them to scan all PLCs in the project. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array of objects with: " +
    "Meldetext (comment of the config element), " +
    "Meldetype (start value of iType), " +
    "Meldeblock (HMI array index if nested in an array, otherwise null), " +
    "Meldeindex (0..15 within a 16-message unit), " +
    "DbName (data block that holds the config), " +
    "Path (string array of user group folders to the DB), " +
    "DeviceName, DeviceItemName, PlcName (PLC context), " +
    "ConfMemberPath (member path of the config element inside the DB).")]
  public static async Task<CallToolResult> ListMessages(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional device / station name to limit the scan")] string? deviceName = null,
    [Description("Optional CPU / device item name to limit the scan")] string? deviceItemName = null) {
    var activity = ToolActivity.Start<MessageTools>("Meldungen werden gesammelt");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{MessageRoutes.List}?processId={processId}&projectName={safeProjectName}";
      if (!string.IsNullOrWhiteSpace(deviceName)) {
        route += $"&deviceName={Uri.EscapeDataString(deviceName)}";
      }
      if (!string.IsNullOrWhiteSpace(deviceItemName)) {
        route += $"&deviceItemName={Uri.EscapeDataString(deviceItemName)}";
      }

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
