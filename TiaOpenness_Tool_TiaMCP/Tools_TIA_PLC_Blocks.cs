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
    "Instances of project library types (Library is set in ListBlocks/GetBlock) cannot be overwritten by a plain import: " +
    "without libraryMode the call is rejected with StatusCode 409 and nothing is changed. " +
    "libraryMode \"InTest\" writes the content into the library type as in-test version: a released version is first opened for editing " +
    "(this block becomes the test instance), an existing in-test version is overwritten; the block must stay in its folder. " +
    "libraryMode \"Release\" additionally releases that version (optional libraryVersion e.g. \"0.1.37\", default = number of the in-test version; " +
    "libraryAuthor, libraryComment); dependent types are not released automatically, and a failed release leaves the content in the in-test version. " +
    "Use DiscardBlockTypeVersion to throw away an in-test version. Other instances of the type in the project are not updated. ";

  [McpServerTool, Description(
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
    "DeviceName (device / station name, required for GetBlock), " +
    "DeviceItemName (CPU / device item name, required for GetBlock), " +
    "PlcName (PLC software name), " +
    "Blocks (array of the PLC's blocks, each with: " +
    "BlockName (block name, required for GetBlock), " +
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
    [Description("Optional block folder as folder names from the root, e.g. [\"TopCtrl_Func\", \"Basis\"]; includes subfolders")] string[]? path = null) {

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

  [McpServerTool, Description(
    "Returns the content of a PLC block (FC, FB, OB, GlobalDB, InstanceDB, …). " +
    "Use DeviceName, DeviceItemName and BlockName from ListBlocks. " +
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
    "Format (\"SimaticData/SD\" for LAD, FBD, SCL and DB incl. F-variants, or \"SimaticML/XML\" for all other languages such as STL or GRAPH), " +
    "Content (block text: SIMATIC SD (.s7dcl) or Simatic ML XML with interface, attributes and program code where applicable), " +
    "MultiLingualText (only for Format \"SimaticData/SD\": content of the .s7res file with the multilingual comments and texts. " +
    "Content references them by ID, e.g. { S7_MLC := \"MLC_3SC\" }, and MultiLingualText holds the texts per ID and language " +
    "(YAML: \"MultiLingualTexts:\" followed by entries \"- id: MLC_3SC\" and \"de-DE: <text>\"). " +
    "null for \"SimaticML/XML\" (texts are inside the XML) or if the block has no such texts). " +
    "To write the block back with PutBlock, pass Format and MultiLingualText exactly as returned here together with the (modified) Content.")]
  public static async Task<CallToolResult> GetBlock(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the block")] string blockName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
    var activity = ToolActivity.Start<BlockTools>($"Baustein {blockName} wird abgerufen");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.Get}?processId={processId}&projectName={safeProjectName}&blockName={Uri.EscapeDataString(blockName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool, Description(
    "Creates a new PLC block or overwrites an existing block with the same name (FC, FB, OB, GlobalDB, …). " +
    "Content, Format and MultiLingualText correspond to the fields of the same name returned by GetBlock. " +
    "Typical workflow: GetBlock, modify Content (and MultiLingualText if needed), then PutBlock with the same Format and MultiLingualText. " +
    "Format \"SimaticData/SD\": Content is the .s7dcl text; multilingual comments are referenced by ID, e.g. { S7_MLC := \"MLC_3SC\" }, " +
    "and MultiLingualText (the .s7res text from GetBlock) must contain an entry \"- id: <ID>\" with the texts per language for every referenced ID. " +
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
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Complete block text in the given format (Content from GetBlock, modified as needed)")] string content,
    [Description("MultiLingualText from GetBlock (.s7res text with the texts for all S7_MLC IDs in Content); required for \"SimaticData/SD\" if Content references IDs, otherwise null; must be null for \"SimaticML/XML\"")] string? multiLingualText,
    [Description("Format from GetBlock: \"SimaticData/SD\" or \"SimaticML/XML\"")] string format,
    [Description("Optional target folder as user group names from the root, e.g. [\"Plant\", \"Unit1\"]; empty array = root folder")] string[]? path = null,
    [Description("Optional block number; omit to keep the number of an existing block or to let TIA assign one for a new block")] int? blockNumber = null,
    [Description("Only for instances of library types: \"InTest\" (write as in-test version) or \"Release\" (write and release); omit for normal blocks")] string? libraryMode = null,
    [Description("Only with libraryMode \"Release\": version number to release, e.g. \"0.1.37\"; omit to use the number of the in-test version")] string? libraryVersion = null,
    [Description("Only with libraryMode \"Release\": author of the released version; omit to keep the author of the version")] string? libraryAuthor = null,
    [Description("Only with libraryMode \"Release\": comment of the released version")] string? libraryComment = null) {
    if (string.IsNullOrWhiteSpace(format)) {
      return TiaRestClient.FromValidationError("Error: format is required: \"SimaticData/SD\" or \"SimaticML/XML\" (use Format from GetBlock).");
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

  [McpServerTool, Description(
    "Exports a PLC block to files instead of returning its content (same block selection as GetBlock). " +
    "The files are written to <rootDirectory>\\<project name>\\<block folder path>\\, mirroring the block folders of the project " +
    "(invalid file name characters in project and folder names are replaced by '_'); missing directories are created. " +
    "Files: <BlockName>.s7dcl plus <BlockName>.s7res (if the block has multilingual texts) for Format \"SimaticData/SD\" (LAD, FBD, SCL, DB incl. F-variants), " +
    "or <BlockName>.xml for Format \"SimaticML/XML\" (all other languages such as STL or GRAPH). " +
    "Existing .s7dcl/.s7res/.xml files of the block in that directory are replaced. " +
    "rootDirectory is a path on the machine running TIA Portal and the REST API. " +
    "Use PutBlockFile with the same rootDirectory to import the (edited) files again. " +
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
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Absolute root directory for the export, e.g. \"C:\\\\TiaFiles\"; files go to <rootDirectory>\\<project name>\\<block folder path>")] string rootDirectory) {
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

  [McpServerTool, Description(
    "Creates a new PLC block or overwrites an existing block with the same name from files (same behavior as PutBlock, but without passing the content). " +
    "The files are read from <rootDirectory>\\<project name>\\<folder path>\\, the layout written by GetBlockFile; " +
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
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Absolute root directory of the files, e.g. \"C:\\\\TiaFiles\"; files are read from <rootDirectory>\\<project name>\\<folder path>")] string rootDirectory,
    [Description("Optional \"SimaticData/SD\" or \"SimaticML/XML\"; omit to derive it from the existing files")] string? format = null,
    [Description("Optional folder as user group names from the root, e.g. [\"Plant\", \"Unit1\"]; selects both the file subdirectory and the target folder in the project; empty array = root folder")] string[]? path = null,
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

  [McpServerTool, Description(
    "Discards the in-test version of the project library type a PLC block is an instance of (e.g. after PutBlock/PutBlockFile with libraryMode \"InTest\"). " +
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
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
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

  [McpServerTool, Description(
    "Changes only the start values of variables in a data block (GlobalDB, InstanceDB, ArrayDB); the block structure stays unchanged. " +
    "Member is the variable path inside the data block: structs/UDTs with dots, array elements with brackets, nesting allowed, " +
    "e.g. \"Speed\", \"Motor.Scale.Min\", \"Motor.Diag[2]\", \"Matrix[1,2]\" or \"Motor.Params[1].Scale.Mins[1].Value\". " +
    "Value is the start value in TIA notation, e.g. \"100\", \"TRUE\", \"16#FF\", \"T#5S\", \"'text'\". " +
    "If any member is not found, nothing is changed. The block is not compiled. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, BlockName, BlockNumber, BlockType, " +
    "Changes (array of objects with Member, OldValue, NewValue and Error; Error is null if the value was set). " +
    "If some values could not be set, IsSuccess is false and Error contains the same JSON object.")]
  public static async Task<CallToolResult> PatchBlockStartValues(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the data block")] string blockName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
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

  [McpServerTool, Description(
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
      string route = $"{BlockRoutes.ExportCapabilities}?processId={processId}&projectName={safeProjectName}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
