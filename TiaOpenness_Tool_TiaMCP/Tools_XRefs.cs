using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class XRefTools {
  [McpServerTool, Description(
    "Returns cross-references for a PLC object (where it is used / referenced). " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName, DeviceItemName, PlcName (context of the query), " +
    "ObjectName (queried object name), " +
    "ObjectKind (Block, Tag, Udt, or SystemConstant), " +
    "Filter (applied CrossReferenceFilter value), " +
    "Sources (array of source objects; each has Name, Path, Address, Device, TypeName, " +
    "References (array of referenced objects with Name, Path, Address, Device, TypeName, " +
    "and Locations: array of { Name, Address, TypeName, Access, ReferenceType, ReferenceLocation, ReferencedAsName } describing each usage site), " +
    "and Children (nested SourceInfo nodes for hierarchical results).")]
  public static async Task<McpApiResponse> GetCrossReferences(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("The name of the object to look up (block, tag, UDT, or system constant name)")] string objectName,
    [Description("The kind of object. Allowed values: Block (FC/FB/OB/DB), Tag (PLC tag), Udt (user-defined type), SystemConstant")] string objectKind,
    [Description("Optional CrossReferenceFilter. Allowed values: AllObjects (default), ObjectsWithReferences, ObjectsWithoutReferences, UnusedObjects")] string filter = "AllObjects") {
    try {
      Console.WriteLine("Querverweise werden abgerufen...");
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{XRefRoutes.Get}?processId={processId}&projectName={safeProjectName}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}&objectName={Uri.EscapeDataString(objectName)}&objectKind={Uri.EscapeDataString(objectKind)}&filter={Uri.EscapeDataString(filter ?? "AllObjects")}";

      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      string responseBody = await response.Content.ReadAsStringAsync();
      return new McpApiResponse {
        StatusCode = (int)response.StatusCode,
        IsSuccess = response.IsSuccessStatusCode,
        Data = JsonSerializer.Deserialize<JsonElement>(responseBody)
      };
    } catch (HttpRequestException) {
      return new McpApiResponse {
        StatusCode = (int)HttpStatusCode.BadGateway,
        IsSuccess = false,
        Error = "The TIA Openness REST API is not running or is not reachable."
      };
    } catch (Exception ex) {
      return new McpApiResponse {
        StatusCode = (int)HttpStatusCode.InternalServerError,
        IsSuccess = false,
        Error = $"An unexpected error occurred: {ex.Message}"
      };
    }
  }
}
