using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.PLC.Block;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.PLC;

[McpServerToolType]
public class BlockTools {
  private const string LibraryInfoDescription =
    "Library (only for instances of a project library type, otherwise omitted/null: TypeName, Version e.g. \"0.1.36\", " +
    "State \"Committed\" = released and write-protected or \"InWork\" = in-test version being edited)";

  private const string LibraryWriteDescription =
    "Instances of project library types (Library is set in tia_plc_blocks_list/tia_plc_blocks_get) cannot be overwritten by a plain import: " +
    "without libraryMode the call is rejected with StatusCode 409 and nothing is changed. " +
    "libraryMode \"InTest\" writes the content into the library type as in-test version: a released version is first opened for editing " +
    "(this block becomes the test instance), an existing in-test version is overwritten; the block must stay in its folder. " +
    "libraryMode \"Release\" additionally releases that version (optional libraryVersion e.g. \"0.1.37\", default = number of the in-test version; " +
    "libraryAuthor, libraryComment); dependent types are not released automatically, and a failed release leaves the content in the in-test version. " +
    "Use tia_plc_blocks_discard_type_version to throw away an in-test version. Other instances of the type in the project are not updated. ";

  [McpServerTool(Name = "tia_plc_blocks_list"), Description(
    "Returns a list of all PLC blocks of a specific TIA project. " +
    "Optionally filter with the comma-separated Openness type names in filterTypes (exact match on BlockType). " +
    "Allowed filter strings and their meaning: " +
    "FC = Function: code block with input, output and in-out parameters, but no retained internal memory (stateless; each call works only with its interface); " +
    "FB = Function block: code block with input, output and in-out parameters plus internal static memory (instance data, typically stored in an InstanceDB); " +
    "OB = Organization block: cyclic/event-driven entry point of the PLC program (e.g. main cycle, startup, interrupt, error); " +
    "GlobalDB = Global data block: standalone data storage shared across the program, not tied to one FB instance; " +
    "InstanceDB = Instance data block: data storage belonging to a specific FB instance (holds that FB's internal memory); " +
    "ArrayDB = Array data block: data block whose structure is an array of a PLC data type; " +
    "TechnologicalInstanceDB = Technological instance DB: instance data for a technological object. " +
    "Example: filterTypes=\"FC,FB,OB,GlobalDB\". Omit filterTypes to return all block types. " +
    "Optionally filter with the comma-separated programming languages in filterLanguages (exact match on ProgrammingLanguage, case-insensitive), " +
    "e.g. \"SCL,LAD\"; common values: LAD, FBD, STL, SCL, GRAPH, DB, F_LAD, F_FBD, F_DB, ProDiag; unknown values are rejected with the list of allowed ones. " +
    "Optionally restrict the result to a block folder with path (folder names from the block group root, e.g. [\"TopCtrl_Func\", \"Basis\"]); " +
    "blocks in that folder and all its subfolders are returned, PLCs without that folder are omitted, and an error is returned if no PLC has it. " +
    "All filters can be combined. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array with one object per PLC: " +
    "DeviceName (device / station name, required for tia_plc_blocks_get), " +
    "DeviceItemName (CPU / device item name, required for tia_plc_blocks_get), " +
    "PlcName (PLC software name), " +
    "Blocks (array of the PLC's blocks, each with: " +
    "BlockName (block name, required for tia_plc_blocks_get), " +
    "BlockNumber (block number in the PLC), " +
    "BlockType (Openness type name such as FC, FB, OB, GlobalDB, …), " +
    "ProgrammingLanguage (e.g. LAD, SCL, FBD, STL, DB), " +
    "Path (string array of user group/folder names from the block group root to the block; empty if the block is in the root), " +
    LibraryInfoDescription + ").")]
  public static async Task<CallToolResult> ListBlocks(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description(
      "Optional comma-separated filter of exact Openness BlockType names. " +
      "Allowed values: " +
      "FC (function: IN/OUT/INOUT, no internal memory); " +
      "FB (function block: IN/OUT/INOUT plus internal memory via instance DB); " +
      "OB (organization block / program entry point); " +
      "GlobalDB (shared global data block); " +
      "InstanceDB (FB instance data block); " +
      "ArrayDB (array data block); " +
      "TechnologicalInstanceDB (technological object instance DB). " +
      "Example: FC,FB,GlobalDB")] string? filterTypes = null,
    [Description("Optional comma-separated filter of programming languages (ProgrammingLanguage), e.g. \"SCL,LAD\"")] string? filterLanguages = null,
    [Description("Optional TIA block folder path (not filesystem): folder names from the PLC block root, e.g. [\"TopCtrl_Func\", \"Basis\"]; includes subfolders")] string[]? path = null) {

    var activity = ToolActivity.Start<BlockTools>("Bausteine werden abgerufen");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.List}?processId={processId}&projectName={safeProjectName}";
      if (!string.IsNullOrWhiteSpace(filterTypes)) {
        route += $"&filterTypes={Uri.EscapeDataString(filterTypes)}";
      }
      if (!string.IsNullOrWhiteSpace(filterLanguages)) {
        route += $"&filterLanguages={Uri.EscapeDataString(filterLanguages)}";
      }
      route += TiaRestClient.PathQuery(path);

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_get"), Description(
    "Returns the content of a PLC block (FC, FB, OB, GlobalDB, InstanceDB, …). " +
    "Use DeviceName, DeviceItemName and BlockName from tia_plc_blocks_list. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName (device / station name), " +
    "DeviceItemName (CPU / device item name), " +
    "PlcName (PLC software name), " +
    "BlockName (block name), " +
    "BlockNumber (block number in the PLC), " +
    "BlockType (Openness type name such as FC, FB, OB, GlobalDB, …), " +
    "ProgrammingLanguage (e.g. LAD, SCL, FBD, STL, DB), " +
    "Path (string array of user group/folder names from the block group root to the block; empty if the block is in the root), " +
    LibraryInfoDescription + ", " +
    "Format (\"SimaticData/SD\" for LAD, FBD, SCL and DB incl. F-variants, or \"SimaticML/XML\" for all other languages such as STL or GRAPH, " +
    "and as fallback when the SD export fails, e.g. for blocks last modified before TIA Portal V20 Update 4), " +
    "Content (block text: SIMATIC SD (.s7dcl) or Simatic ML XML with interface, attributes and program code where applicable), " +
    "MultiLingualText (only for Format \"SimaticData/SD\": content of the .s7res file with the multilingual comments and texts. " +
    "Content references them by ID, e.g. { S7_MLC := \"MLC_3SC\" }, and MultiLingualText holds the texts per ID and language " +
    "(YAML: \"MultiLingualTexts:\" followed by entries \"- id: MLC_3SC\" and \"de-DE: <text>\"). " +
    "null for \"SimaticML/XML\" (texts are inside the XML) or if the block has no such texts). " +
    "To write the block back with tia_plc_blocks_put, pass Format and MultiLingualText exactly as returned here together with the (modified) Content.")]
  public static async Task<CallToolResult> GetBlock(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the block")] string blockName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Optional export format: \"SD\" or \"XML\". Omit for SD where supported with XML as fallback")] string? format = null) {
    var activity = ToolActivity.Start<BlockTools>($"Baustein {blockName} wird abgerufen");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.Get}?processId={processId}&projectName={safeProjectName}&blockName={Uri.EscapeDataString(blockName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";
      if (!string.IsNullOrWhiteSpace(format)) {
        route += $"&format={Uri.EscapeDataString(format)}";
      }

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_put"), Description(
    "Creates a new PLC block or overwrites an existing block with the same name (FC, FB, OB, GlobalDB, …). " +
    "Content, Format and MultiLingualText correspond to the fields of the same name returned by tia_plc_blocks_get. " +
    "Typical workflow: tia_plc_blocks_get, modify Content (and MultiLingualText if needed), then tia_plc_blocks_put with the same Format and MultiLingualText. " +
    "Format \"SimaticData/SD\": Content is the .s7dcl text; multilingual comments are referenced by ID, e.g. { S7_MLC := \"MLC_3SC\" }, " +
    "and MultiLingualText (the .s7res text from tia_plc_blocks_get) must contain an entry \"- id: <ID>\" with the texts per language for every referenced ID. " +
    "When adding a new comment, add a new unique ID in Content and a matching entry in MultiLingualText. " +
    "MultiLingualText may be null only if Content references no IDs. " +
    "Format \"SimaticML/XML\": Content is the XML text including all texts; MultiLingualText must be null. " +
    "Mismatches (e.g. XML content with Format SimaticData/SD, or missing text IDs) are rejected before anything is changed. " +
    "Path selects the target folder (user group names from the block group root, missing folders are created); " +
    "omit Path to keep an existing block in its current folder or to create a new block in the root folder. " +
    "If an existing block lives in a different folder than Path, it is moved (deleted and re-imported; restored if the import fails). " +
    "An overwritten block keeps its block number; pass blockNumber to set a specific number. " +
    "Know-how-protected blocks cannot be overwritten. The block is not compiled. " +
    LibraryWriteDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, BlockName, BlockNumber, BlockType, ProgrammingLanguage, " +
    "Path (folder of the block after the import), " +
    LibraryInfoDescription + " after the call, " +
    "Created (true = new block, false = existing block overwritten), " +
    "Format (format used for the import), " +
    "Messages (messages reported by TIA Portal during the import, incl. the library steps).")]
  public static async Task<CallToolResult> PutBlock(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the block")] string blockName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Complete block text in the given format (Content from tia_plc_blocks_get, modified as needed)")] string content,
    [Description("MultiLingualText from tia_plc_blocks_get (.s7res text with the texts for all S7_MLC IDs in Content); required for \"SimaticData/SD\" if Content references IDs, otherwise null; must be null for \"SimaticML/XML\"")] string? multiLingualText,
    [Description("Format from tia_plc_blocks_get: \"SimaticData/SD\" or \"SimaticML/XML\"")] string format,
    [Description("Optional TIA target folder (not filesystem): user group names from the PLC root, e.g. [\"Plant\", \"Unit1\"]; empty array = root folder")] string[]? path = null,
    [Description("Optional block number; omit to keep the number of an existing block or to let TIA assign one for a new block")] int? blockNumber = null,
    [Description("Only for instances of library types: \"InTest\" (write as in-test version) or \"Release\" (write and release); omit for normal blocks")] string? libraryMode = null,
    [Description("Only with libraryMode \"Release\": version number to release, e.g. \"0.1.37\"; omit to use the number of the in-test version")] string? libraryVersion = null,
    [Description("Only with libraryMode \"Release\": author of the released version; omit to keep the author of the version")] string? libraryAuthor = null,
    [Description("Only with libraryMode \"Release\": comment of the released version")] string? libraryComment = null) {
    if (string.IsNullOrWhiteSpace(format)) {
      return TiaRestClient.FromValidationError("Error: format is required: \"SimaticData/SD\" or \"SimaticML/XML\" (use Format from tia_plc_blocks_get).");
    }
    var activity = ToolActivity.Start<BlockTools>($"Baustein {blockName} wird geschrieben");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.Put}?processId={processId}&projectName={safeProjectName}&blockName={Uri.EscapeDataString(blockName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutRequest {
        Path = path,
        Format = format,
        Content = content,
        MultiLingualText = multiLingualText,
        BlockNumber = blockNumber,
        LibraryMode = libraryMode,
        LibraryVersion = libraryVersion,
        LibraryAuthor = libraryAuthor,
        LibraryComment = libraryComment
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_get_file"), Description(
    "Exports a PLC block to files instead of returning its content (same block selection as tia_plc_blocks_get). " +
    "The files are written to <rootDirectory>\\<project name>\\<block folder path>\\, mirroring the block folders of the project " +
    "(invalid file name characters in project and folder names are replaced by '_'); missing directories are created. " +
    "Files: <BlockName>.s7dcl plus <BlockName>.s7res (if the block has multilingual texts) for Format \"SimaticData/SD\" (LAD, FBD, SCL, DB incl. F-variants), " +
    "or <BlockName>.xml for Format \"SimaticML/XML\" (all other languages such as STL or GRAPH). " +
    "Existing .s7dcl/.s7res/.xml files of the block in that directory are replaced. " +
    "rootDirectory is a path on the machine running TIA Portal and the REST API. " +
    "Use tia_plc_blocks_put_file with the same rootDirectory to import the (edited) files again. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, BlockName, BlockNumber, BlockType, ProgrammingLanguage, " +
    "Path (block folder path in the project), " +
    LibraryInfoDescription + ", " +
    "Format (\"SimaticData/SD\" or \"SimaticML/XML\"), " +
    "Directory (full path of the directory the files were written to), " +
    "Files (array of full paths of the exported files).")]
  public static async Task<CallToolResult> GetBlockFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the block")] string blockName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem path on the TIA Portal/REST machine used as export base, e.g. \"C:\\\\TiaFiles\"; files are written under <rootDirectory>\\<project name>\\<TIA block folder path>")] string rootDirectory) {
    var activity = ToolActivity.Start<BlockTools>($"Baustein {blockName} wird in Dateien exportiert");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.GetFile}?processId={processId}&projectName={safeProjectName}&blockName={Uri.EscapeDataString(blockName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}&rootDirectory={Uri.EscapeDataString(rootDirectory)}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_put_file"), Description(
    "Creates a new PLC block or overwrites an existing block with the same name from files (same behavior as tia_plc_blocks_put, but without passing the content). " +
    "The files are read from <rootDirectory>\\<project name>\\<folder path>\\, the layout written by tia_plc_blocks_get_file; " +
    "<folder path> is Path if given, otherwise the current folder of an existing block, otherwise the root folder. " +
    "Expected files: <BlockName>.s7dcl plus optional <BlockName>.s7res for Format \"SimaticData/SD\", or <BlockName>.xml for Format \"SimaticML/XML\". " +
    "If format is omitted, it is derived from the existing files (.s7dcl = SimaticData/SD, .xml = SimaticML/XML); if both exist, format is required. " +
    "For SimaticData/SD every text ID referenced in the .s7dcl file (e.g. { S7_MLC := \"MLC_3SC\" }) needs an entry in the .s7res file. " +
    "Path also selects the target folder in the project (missing folders are created); if an existing block lives in a different folder, it is moved. " +
    "An overwritten block keeps its block number; pass blockNumber to set a specific number. " +
    "Know-how-protected blocks cannot be overwritten. The block is not compiled. The files are not changed or deleted. " +
    "rootDirectory is a path on the machine running TIA Portal and the REST API. " +
    LibraryWriteDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, BlockName, BlockNumber, BlockType, ProgrammingLanguage, " +
    "Path (folder of the block after the import), " +
    LibraryInfoDescription + " after the call, " +
    "Created (true = new block, false = existing block overwritten), " +
    "Format (format used for the import), " +
    "Messages (messages reported by TIA Portal during the import, incl. the library steps), " +
    "Directory (directory the files were read from), " +
    "Files (array of full paths of the imported files).")]
  public static async Task<CallToolResult> PutBlockFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the block")] string blockName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem path on the TIA Portal/REST machine used as import base, e.g. \"C:\\\\TiaFiles\"; files are read from <rootDirectory>\\<project name>\\<TIA folder path>")] string rootDirectory,
    [Description("Optional \"SimaticData/SD\" or \"SimaticML/XML\"; omit to derive it from the existing files")] string? format = null,
    [Description("Optional TIA folder path (not filesystem): user group names from the PLC root, e.g. [\"Plant\", \"Unit1\"]; selects both the filesystem subdirectory under rootDirectory and the TIA target folder; empty = root")] string[]? path = null,
    [Description("Optional block number; omit to keep the number of an existing block or to let TIA assign one for a new block")] int? blockNumber = null,
    [Description("Only for instances of library types: \"InTest\" (write as in-test version) or \"Release\" (write and release); omit for normal blocks")] string? libraryMode = null,
    [Description("Only with libraryMode \"Release\": version number to release, e.g. \"0.1.37\"; omit to use the number of the in-test version")] string? libraryVersion = null,
    [Description("Only with libraryMode \"Release\": author of the released version; omit to keep the author of the version")] string? libraryAuthor = null,
    [Description("Only with libraryMode \"Release\": comment of the released version")] string? libraryComment = null) {
    var activity = ToolActivity.Start<BlockTools>($"Baustein {blockName} wird aus Dateien importiert");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.PutFile}?processId={processId}&projectName={safeProjectName}&blockName={Uri.EscapeDataString(blockName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutFileRequest {
        RootDirectory = rootDirectory,
        Path = path,
        Format = format,
        BlockNumber = blockNumber,
        LibraryMode = libraryMode,
        LibraryVersion = libraryVersion,
        LibraryAuthor = libraryAuthor,
        LibraryComment = libraryComment
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_get_dir"), Description(
    "Exports all PLC blocks of a block folder to files, like tia_plc_blocks_get_file for every block; uses a single TIA session, so it is much faster than many single calls. " +
    "path selects the block folder (folder names from the block group root; omit for the root folder, i.e. the whole PLC), " +
    "recursive (default true) includes all subfolders. " +
    "Each block is written to <rootDirectory>\\<project name>\\<block folder path>\\ as <BlockName>.s7dcl (+ .s7res) or <BlockName>.xml, replacing existing files of the block; " +
    "other files in the directories are not touched. Know-how-protected blocks are skipped. " +
    "UDTs and tag tables exported with tia_plc_udts_get_dir/tia_plc_tagtables_get_dir go to the same directory tree when their folders have the same names; the Put*Dir tools tell the kinds apart by file content. " +
    "Use tia_plc_blocks_put_dir or tia_plc_blocks_put_file_list with the same rootDirectory to import the (edited) files again. " +
    "Each item: DeviceName, DeviceItemName, PlcName, BlockName, BlockNumber, BlockType, ProgrammingLanguage, Path, " + LibraryInfoDescription + ", Format, Directory, Files. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> GetBlockDir(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem path on the TIA Portal/REST machine used as export base, e.g. \"C:\\\\TiaFiles\"; files are written under <rootDirectory>\\<project name>\\<TIA block folder path>")] string rootDirectory,
    [Description("Optional TIA block folder path (not filesystem): folder names from the PLC block root, e.g. [\"TopCtrl_Func\", \"Sequenz\"]; omit for the root folder")] string[]? path = null,
    [Description("Include subfolders (default true)")] bool recursive = true) {
    var activity = ToolActivity.Start<BlockTools>("Bausteine eines Ordners werden in Dateien exportiert");
    try {
      string route = $"{BlockRoutes.GetDir}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}&rootDirectory={Uri.EscapeDataString(rootDirectory)}&recursive={recursive}"
        + TiaRestClient.PathQuery(path);

      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_get_file_list"), Description(
    "Exports several PLC blocks to files, exactly like tia_plc_blocks_get_file for each name in blockNames, in a single TIA session. " +
    "Each block is written to <rootDirectory>\\<project name>\\<block folder path>\\. " +
    "Each item: DeviceName, DeviceItemName, PlcName, BlockName, BlockNumber, BlockType, ProgrammingLanguage, Path, " + LibraryInfoDescription + ", Format, Directory, Files. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> GetBlockFileList(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem path on the TIA Portal/REST machine used as export base, e.g. \"C:\\\\TiaFiles\"; files under <rootDirectory>\\<project name>\\<TIA folder path>")] string rootDirectory,
    [Description("Names of the blocks to export")] string[] blockNames) {
    if (blockNames == null || blockNames.All(string.IsNullOrWhiteSpace)) {
      return TiaRestClient.FromValidationError("Error: blockNames must contain at least one block name.");
    }
    var activity = ToolActivity.Start<BlockTools>($"{blockNames.Length} Bausteine werden in Dateien exportiert");
    try {
      string route = $"{BlockRoutes.GetFileList}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}&rootDirectory={Uri.EscapeDataString(rootDirectory)}"
        + TiaRestClient.RepeatedQuery("blockName", blockNames);

      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_put_dir"), Description(
    "Imports all block files of a directory, like tia_plc_blocks_put_file for every file, in a single TIA session. " +
    "Reads <rootDirectory>\\<project name>\\<path>\\ and, with recursive (default true), all subdirectories; " +
    "every <Name>.s7dcl (+ optional <Name>.s7res) or <Name>.xml whose content is a block is imported as block <Name> " +
    "into the project folder that matches its directory (subdirectory names = folder names; missing folders are created, existing blocks in other folders are moved). " +
    "Files of UDTs and tag tables, and files whose content is not recognized, are listed in Skipped. " +
    "If <Name>.s7dcl and <Name>.xml both exist, format selects one; without format that block is reported in Errors. " +
    "Existing blocks keep their block number. Instances of project library types are written as in-test version of their type " +
    "(a released version is opened for editing as a new in-test version, an existing in-test version is overwritten; nothing is released; " +
    "use tia_plc_blocks_discard_type_version to throw an in-test version away). " +
    "Blocks whose import fails in TIA are retried after the others, so dependencies between the files do not depend on the file order. " +
    "Nothing is compiled; use tia_plc_compile_plc afterwards. The files are not changed or deleted. " +
    "Each item: same fields as tia_plc_blocks_put_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> PutBlockDir(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem path on the TIA Portal/REST machine used as import base, e.g. \"C:\\\\TiaFiles\"; files are read from <rootDirectory>\\<project name>\\<TIA folder path>")] string rootDirectory,
    [Description("Optional TIA folder path (not filesystem): folder names from the PLC root, e.g. [\"TopCtrl_Func\", \"Sequenz\"]; selects start filesystem directory under rootDirectory and matching TIA project folder; omit for root")] string[]? path = null,
    [Description("Include subdirectories (default true)")] bool recursive = true,
    [Description("Only needed if <Name>.s7dcl and <Name>.xml both exist: \"SimaticData/SD\" or \"SimaticML/XML\"")] string? format = null) {
    var activity = ToolActivity.Start<BlockTools>("Bausteine eines Verzeichnisses werden importiert");
    try {
      string route = $"{BlockRoutes.PutDir}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch.PutDirRequest {
        RootDirectory = rootDirectory,
        Path = path,
        Recursive = recursive,
        Format = format
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_put_file_list"), Description(
    "Imports several PLC blocks from files, exactly like tia_plc_blocks_put_file for each entry in items, in a single TIA session. " +
    "Each entry has BlockName and optional Path, Format and BlockNumber with the same meaning as in tia_plc_blocks_put_file " +
    "(files are read from <rootDirectory>\\<project name>\\<Path or current folder of the block>\\). " +
    "The library options apply to every instance of a library type in the list; without libraryMode such blocks fail with StatusCode 409. " +
    LibraryWriteDescription +
    "Blocks whose import fails in TIA are retried after the others, so the order of the entries does not matter for dependencies. " +
    "Nothing is compiled. The files are not changed or deleted. " +
    "Each item: same fields as tia_plc_blocks_put_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> PutBlockFileList(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("Absolute filesystem path on the TIA Portal/REST machine used as import base, e.g. \"C:\\\\TiaFiles\"; files under <rootDirectory>\\<project name>\\<TIA folder path>")] string rootDirectory,
    [Description("Blocks to import: BlockName (required), Path (optional TIA folder names, not filesystem; selects subdirectory under rootDirectory and TIA target folder), Format (optional), BlockNumber (optional)")] PutFileListItem[] items,
    [Description("Only for instances of library types: \"InTest\" or \"Release\"; omit for normal blocks")] string? libraryMode = null,
    [Description("Only with libraryMode \"Release\": version number to release, e.g. \"0.1.37\"")] string? libraryVersion = null,
    [Description("Only with libraryMode \"Release\": author of the released versions")] string? libraryAuthor = null,
    [Description("Only with libraryMode \"Release\": comment of the released versions")] string? libraryComment = null) {
    if (items == null || items.Length == 0) {
      return TiaRestClient.FromValidationError("Error: items must contain at least one block.");
    }
    var activity = ToolActivity.Start<BlockTools>($"{items.Length} Bausteine werden aus Dateien importiert");
    try {
      string route = $"{BlockRoutes.PutFileList}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutFileListRequest {
        RootDirectory = rootDirectory,
        Items = items.ToList(),
        LibraryMode = libraryMode,
        LibraryVersion = libraryVersion,
        LibraryAuthor = libraryAuthor,
        LibraryComment = libraryComment
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_discard_type_version"), Description(
    "Discards the in-test version of the project library type a PLC block is an instance of (e.g. after tia_plc_blocks_put/tia_plc_blocks_put_file with libraryMode \"InTest\"). " +
    "The instances fall back to the last released version; all changes of the in-test version are lost. " +
    "Fails with StatusCode 400 if the block is not a library type instance and 409 if its version is already released. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, BlockName, BlockNumber, BlockType, ProgrammingLanguage, Path, " +
    LibraryInfoDescription + " after discarding, " +
    "DiscardedVersion (version number of the discarded in-test version).")]
  public static async Task<CallToolResult> DiscardBlockTypeVersion(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the block (instance of the library type)")] string blockName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName) {
    var activity = ToolActivity.Start<BlockTools>($"In-Test-Version von {blockName} wird verworfen");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.DiscardTypeVersion}?processId={processId}&projectName={safeProjectName}&blockName={Uri.EscapeDataString(blockName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      using var httpContent = new StringContent("", Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PostAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_patch"), Description(
    "Changes only the start values of variables in a data block (GlobalDB, InstanceDB, ArrayDB); the block structure stays unchanged. " +
    "Member is the variable path inside the data block: structs/UDTs with dots, array elements with brackets, nesting allowed, " +
    "e.g. \"Speed\", \"Motor.Scale.Min\", \"Motor.Diag[2]\", \"Matrix[1,2]\" or \"Motor.Params[1].Scale.Mins[1].Value\". " +
    "Value is the start value in TIA notation, e.g. \"100\", \"TRUE\", \"16#FF\", \"T#5S\", \"'text'\". " +
    "If any member is not found, nothing is changed. The block is not compiled. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, BlockName, BlockNumber, BlockType, " +
    "Changes (array of objects with Member, OldValue, NewValue and Error; Error is null if the value was set). " +
    "If some values could not be set, IsSuccess is false, Error is a short summary and Data still contains the same JSON object.")]
  public static async Task<CallToolResult> PatchBlockStartValues(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the data block")] string blockName,
    [Description("PLC station / device name (DeviceName from tia_plc_devices_list)")] string deviceName,
    [Description("CPU / device item name (DeviceItemName from tia_plc_devices_list)")] string deviceItemName,
    [Description("New start values: list of objects with Member (variable path) and Value (start value)")] StartValueUpdate[] startValues) {
    var activity = ToolActivity.Start<BlockTools>($"Startwerte in {blockName} werden angepasst");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.Patch}?processId={processId}&projectName={safeProjectName}&blockName={Uri.EscapeDataString(blockName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PatchRequest {
        StartValues = startValues.ToList()
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PatchAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_blocks_list_export_capabilities"), Description(
    "Probes export support per ProgrammingLanguage found in a TIA project. " +
    "Walks all blocks, picks one non-know-how-protected sample per ProgrammingLanguage, " +
    "and tries ExportAsDocuments (SIMATIC SD) and Simatic ML XML export. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array of objects with: " +
    "ProgrammingLanguage, SampleBlockName, SampleBlockType, DeviceName, DeviceItemName, PlcName, " +
    "DocumentsSupported (bool), DocumentsExtensions (e.g. [\".s7dcl\",\".s7res\"]), DocumentsError, " +
    "XmlSupported (bool), XmlExtension (typically \".xml\"), XmlError.")]
  public static async Task<CallToolResult> ListBlockExportCapabilities(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName) {
    var activity = ToolActivity.Start<BlockTools>("Export-Fähigkeiten werden geprüft");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.ListExportCapabilities}?processId={processId}&projectName={safeProjectName}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
