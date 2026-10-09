using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.PLC;

[McpServerToolType]
public class CompileTools {
  private const string ResultDescription =
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "IsSuccess only tells whether the compile could be run; compile errors are reported in Data with IsSuccess true. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, " +
    "Target (\"Block\", \"UDT\" or \"PLC\"), Name (block/UDT name or PLC name), " +
    "Path (folder of the block/UDT, as in ListBlocks/ListUdts; omitted for PLC), " +
    "State (\"Success\", \"Information\", \"Warning\" or \"Error\"), ErrorCount, WarningCount, " +
    "Messages (errors and warnings only, each with: " +
    "Location (string array of the compiler message tree from the PLC down to the message, " +
    "e.g. [\"PLC_1\", \"Program blocks\", \"Plant\", \"Main (OB1)\", \"12\"]: PLC, block folders, block with type and number, position), " +
    "BlockName (block the message belongs to, taken from Location; omitted for general messages), " +
    "State (\"Error\" or \"Warning\"), Description (message text of TIA Portal)). " +
    "For SCL blocks the last Location element is the line number in the SCL code; in the .s7dcl text (Content of GetBlock, file of GetBlockFile) " +
    "this is the line of the 'NETWORK' keyword plus that number (e.g. NETWORK in line 354 and position 75 = .s7dcl line 429). " +
    "Typical fix loop: compile, read the affected block (GetBlock/GetBlockFile) or UDT (GetUdt), fix it, write it back (PutBlock/PutBlockFile/PutUdt), compile again.";

  [McpServerTool, Description(
    "Compiles a single PLC block (FC, FB, OB, DB, …) or PLC data type (UDT) and returns the compile errors and warnings. " +
    "TIA Portal also compiles objects the item depends on if needed; their messages are included. " +
    "A block that is already consistent (unchanged since its last compile) is not compiled again, so its warnings are not repeated. " +
    "If itemType is omitted, a block with that name is searched first, then a UDT. " +
    ResultDescription)]
  public static async Task<CallToolResult> CompileBlockOrUdt(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the block or UDT")] string name,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Optional \"Block\" or \"UDT\"; required only if a block and a UDT have the same name")] string? itemType = null) {
    var activity = ToolActivity.Start<CompileTools>($"{name} wird übersetzt");
    try {
      string route = $"{CompileRoutes.Item}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&name={Uri.EscapeDataString(name)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";
      if (!string.IsNullOrWhiteSpace(itemType)) {
        route += $"&itemType={Uri.EscapeDataString(itemType)}";
      }

      using var httpContent = new StringContent("", Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PostAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool, Description(
    "Compiles the software of a PLC (\"Compile > Software (only changes)\" in TIA Portal) and returns the compile errors and warnings. " +
    "All inconsistent (changed or affected) UDTs and blocks are compiled, so every compile error of the program is reported; " +
    "warnings of blocks that were already compiled before are not repeated. " +
    "\"Software (rebuild all blocks)\" is not available in the TIA Openness API. " +
    "Hardware configuration is not compiled. Can take a few minutes for large programs. " +
    ResultDescription)]
  public static async Task<CallToolResult> CompilePlc(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
    var activity = ToolActivity.Start<CompileTools>($"PLC {deviceItemName} wird übersetzt");
    try {
      string route = $"{CompileRoutes.Plc}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      using var httpContent = new StringContent("", Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PostAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
