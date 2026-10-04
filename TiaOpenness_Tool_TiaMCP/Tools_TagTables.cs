using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.TagTable;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class TagTableTools {
  [McpServerTool, Description(
    "Returns a list of all PLC tag tables of a specific TIA project. " +
    "Optionally restrict the result to a tag table folder with path (folder names from the tag table root, e.g. [\"TopCtrl_Func\"]); " +
    "tables in that folder and all its subfolders are returned, PLCs without that folder are omitted, and an error is returned if no PLC has it. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array with one object per PLC: " +
    "DeviceName (device / station name, required for GetTagTable), " +
    "DeviceItemName (CPU / device item name, required for GetTagTable), " +
    "PlcName (PLC software name), " +
    "TagTables (array of the PLC's tag tables, each with: " +
    "TagTableName (tag table name, required for GetTagTable), " +
    "IsDefault (true if this is the PLC's default tag table), " +
    "Path (string array of user group/folder names from the tag table root to the table; empty if the table is in the root)).")]
  public static async Task<CallToolResult> ListTagTables(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional tag table folder as folder names from the root, e.g. [\"TopCtrl_Func\"]; includes subfolders")] string[]? path = null) {
    var activity = ToolActivity.Start<TagTableTools>("Variablentabellen werden abgerufen");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{TagTableRoutes.List}?processId={processId}&projectName={safeProjectName}" + TiaRestClient.PathQuery(path);

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool, Description(
    "Returns tags, user constants and system constants of a PLC tag table. " +
    "Use DeviceName, DeviceItemName and TagTableName from ListTagTables. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName (device / station name), " +
    "DeviceItemName (CPU / device item name), " +
    "PlcName (PLC software name), " +
    "TagTableName (tag table name), " +
    "IsDefault (true if this is the PLC's default tag table), " +
    "Path (string array of user group/folder names from the tag table root to the table; empty if the table is in the root), " +
    "Tags (array of { Name, DataTypeName, LogicalAddress, Comment } — PLC tags with data type, absolute address such as \"%M10.0\" and comment), " +
    "UserConstants (array of { Name, DataTypeName, Value, Comment } — user-defined constants), " +
    "SystemConstants (array of { Name, DataTypeName, Value } — read-only system constants of the table). " +
    "Comment is the text in the project's editing language (null if not available). " +
    "Tags and UserConstants have the same shape as the entries expected by PutTagTable and PatchTagTable.")]
  public static async Task<CallToolResult> GetTagTable(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table")] string tagTableName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
    var activity = ToolActivity.Start<TagTableTools>($"Variablentabelle {tagTableName} wird abgerufen");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{TagTableRoutes.Get}?processId={processId}&projectName={safeProjectName}&tagTableName={Uri.EscapeDataString(tagTableName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool, Description(
    "Creates a PLC tag table or replaces the complete content of an existing one. " +
    "tags and userConstants are the full target content: entries missing from the lists are DELETED, changed entries are updated, new entries are created. " +
    "Pass empty lists to clear the table. To change only some entries, use PatchTagTable instead. " +
    "Typical workflow: GetTagTable, modify Tags/UserConstants, then PutTagTable with both lists. " +
    "Each tag needs Name, DataTypeName (e.g. \"Bool\", \"Int\", \"Real\") and LogicalAddress (e.g. \"%M10.0\", \"%MW12\", \"%I0.1\"); " +
    "each user constant needs Name, DataTypeName and Value (e.g. \"10\"). " +
    "Comment is written in the project's editing language; Comment null keeps the existing comment, \"\" clears it. " +
    "Tag and constant names are unique across the whole PLC: names used in another tag table are rejected with nothing changed. " +
    "System constants are read-only and not affected. " +
    "Path is only used when a new table is created (user group names from the tag table root, missing folders are created; omit for the root folder); " +
    "an existing table is never moved. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, TagTableName, Path, " +
    "Created (true = table newly created), " +
    "Changes (array of { Kind (\"Tag\" or \"UserConstant\"), Name, Action (\"Created\", \"Updated\", \"Deleted\" or \"Unchanged\"), Details (changed fields, e.g. \"LogicalAddress: %M10.0 -> %M12.0\"), Error (null on success) }), " +
    "Messages (notes, e.g. a Path that was ignored). " +
    "If some entries failed, IsSuccess is false and Error contains the same JSON object; the other entries are applied.")]
  public static async Task<CallToolResult> PutTagTable(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table")] string tagTableName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Complete list of tags: objects with Name, DataTypeName, LogicalAddress and optional Comment")] TagInfo[] tags,
    [Description("Complete list of user constants: objects with Name, DataTypeName, Value and optional Comment")] ConstantInfo[] userConstants,
    [Description("Optional target folder for a new table as user group names from the root, e.g. [\"Plant\", \"Unit1\"]; ignored for existing tables")] string[]? path = null) {
    var activity = ToolActivity.Start<TagTableTools>($"Variablentabelle {tagTableName} wird geschrieben");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{TagTableRoutes.Put}?processId={processId}&projectName={safeProjectName}&tagTableName={Uri.EscapeDataString(tagTableName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutRequest {
        Path = path,
        Tags = tags?.ToList(),
        UserConstants = userConstants?.ToList()
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool, Description(
    "Changes individual fields of existing tags and user constants in a PLC tag table; nothing is created or deleted. " +
    "Identify each entry by Name and set only the fields to change; fields that are null stay unchanged. " +
    "Tags: DataTypeName, LogicalAddress (e.g. \"%M10.0\"), Comment. User constants: DataTypeName, Value, Comment. " +
    "Comment is written in the project's editing language; \"\" clears it. Renaming is not supported. " +
    "If any name is not found in the table, nothing is changed (use PutTagTable to create entries). " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, TagTableName, " +
    "Changes (array of { Kind (\"Tag\" or \"UserConstant\"), Name, Action (\"Updated\" or \"Unchanged\"), Details (changed fields, e.g. \"LogicalAddress: %M10.0 -> %M12.0\"), Error (null on success) }). " +
    "If some entries failed, IsSuccess is false and Error contains the same JSON object; the other entries are applied.")]
  public static async Task<CallToolResult> PatchTagTable(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table")] string tagTableName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Tags to change: objects with Name and the fields to change (DataTypeName, LogicalAddress, Comment); omit or empty for none")] TagInfo[]? tags = null,
    [Description("User constants to change: objects with Name and the fields to change (DataTypeName, Value, Comment); omit or empty for none")] ConstantInfo[]? userConstants = null) {
    if ((tags?.Length ?? 0) + (userConstants?.Length ?? 0) == 0) {
      return TiaRestClient.FromValidationError("Error: Pass at least one entry in tags or userConstants.");
    }
    var activity = ToolActivity.Start<TagTableTools>($"Variablentabelle {tagTableName} wird angepasst");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{TagTableRoutes.Patch}?processId={processId}&projectName={safeProjectName}&tagTableName={Uri.EscapeDataString(tagTableName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PatchRequest {
        Tags = tags?.ToList(),
        UserConstants = userConstants?.ToList()
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PatchAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
