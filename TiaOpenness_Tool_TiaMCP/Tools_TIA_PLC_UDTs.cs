using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch;
using Tophinke.TiaOpenness.Tool.Types.PLC.UDT;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.PLC;

[McpServerToolType]
public class UDTTools {
  private const string LibraryInfoDescription =
    "Library (only for instances of a project library type, otherwise omitted/null: TypeName, Version e.g. \"0.1.36\", " +
    "State \"Committed\" = released and write-protected or \"InWork\" = in-test version being edited)";

  private const string LibraryWriteDescription =
    "Instances of project library types (Library is set in tia_plc_udts_list/tia_plc_udts_get) cannot be overwritten by a plain import: " +
    "without libraryMode the call is rejected with StatusCode 409 and nothing is changed. " +
    "libraryMode \"InTest\" writes the content into the library type as in-test version: a released version is first opened for editing " +
    "(this UDT becomes the test instance), an existing in-test version is overwritten. " +
    "libraryMode \"Release\" additionally releases that version (optional libraryVersion e.g. \"0.1.37\", default = number of the in-test version; " +
    "libraryAuthor, libraryComment); dependent types are not released automatically. " +
    "Use tia_plc_udts_discard_type_version to throw away an in-test version. Other instances of the type in the project are not updated. ";

  private const string FileLayoutDescription =
    "Files: <UdtName>.s7dcl plus <UdtName>.s7res (if the UDT has multilingual texts) in <rootDirectory>\\<project name>\\<type folder path>\\, " +
    "mirroring the type folders of the project (invalid file name characters in project and folder names are replaced by '_'). " +
    "Blocks and tag tables exported with the corresponding file tools use the same directory tree; the kinds are told apart by file content. " +
    "rootDirectory is a path on the machine running TIA Portal and the REST API. ";

  [McpServerTool(Name = "tia_plc_udts_list"), Description(
    "Returns a list of all PLC user-defined types (UDTs) of a specific TIA project. " +
    "Optionally restrict the result to a type folder with path (folder names from the type group root, e.g. [\"TopCtrl_Func\", \"Basis\"]); " +
    "UDTs in that folder and all its subfolders are returned, PLCs without that folder are omitted, and an error is returned if no PLC has it. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array with one object per PLC: " +
    "DeviceName (device / station name, required for tia_plc_udts_get), " +
    "DeviceItemName (CPU / device item name, required for tia_plc_udts_get), " +
    "PlcName (PLC software name), " +
    "Udts (array of the PLC's UDTs, each with: " +
    "UdtName (UDT name, required for tia_plc_udts_get), " +
    "Path (string array of user group/folder names from the type group root to the UDT; empty if the UDT is in the root), " +
    LibraryInfoDescription + ").")]
  public static async Task<CallToolResult> ListUdts(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("Optional type folder as folder names from the root, e.g. [\"TopCtrl_Func\", \"Basis\"]; includes subfolders")] string[]? path = null) {
    var activity = ToolActivity.Start<UDTTools>("Datentypen werden abgerufen");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{UDTRoutes.List}?processId={processId}&projectName={safeProjectName}" + TiaRestClient.PathQuery(path);

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_udts_get"), Description(
    "Returns the structure/content of a PLC user-defined type (UDT). " +
    "Use DeviceName, DeviceItemName and UdtName from tia_plc_udts_list. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName (device / station name), " +
    "DeviceItemName (CPU / device item name), " +
    "PlcName (PLC software name), " +
    "UdtName (UDT name), " +
    "Path (string array of user group/folder names from the type group root to the UDT; empty if the UDT is in the root), " +
    LibraryInfoDescription + ", " +
    "Format (always \"SimaticData/SD\"), " +
    "Content (full UDT text in SIMATIC SD / .s7dcl form: type declaration and members), " +
    "MultiLingualText (content of the .s7res file with the multilingual member comments. " +
    "Content references them by ID, e.g. { S7_MLC := \"MLC_3SC\" }, and MultiLingualText holds the texts per ID and language " +
    "(YAML: \"MultiLingualTexts:\" followed by entries \"- id: MLC_3SC\" and \"de-DE: <text>\"); null if the UDT has no such texts). " +
    "To write the UDT back with tia_plc_udts_put, pass Format and MultiLingualText exactly as returned here together with the (modified) Content.")]
  public static async Task<CallToolResult> GetUdt(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the UDT")] string udtName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
    var activity = ToolActivity.Start<UDTTools>($"Datentyp {udtName} wird abgerufen");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{UDTRoutes.Get}?processId={processId}&projectName={safeProjectName}&udtName={Uri.EscapeDataString(udtName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_udts_put"), Description(
    "Creates a new PLC user-defined type (UDT) or overwrites an existing UDT with the same name. " +
    "Content, Format and MultiLingualText correspond to the fields of the same name returned by tia_plc_udts_get. " +
    "Typical workflow: tia_plc_udts_get, modify Content (and MultiLingualText if needed), then tia_plc_udts_put with the same Format and MultiLingualText. " +
    "Format \"SimaticData/SD\": Content is the .s7dcl text; multilingual comments are referenced by ID, e.g. { S7_MLC := \"MLC_3SC\" }, " +
    "and MultiLingualText (the .s7res text from tia_plc_udts_get) must contain an entry \"- id: <ID>\" with the texts per language for every referenced ID. " +
    "When adding a new comment, add a new unique ID in Content and a matching entry in MultiLingualText. " +
    "MultiLingualText may be null only if Content references no IDs. " +
    "Format \"SimaticML/XML\": Content is a SimaticML XML export of the UDT including all texts; MultiLingualText must be null. " +
    "Mismatches (e.g. XML content with Format SimaticData/SD, or missing text IDs) are rejected before anything is changed. " +
    "Path is only used when a new UDT is created (user group names from the type group root, missing folders are created; omit for the root folder). " +
    "An existing UDT is overwritten in its current folder and never moved, because moving would break its usages; Messages notes a differing Path. " +
    "Changing a UDT makes blocks that use it inconsistent until they are compiled. " +
    "Know-how-protected UDTs cannot be overwritten. Nothing is compiled. " +
    "There is no patch function for UDTs: Openness offers no member-level access to UDTs, so change members via tia_plc_udts_get/tia_plc_udts_put. " +
    LibraryWriteDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, UdtName, " +
    "Path (folder of the UDT after the import), " +
    LibraryInfoDescription + " after the call, " +
    "Created (true = new UDT, false = existing UDT overwritten), " +
    "Format (format used for the import), " +
    "Messages (notes and messages reported by TIA Portal during the import, incl. the library steps).")]
  public static async Task<CallToolResult> PutUdt(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the UDT")] string udtName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Complete UDT text in the given format (Content from tia_plc_udts_get, modified as needed)")] string content,
    [Description("MultiLingualText from tia_plc_udts_get (.s7res text with the texts for all S7_MLC IDs in Content); required for \"SimaticData/SD\" if Content references IDs, otherwise null; must be null for \"SimaticML/XML\"")] string? multiLingualText,
    [Description("Format from tia_plc_udts_get: \"SimaticData/SD\" or \"SimaticML/XML\"")] string format,
    [Description("Optional target folder for a new UDT as user group names from the root, e.g. [\"Plant\", \"Unit1\"]; ignored for existing UDTs")] string[]? path = null,
    [Description("Only for instances of library types: \"InTest\" (write as in-test version) or \"Release\" (write and release); omit for normal UDTs")] string? libraryMode = null,
    [Description("Only with libraryMode \"Release\": version number to release, e.g. \"0.1.37\"; omit to use the number of the in-test version")] string? libraryVersion = null,
    [Description("Only with libraryMode \"Release\": author of the released version; omit to keep the author of the version")] string? libraryAuthor = null,
    [Description("Only with libraryMode \"Release\": comment of the released version")] string? libraryComment = null) {
    if (string.IsNullOrWhiteSpace(format)) {
      return TiaRestClient.FromValidationError("Error: format is required: \"SimaticData/SD\" or \"SimaticML/XML\" (use Format from tia_plc_udts_get).");
    }
    var activity = ToolActivity.Start<UDTTools>($"Datentyp {udtName} wird geschrieben");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{UDTRoutes.Put}?processId={processId}&projectName={safeProjectName}&udtName={Uri.EscapeDataString(udtName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutRequest {
        Path = path,
        Format = format,
        Content = content,
        MultiLingualText = multiLingualText,
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

  [McpServerTool(Name = "tia_plc_udts_get_file"), Description(
    "Exports a PLC user-defined type (UDT) to files instead of returning its content (same UDT selection as tia_plc_udts_get). " +
    FileLayoutDescription +
    "Existing .s7dcl/.s7res/.xml files of the UDT in that directory are replaced; missing directories are created. " +
    "Use tia_plc_udts_put_file with the same rootDirectory to import the (edited) files again. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: DeviceName, DeviceItemName, PlcName, UdtName, " +
    "Path (type folder path in the project), " + LibraryInfoDescription + ", " +
    "Format (\"SimaticData/SD\"), Directory (directory the files were written to), Files (full paths of the exported files).")]
  public static async Task<CallToolResult> GetUdtFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the UDT")] string udtName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Absolute root directory for the export, e.g. \"C:\\\\TiaFiles\"; files go to <rootDirectory>\\<project name>\\<type folder path>")] string rootDirectory) {
    var activity = ToolActivity.Start<UDTTools>($"Datentyp {udtName} wird in Dateien exportiert");
    try {
      string route = $"{UDTRoutes.GetFile}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&udtName={Uri.EscapeDataString(udtName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}&rootDirectory={Uri.EscapeDataString(rootDirectory)}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_udts_put_file"), Description(
    "Creates a new UDT or overwrites an existing UDT with the same name from files (same behavior as tia_plc_udts_put, but without passing the content). " +
    "The files are read from <rootDirectory>\\<project name>\\<folder path>\\, the layout written by tia_plc_udts_get_file; " +
    "<folder path> is Path if given, otherwise the current folder of an existing UDT, otherwise the root folder. " +
    "Expected files: <UdtName>.s7dcl plus optional <UdtName>.s7res for Format \"SimaticData/SD\", or <UdtName>.xml for Format \"SimaticML/XML\". " +
    "If format is omitted, it is derived from the existing files; if both exist, format is required. " +
    "For SimaticData/SD every text ID referenced in the .s7dcl file needs an entry in the .s7res file. " +
    "A new UDT is created in folder Path (missing folders are created); an existing UDT is overwritten in its current folder and never moved. " +
    "Know-how-protected UDTs cannot be overwritten. Nothing is compiled. The files are not changed or deleted. " +
    LibraryWriteDescription +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: DeviceName, DeviceItemName, PlcName, UdtName, Path (folder after the import), " +
    LibraryInfoDescription + " after the call, Created, Format, Messages, Directory (directory the files were read from), Files (full paths of the imported files).")]
  public static async Task<CallToolResult> PutUdtFile(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the UDT")] string udtName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Absolute root directory of the files, e.g. \"C:\\\\TiaFiles\"; files are read from <rootDirectory>\\<project name>\\<folder path>")] string rootDirectory,
    [Description("Optional \"SimaticData/SD\" or \"SimaticML/XML\"; omit to derive it from the existing files")] string? format = null,
    [Description("Optional folder as user group names from the root, e.g. [\"Plant\", \"Unit1\"]; selects the file subdirectory and the folder for a new UDT")] string[]? path = null,
    [Description("Only for instances of library types: \"InTest\" (write as in-test version) or \"Release\" (write and release); omit for normal UDTs")] string? libraryMode = null,
    [Description("Only with libraryMode \"Release\": version number to release, e.g. \"0.1.37\"; omit to use the number of the in-test version")] string? libraryVersion = null,
    [Description("Only with libraryMode \"Release\": author of the released version; omit to keep the author of the version")] string? libraryAuthor = null,
    [Description("Only with libraryMode \"Release\": comment of the released version")] string? libraryComment = null) {
    var activity = ToolActivity.Start<UDTTools>($"Datentyp {udtName} wird aus Dateien importiert");
    try {
      string route = $"{UDTRoutes.PutFile}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&udtName={Uri.EscapeDataString(udtName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutFileRequest {
        RootDirectory = rootDirectory,
        Path = path,
        Format = format,
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

  [McpServerTool(Name = "tia_plc_udts_get_dir"), Description(
    "Exports all UDTs of a type folder to files, like tia_plc_udts_get_file for every UDT, in a single TIA session. " +
    "path selects the type folder (folder names from the type group root; omit for the root folder, i.e. all UDTs of the PLC), " +
    "recursive (default true) includes all subfolders. " +
    FileLayoutDescription +
    "Existing files of the exported UDTs are replaced; other files are not touched. Know-how-protected UDTs are skipped. " +
    "Use tia_plc_udts_put_dir or tia_plc_udts_put_file_list with the same rootDirectory to import the (edited) files again. " +
    "Each item: same fields as tia_plc_udts_get_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> GetUdtDir(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Absolute root directory for the export, e.g. \"C:\\\\TiaFiles\"")] string rootDirectory,
    [Description("Optional type folder as folder names from the root, e.g. [\"TopCtrl_Func\"]; omit for the root folder")] string[]? path = null,
    [Description("Include subfolders (default true)")] bool recursive = true) {
    var activity = ToolActivity.Start<UDTTools>("Datentypen eines Ordners werden in Dateien exportiert");
    try {
      string route = $"{UDTRoutes.GetDir}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}&rootDirectory={Uri.EscapeDataString(rootDirectory)}&recursive={recursive}"
        + TiaRestClient.PathQuery(path);

      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_udts_get_file_list"), Description(
    "Exports several UDTs to files, exactly like tia_plc_udts_get_file for each name in udtNames, in a single TIA session. " +
    FileLayoutDescription +
    "Each item: same fields as tia_plc_udts_get_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> GetUdtFileList(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Absolute root directory for the export, e.g. \"C:\\\\TiaFiles\"")] string rootDirectory,
    [Description("Names of the UDTs to export")] string[] udtNames) {
    if (udtNames == null || udtNames.All(string.IsNullOrWhiteSpace)) {
      return TiaRestClient.FromValidationError("Error: udtNames must contain at least one UDT name.");
    }
    var activity = ToolActivity.Start<UDTTools>($"{udtNames.Length} Datentypen werden in Dateien exportiert");
    try {
      string route = $"{UDTRoutes.GetFileList}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}&rootDirectory={Uri.EscapeDataString(rootDirectory)}"
        + TiaRestClient.RepeatedQuery("udtName", udtNames);

      using HttpResponseMessage response = await TiaRestClient.LongRunningClient.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }

  [McpServerTool(Name = "tia_plc_udts_put_dir"), Description(
    "Imports all UDT files of a directory, like tia_plc_udts_put_file for every file, in a single TIA session. " +
    "Reads <rootDirectory>\\<project name>\\<path>\\ and, with recursive (default true), all subdirectories; " +
    "every <Name>.s7dcl (+ optional <Name>.s7res) or <Name>.xml whose content is a UDT is imported as UDT <Name>. " +
    "New UDTs are created in the project folder that matches their directory (subdirectory names = folder names, missing folders are created); " +
    "existing UDTs are overwritten in their current folder and never moved. " +
    "Files of blocks and tag tables, and files whose content is not recognized, are listed in Skipped. " +
    "If <Name>.s7dcl and <Name>.xml both exist, format selects one; without format that UDT is reported in Errors. " +
    "Instances of project library types are written as in-test version of their type " +
    "(a released version is opened for editing as a new in-test version, an existing in-test version is overwritten; nothing is released). " +
    "UDTs whose import fails in TIA (e.g. because they use another UDT of the directory that is not imported yet) are retried after the others. " +
    "Nothing is compiled. The files are not changed or deleted. " +
    "Each item: same fields as tia_plc_udts_put_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> PutUdtDir(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Absolute root directory of the files, e.g. \"C:\\\\TiaFiles\"; files are read from <rootDirectory>\\<project name>\\<path>")] string rootDirectory,
    [Description("Optional folder as names from the root; selects the start directory and the matching project folder; omit for the root")] string[]? path = null,
    [Description("Include subdirectories (default true)")] bool recursive = true,
    [Description("Only needed if <Name>.s7dcl and <Name>.xml both exist: \"SimaticData/SD\" or \"SimaticML/XML\"")] string? format = null) {
    var activity = ToolActivity.Start<UDTTools>("Datentypen eines Verzeichnisses werden importiert");
    try {
      string route = $"{UDTRoutes.PutDir}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutDirRequest {
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

  [McpServerTool(Name = "tia_plc_udts_put_file_list"), Description(
    "Imports several UDTs from files, exactly like tia_plc_udts_put_file for each entry in items, in a single TIA session. " +
    "Each entry has UdtName and optional Path and Format with the same meaning as in tia_plc_udts_put_file. " +
    "The library options apply to every instance of a library type in the list; without libraryMode such UDTs fail with StatusCode 409. " +
    LibraryWriteDescription +
    "UDTs whose import fails in TIA are retried after the others, so the order of the entries does not matter for dependencies. " +
    "Nothing is compiled. The files are not changed or deleted. " +
    "Each item: same fields as tia_plc_udts_put_file returns. " +
    TiaRestClient.BatchResultDescription)]
  public static async Task<CallToolResult> PutUdtFileList(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Absolute root directory of the files, e.g. \"C:\\\\TiaFiles\"")] string rootDirectory,
    [Description("UDTs to import: UdtName (required), Path (optional folder names; selects file subdirectory and folder for new UDTs), Format (optional)")] PutFileListItem[] items,
    [Description("Only for instances of library types: \"InTest\" or \"Release\"; omit for normal UDTs")] string? libraryMode = null,
    [Description("Only with libraryMode \"Release\": version number to release, e.g. \"0.1.37\"")] string? libraryVersion = null,
    [Description("Only with libraryMode \"Release\": author of the released versions")] string? libraryAuthor = null,
    [Description("Only with libraryMode \"Release\": comment of the released versions")] string? libraryComment = null) {
    if (items == null || items.Length == 0) {
      return TiaRestClient.FromValidationError("Error: items must contain at least one UDT.");
    }
    var activity = ToolActivity.Start<UDTTools>($"{items.Length} Datentypen werden aus Dateien importiert");
    try {
      string route = $"{UDTRoutes.PutFileList}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

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

  [McpServerTool(Name = "tia_plc_udts_discard_type_version"), Description(
    "Discards the in-test version of the project library type a UDT is an instance of (e.g. after tia_plc_udts_put/tia_plc_udts_put_file/tia_plc_udts_put_dir with libraryMode \"InTest\"). " +
    "The instances fall back to the last released version; all changes of the in-test version are lost. " +
    "Fails with StatusCode 400 if the UDT is not a library type instance and 409 if its version is already released. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: DeviceName, DeviceItemName, PlcName, UdtName, Path, " +
    LibraryInfoDescription + " after discarding, " +
    "DiscardedVersion (version number of the discarded in-test version).")]
  public static async Task<CallToolResult> DiscardUdtTypeVersion(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the UDT (instance of the library type)")] string udtName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
    var activity = ToolActivity.Start<UDTTools>($"In-Test-Version von {udtName} wird verworfen");
    try {
      string route = $"{UDTRoutes.DiscardTypeVersion}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}&udtName={Uri.EscapeDataString(udtName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      using var httpContent = new StringContent("", Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PostAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
