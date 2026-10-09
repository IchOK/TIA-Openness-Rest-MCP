using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.PLC.UDT;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.PLC;

[McpServerToolType]
public class UDTTools {
  [McpServerTool, Description(
    "Returns a list of all PLC user-defined types (UDTs) of a specific TIA project. " +
    "Optionally restrict the result to a type folder with path (folder names from the type group root, e.g. [\"TopCtrl_Func\", \"Basis\"]); " +
    "UDTs in that folder and all its subfolders are returned, PLCs without that folder are omitted, and an error is returned if no PLC has it. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array with one object per PLC: " +
    "DeviceName (device / station name, required for GetUdt), " +
    "DeviceItemName (CPU / device item name, required for GetUdt), " +
    "PlcName (PLC software name), " +
    "Udts (array of the PLC's UDTs, each with: " +
    "UdtName (UDT name, required for GetUdt), " +
    "Path (string array of user group/folder names from the type group root to the UDT; empty if the UDT is in the root)).")]
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

  [McpServerTool, Description(
    "Returns the structure/content of a PLC user-defined type (UDT). " +
    "Use DeviceName, DeviceItemName and UdtName from ListUdts. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName (device / station name), " +
    "DeviceItemName (CPU / device item name), " +
    "PlcName (PLC software name), " +
    "UdtName (UDT name), " +
    "Path (string array of user group/folder names from the type group root to the UDT; empty if the UDT is in the root), " +
    "Format (always \"SimaticData/SD\"), " +
    "Content (full UDT text in SIMATIC SD / .s7dcl form: type declaration and members), " +
    "MultiLingualText (content of the .s7res file with the multilingual member comments. " +
    "Content references them by ID, e.g. { S7_MLC := \"MLC_3SC\" }, and MultiLingualText holds the texts per ID and language " +
    "(YAML: \"MultiLingualTexts:\" followed by entries \"- id: MLC_3SC\" and \"de-DE: <text>\"); null if the UDT has no such texts). " +
    "To write the UDT back with PutUdt, pass Format and MultiLingualText exactly as returned here together with the (modified) Content.")]
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

  [McpServerTool, Description(
    "Creates a new PLC user-defined type (UDT) or overwrites an existing UDT with the same name. " +
    "Content, Format and MultiLingualText correspond to the fields of the same name returned by GetUdt. " +
    "Typical workflow: GetUdt, modify Content (and MultiLingualText if needed), then PutUdt with the same Format and MultiLingualText. " +
    "Format \"SimaticData/SD\": Content is the .s7dcl text; multilingual comments are referenced by ID, e.g. { S7_MLC := \"MLC_3SC\" }, " +
    "and MultiLingualText (the .s7res text from GetUdt) must contain an entry \"- id: <ID>\" with the texts per language for every referenced ID. " +
    "When adding a new comment, add a new unique ID in Content and a matching entry in MultiLingualText. " +
    "MultiLingualText may be null only if Content references no IDs. " +
    "Format \"SimaticML/XML\": Content is a SimaticML XML export of the UDT including all texts; MultiLingualText must be null. " +
    "Mismatches (e.g. XML content with Format SimaticData/SD, or missing text IDs) are rejected before anything is changed. " +
    "Path is only used when a new UDT is created (user group names from the type group root, missing folders are created; omit for the root folder). " +
    "An existing UDT is overwritten in its current folder and never moved, because moving would break its usages; Messages notes a differing Path. " +
    "Changing a UDT makes blocks that use it inconsistent until they are compiled. " +
    "Know-how-protected UDTs cannot be overwritten. Nothing is compiled. " +
    "There is no patch function for UDTs: Openness offers no member-level access to UDTs, so change members via GetUdt/PutUdt. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName, UdtName, " +
    "Path (folder of the UDT after the import), " +
    "Created (true = new UDT, false = existing UDT overwritten), " +
    "Format (format used for the import), " +
    "Messages (notes and messages reported by TIA Portal during the import).")]
  public static async Task<CallToolResult> PutUdt(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the UDT")] string udtName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("Complete UDT text in the given format (Content from GetUdt, modified as needed)")] string content,
    [Description("MultiLingualText from GetUdt (.s7res text with the texts for all S7_MLC IDs in Content); required for \"SimaticData/SD\" if Content references IDs, otherwise null; must be null for \"SimaticML/XML\"")] string? multiLingualText,
    [Description("Format from GetUdt: \"SimaticData/SD\" or \"SimaticML/XML\"")] string format,
    [Description("Optional target folder for a new UDT as user group names from the root, e.g. [\"Plant\", \"Unit1\"]; ignored for existing UDTs")] string[]? path = null) {
    if (string.IsNullOrWhiteSpace(format)) {
      return TiaRestClient.FromValidationError("Error: format is required: \"SimaticData/SD\" or \"SimaticML/XML\" (use Format from GetUdt).");
    }
    var activity = ToolActivity.Start<UDTTools>($"Datentyp {udtName} wird geschrieben");
    try {
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{UDTRoutes.Put}?processId={processId}&projectName={safeProjectName}&udtName={Uri.EscapeDataString(udtName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

      var body = new PutRequest {
        Path = path,
        Format = format,
        Content = content,
        MultiLingualText = multiLingualText
      };
      using var httpContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
      using HttpResponseMessage response = await TiaRestClient.Client.PutAsync(route, httpContent);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
