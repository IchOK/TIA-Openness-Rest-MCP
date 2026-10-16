using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.Project.Graphics;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class ProjectGraphicsTools {
  [McpServerTool(Name = "tia_project_graphics_list"), Description(
    "Lists all entries of the project graphics collection (Projektgrafiken under Languages & Resources). " +
    "These graphics are shared by all HMI devices, including WinCC Unified. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array of { Name }.")]
  public static async Task<CallToolResult> ListGraphics(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional name filter with * and ?, e.g. \"Icon_*\"")] string? namePattern = null) {
    var activity = ToolActivity.Start<ProjectGraphicsTools>("Projektgrafiken werden abgerufen");
    try {
      string route = $"{ProjectRoutes.GraphicsList}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}";
      if (!string.IsNullOrWhiteSpace(namePattern)) {
        route += $"&namePattern={Uri.EscapeDataString(namePattern)}";
      }
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_project_graphics_cleanup_duplicates"), Description(
    "Cleans up duplicate project graphics named like Original, Original_1, Original_2 (TIA copy/import suffixes). " +
    "Preferred order (proactive, not compile-driven): " +
    "(1) find every Name_<number> whose base Name exists in the project graphics, " +
    "(2) by default (compareContent=true) export both and keep only pairs with identical image bytes " +
    "(numbered variants that are different icons are left untouched), " +
    "(3) on all WinCC Unified devices, find screen properties ending with Graphic (Graphic, AlternateGraphic, BackGraphic, MarkerGraphic, …) " +
    "that still reference the duplicate and set them to the original name, " +
    "(4) delete the duplicate entries from the project graphics collection, " +
    "(5) optionally compile each Unified HMI (compileAfter) and return compiler messages. " +
    "Duplicates with remaining read-only or failed references are not deleted. " +
    "Content comparison can take many minutes on large graphic collections; narrow with namePattern when possible. " +
    "Use dryRun=true first. Changes are not saved; use tia_projects_save. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data (CleanupDuplicatesResult): " +
    "DryRun, GraphicCount, DuplicateCount, Duplicates [{ Duplicate, Original, ContentMatch }], " +
    "Usages [{ DeviceName, DeviceItemName, HmiName, ScreenName, ScreenPath, ItemName, ItemType, Property, OldValue, NewValue, " +
    "Action (\"Replace\", \"WouldReplace\", \"SkippedReadOnly\", \"Error\"), Error }], " +
    "DeletedGraphics [{ Kind \"Graphic\", Name, Action (\"Deleted\", \"WouldDelete\", \"Skipped\"), Details, Error }], " +
    "Compiles (optional compile results per Unified device), Warnings.")]
  public static async Task<CallToolResult> CleanupDuplicates(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional: only plan, change nothing")] bool dryRun = false,
    [Description("Optional: after cleanup, compile each Unified HMI and return messages")] bool compileAfter = false,
    [Description("Optional: true (default) only delete Name_<n> when image content matches Name; false = name heuristic only")] bool compareContent = true,
    [Description("Optional: only this Unified device name for reference updates and compile")] string? deviceName = null,
    [Description("Optional: Unified device item name")] string? deviceItemName = null,
    [Description("Optional: only duplicates whose original name matches * and ?, e.g. \"tvPhase_Icon_*\"")] string? namePattern = null) {
    var activity = ToolActivity.Start<ProjectGraphicsTools>(
      dryRun ? "Doppelte Projektgrafiken werden geprüft" : "Doppelte Projektgrafiken werden aufgeräumt");
    try {
      string route = $"{ProjectRoutes.GraphicsCleanupDuplicates}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}";
      var body = new CleanupDuplicatesRequest {
        DryRun = dryRun,
        CompileAfter = compileAfter,
        CompareContent = compareContent,
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
        NamePattern = namePattern
      };
      using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PostAsync(route, content);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
