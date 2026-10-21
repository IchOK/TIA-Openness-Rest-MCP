using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch;
using Tophinke.TiaOpenness.Tool.Types.PLC.TagTable;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.PLC;

[McpServerToolType]
public class TagTableTools {
  [McpServerTool(Name = "tia_plc_tagtables_list"), Description(
    "Returns a list of all PLC tag tables of a specific TIA project. " +
    "Optionally restrict the result to a tag table folder with path (folder names from the tag table root, e.g. [\"TopCtrl_Func\"]); " +
    "tables in that folder and all its subfolders are returned, PLCs without that folder are omitted, and an error is returned if no PLC has it. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array with one object per PLC: " +
    "DeviceName (device / station name, required for tia_plc_tagtables_get), " +
    "DeviceItemName (CPU / device item name, required for tia_plc_tagtables_get), " +
    "PlcName (PLC software name), " +
    "TagTables (array of the PLC's tag tables, each with: " +
    "TagTableName (tag table name, required for tia_plc_tagtables_get), " +
    "IsDefault (true if this is the PLC's default tag table), " +
    "Path (string array of user group/folder names from the tag table root to the table; empty if the table is in the root), " +
    "ModifiedDate (UTC ISO-8601 last change of the table, Openness PlcTagTable.ModifiedTimeStamp)).")]
  public static async Task<CallToolResult> ListTagTables(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional TIA tag table folder path (not filesystem): folder names from the PLC tag table root, e.g. [\"TopCtrl_Func\"]; includes subfolders")] string[]? path = null) {
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

  [McpServerTool(Name = "tia_plc_tagtables_get"), Description(
    "Returns tags, user constants and system constants of a PLC tag table. " +
    "Use DeviceName, DeviceItemName and TagTableName from tia_plc_tagtables_list. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName (device / station name), " +
    "DeviceItemName (CPU / device item name), " +
    "PlcName (PLC software name), " +
    "TagTableName (tag table name), " +
    "IsDefault (true if this is the PLC's default tag table), " +
    "Path (string array of user group/folder names from the tag table root to the table; empty if the table is in the root), " +
    "ModifiedDate (UTC ISO-8601 last change of the table, Openness PlcTagTable.ModifiedTimeStamp), " +
    "Tags (array of { Name, DataTypeName, LogicalAddress, Comment } — PLC tags with data type, absolute address such as \"%M10.0\" and comment), " +
    "UserConstants (array of { Name, DataTypeName, Value, Comment } — user-defined constants), " +
    "SystemConstants (array of { Name, DataTypeName, Value } — read-only system constants of the table). " +
    "Comment is the text in the project's editing language (null if not available). " +
    "Tags and UserConstants have the same shape as the entries expected by tia_plc_tagtables_put and tia_plc_tagtables_patch.")]
  public static async Task<CallToolResult> GetTagTable(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table")] string tagTableName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName) {
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

  [McpServerTool(Name = "tia_plc_tagtables_put"), Description(
    "Creates a PLC tag table or replaces the complete content of an existing one. " +
    "tags and userConstants are the full target content: entries missing from the lists are DELETED, changed entries are updated, new entries are created. " +
    "Pass empty lists to clear the table. To change only some entries, use tia_plc_tagtables_patch instead. " +
    "Typical workflow: tia_plc_tagtables_get, modify Tags/UserConstants, then tia_plc_tagtables_put with both lists. " +
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
    "If some entries failed, IsSuccess is false, Error is a short summary and Data still contains the same JSON object; the other entries are applied.")]
  public static async Task<CallToolResult> PutTagTable(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table")] string tagTableName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Complete list of tags: objects with Name, DataTypeName, LogicalAddress and optional Comment")] TagInfo[] tags,
    [Description("Complete list of user constants: objects with Name, DataTypeName, Value and optional Comment")] ConstantInfo[] userConstants,
    [Description("Optional TIA target folder for a new table (not filesystem): user group names from the PLC tag table root, e.g. [\"Plant\", \"Unit1\"]; ignored for existing tables")] string[]? path = null) {
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

  [McpServerTool(Name = "tia_plc_tagtables_patch"), Description(
    "Changes individual fields of existing tags and user constants in a PLC tag table; nothing is created or deleted. " +
    "Identify each entry by Name and set only the fields to change; fields that are null stay unchanged. " +
    "Tags: DataTypeName, LogicalAddress (e.g. \"%M10.0\"), Comment. User constants: DataTypeName, Value, Comment. " +
    "Comment is written in the project's editing language; \"\" clears it. Renaming is not supported. " +
    "If any name is not found in the table, nothing is changed (use tia_plc_tagtables_put to create entries). " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, TagTableName, " +
    "Changes (array of { Kind (\"Tag\" or \"UserConstant\"), Name, Action (\"Updated\" or \"Unchanged\"), Details (changed fields, e.g. \"LogicalAddress: %M10.0 -> %M12.0\"), Error (null on success) }). " +
    "If some entries failed, IsSuccess is false, Error is a short summary and Data still contains the same JSON object; the other entries are applied.")]
  public static async Task<CallToolResult> PatchTagTable(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table")] string tagTableName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
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

  private const string FileLayoutDescription =
    "Tag tables are exchanged as SimaticML XML only (TIA Openness has no SIMATIC SD format for tag tables): " +
    "one file <TagTableName>.xml written directly into directory. The XML contains tags, user constants and all texts. " +
    "directory is an absolute filesystem folder on the TIA Portal/REST machine; path is the TIA tag table folder (not the filesystem). ";

  private const string ImportDescription =
    "The XML replaces the whole content of the tag table (TIA import with override). " +
    "A new table is created in the TIA folder Path (missing folders are created); " +
    "an existing table is overwritten in its current folder and never moved (Messages notes a differing Path). " +
    "Tag and constant names are unique across all tag tables of a PLC: names used in another table make the import fail. " +
    "Nothing is compiled. The files are not changed or deleted. ";

  [McpServerTool(Name = "tia_plc_tagtables_get_file"), Description(
    "Exports a PLC tag table to a SimaticML XML file. " +
    FileLayoutDescription +
    "An existing file of the table is replaced; missing directories are created. " +
    "Use tia_plc_tagtables_put_file with the same directory and path to import the (edited) file again. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: DeviceName, DeviceItemName, PlcName, TagTableName, IsDefault, " +
    "Path (tag table folder path in the project), Format (\"SimaticML/XML\"), Directory, Files (full path of the exported file).")]
  public static async Task<CallToolResult> GetTagTableFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table")] string tagTableName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem directory on the TIA Portal/REST machine; the file is written directly there")] string directory,
    [Description("Optional TIA folder of the table (not filesystem): user group names from the PLC tag table root; omit to search the whole PLC")] string[]? path = null) {
    var activity = ToolActivity.Start<TagTableTools>($"Variablentabelle {tagTableName} wird in eine Datei exportiert");
    try {
      var body = new GetFileRequest {
        ProcessId = processId,
        ProjectName = projectName,
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
        Directory = directory,
        TagTableName = tagTableName,
        Path = path
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PostAsync(TagTableRoutes.GetFile, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_tagtables_put_file"), Description(
    "Creates or overwrites a PLC tag table from a SimaticML XML file (the layout written by tia_plc_tagtables_get_file). " +
    "directory is the absolute filesystem folder containing <TagTableName>.xml. path is the TIA folder for a new table. " +
    ImportDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: DeviceName, DeviceItemName, PlcName, TagTableName, IsDefault, Path (folder after the import), " +
    "Created (true = new table), Format, Messages, Directory, Files (full path of the imported file).")]
  public static async Task<CallToolResult> PutTagTableFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table (= file name without .xml)")] string tagTableName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem directory on the TIA Portal/REST machine containing the XML file")] string directory,
    [Description("Optional TIA folder for a new table (not filesystem): user group names from the PLC tag table root; omit = current folder or root")] string[]? path = null) {
    var activity = ToolActivity.Start<TagTableTools>($"Variablentabelle {tagTableName} wird aus einer Datei importiert");
    try {
      string route = $"{TagTableRoutes.PutFile}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&tagTableName={Uri.EscapeDataString(tagTableName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutFileRequest {
        Directory = directory,
        Path = path
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_tagtables_get_dir"), Description(
    "Exports all tag tables of a TIA tag table folder to XML files, like tia_plc_tagtables_get_file for every table, in a single TIA session. " +
    "directory is the absolute filesystem folder that corresponds to path; TIA subfolders relative to path become subdirectories of directory. " +
    "path selects the folder (folder names from the tag table root; omit for the root folder, i.e. all tables of the PLC), " +
    "recursive (default true) includes all subfolders. " +
    FileLayoutDescription +
    "Each item: same fields as tia_plc_tagtables_get_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> GetTagTableDir(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem directory on the TIA Portal/REST machine corresponding to path")] string directory,
    [Description("Optional TIA tag table folder path (not filesystem): folder names from the PLC tag table root; omit for the root folder")] string[]? path = null,
    [Description("Include subfolders (default true)")] bool recursive = true) {
    var activity = ToolActivity.Start<TagTableTools>("Variablentabellen eines Ordners werden in Dateien exportiert");
    try {
      var body = new GetDirRequest {
        ProcessId = processId,
        ProjectName = projectName,
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
        Directory = directory,
        Path = path,
        Recursive = recursive
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PostAsync(TagTableRoutes.GetDir, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_tagtables_get_file_list"), Description(
    "Exports several tag tables to XML files, exactly like tia_plc_tagtables_get_file for each entry in items, in a single TIA session. " +
    "Each entry has Directory (absolute filesystem folder), TagTableName and optional Path (TIA folder; omit to search the whole PLC). " +
    "Each item: same fields as tia_plc_tagtables_get_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> GetTagTableFileList(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Tag tables to export: Directory (absolute filesystem folder, required), TagTableName (required), Path (optional TIA folder names)")] PutFileListItem[] items) {
    if (items == null || items.Length == 0) {
      return TiaRestClient.FromValidationError("Error: items must contain at least one tag table.");
    }
    var activity = ToolActivity.Start<TagTableTools>($"{items.Length} Variablentabellen werden in Dateien exportiert");
    try {
      var body = new GetFileListRequest<PutFileListItem> {
        ProcessId = processId,
        ProjectName = projectName,
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
        Items = items.ToList()
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PostAsync(TagTableRoutes.GetFileList, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_tagtables_put_dir"), Description(
    "Imports all tag table files of a directory, like tia_plc_tagtables_put_file for every file, in a single TIA session. " +
    "directory is the absolute filesystem folder corresponding to path; with recursive (default true), subdirectories are included and appended to path. " +
    "every <Name>.xml whose content is a tag table export is imported as tag table <Name>; subdirectory names are the TIA folder names for new tables. " +
    "Files of blocks and UDTs, and files whose content is not recognized, are listed in Skipped. " +
    ImportDescription +
    "Tables whose import fails in TIA are retried after the others. " +
    "Each item: same fields as tia_plc_tagtables_put_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> PutTagTableDir(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem directory on the TIA Portal/REST machine corresponding to path")] string directory,
    [Description("Optional TIA folder path (not filesystem): folder names from the PLC tag table root; omit for the root folder")] string[]? path = null,
    [Description("Include subdirectories (default true)")] bool recursive = true) {
    var activity = ToolActivity.Start<TagTableTools>("Variablentabellen eines Verzeichnisses werden importiert");
    try {
      string route = $"{TagTableRoutes.PutDir}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutDirRequest {
        Directory = directory,
        Path = path,
        Recursive = recursive
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_tagtables_put_file_list"), Description(
    "Imports several tag tables from XML files, exactly like tia_plc_tagtables_put_file for each entry in items, in a single TIA session. " +
    "Each entry has Directory (absolute filesystem folder), TagTableName and optional Path with the same meaning as in tia_plc_tagtables_put_file. " +
    ImportDescription +
    "Tables whose import fails in TIA are retried after the others. " +
    "Each item: same fields as tia_plc_tagtables_put_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> PutTagTableFileList(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Tag tables to import: Directory (absolute filesystem folder, required), TagTableName (required), Path (optional TIA folder)")] PutFileListItem[] items) {
    if (items == null || items.Length == 0) {
      return TiaRestClient.FromValidationError("Error: items must contain at least one tag table.");
    }
    var activity = ToolActivity.Start<TagTableTools>($"{items.Length} Variablentabellen werden aus Dateien importiert");
    try {
      string route = $"{TagTableRoutes.PutFileList}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutFileListRequest {
        Items = items.ToList()
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
