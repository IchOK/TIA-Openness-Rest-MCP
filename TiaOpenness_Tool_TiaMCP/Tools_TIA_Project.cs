using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class ProjectTools {
  [McpServerTool(Name = "tia_projects_list"), Description(
    "Returns a list of all currently open TIA Portal projects and their Process IDs. " +
    "Call this first to obtain processId and projectName for other tools. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array of objects with: " +
    "ProcessId (TIA Portal process ID; pass as processId to other tools), " +
    "Name (project name; pass as projectName to other tools), " +
    "Path (full file system path of the project).")]
  public static async Task<CallToolResult> ListProjects() {
    var activity = ToolActivity.Start<ProjectTools>("TIA Portal Projekte werden abgerufen");
    try {
      string route = $"{ProjectRoutes.List}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_projects_open"), Description(
    "Opens a TIA Portal project file (.ap<version>, e.g. .ap20) or retrieves a project archive (.zap<version>) into targetDirectory. " +
    "Without processId, a running TIA Portal instance without an open project is used; if there is none, TIA Portal (with user interface, " +
    "version from the REST appsettings.json) is started and the call waits until it accepts Openness connections (may take minutes). " +
    "With processId, that instance must not have a project open (close it first with tia_projects_close). " +
    "If the project is already open in some instance, that instance is returned with Mode \"AlreadyOpen\". " +
    "Projects of an older TIA Portal version need upgrade=true (the project is upgraded on opening). " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: ProcessId and Name (pass as processId/projectName to other tools), Path (project file), " +
    "Mode (\"Open\", \"OpenWithUpgrade\", \"Retrieve\", \"RetrieveWithUpgrade\" or \"AlreadyOpen\"), StartedTiaPortal (true if TIA Portal was started).")]
  public static async Task<CallToolResult> OpenProject(
    [Description("Absolute path of the project file (.ap<version>) or project archive (.zap<version>)")] string path,
    [Description("Optional ProcessId of a TIA Portal instance without open project; omit to use an idle instance or start TIA Portal")] int? processId = null,
    [Description("Optional: true opens/retrieves a project of an older TIA Portal version with upgrade")] bool upgrade = false,
    [Description("Archives only: absolute directory the archive is retrieved to")] string? targetDirectory = null,
    [Description("Optional maximum time in seconds to wait for a newly started TIA Portal (default 300)")] int? startTimeoutSeconds = null) {
    var activity = ToolActivity.Start<ProjectTools>($"Projekt {Path.GetFileName(path)} wird geöffnet");
    try {
      string route = $"{ProjectRoutes.Open}?path={Uri.EscapeDataString(path)}";
      if (processId.HasValue) {
        route += $"&processId={processId.Value}";
      }
      if (upgrade) {
        route += "&upgrade=true";
      }
      if (!string.IsNullOrWhiteSpace(targetDirectory)) {
        route += $"&targetDirectory={Uri.EscapeDataString(targetDirectory)}";
      }
      if (startTimeoutSeconds.HasValue) {
        route += $"&startTimeoutSeconds={startTimeoutSeconds.Value}";
      }

      using var httpContent = new StringContent("", Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PostAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_projects_save"), Description(
    "Saves an open TIA Portal project. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: ProcessId, Name, Path, WasModified (true if the project had unsaved changes).")]
  public static async Task<CallToolResult> SaveProject(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName) {
    var activity = ToolActivity.Start<ProjectTools>($"Projekt {projectName} wird gespeichert");
    try {
      string route = $"{ProjectRoutes.Save}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}";

      using var httpContent = new StringContent("", Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PostAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_projects_close"), Description(
    "Closes an open TIA Portal project; TIA Portal itself keeps running. " +
    "If the project has unsaved changes, pass save=true to save them first or discardChanges=true to discard them; " +
    "otherwise the call fails with status 409 and the project stays open. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: ProcessId, Name, Path, Saved (true if saved before closing), DiscardedChanges (true if unsaved changes were discarded).")]
  public static async Task<CallToolResult> CloseProject(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional: true saves the project before closing")] bool save = false,
    [Description("Optional: true closes the project even if it has unsaved changes, discarding them")] bool discardChanges = false) {
    var activity = ToolActivity.Start<ProjectTools>($"Projekt {projectName} wird geschlossen");
    try {
      string route = $"{ProjectRoutes.Close}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}";
      if (save) {
        route += "&save=true";
      }
      if (discardChanges) {
        route += "&discardChanges=true";
      }

      using var httpContent = new StringContent("", Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PostAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
