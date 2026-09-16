using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class XRefTools {
  [McpServerTool, Description("Returns cross-references for a PLC object (Block, Tag, Udt, or SystemConstant).")]
  public static async Task<McpApiResponse> GetCrossReferences(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName,
    [Description("The name of the object")] string objectName,
    [Description("The kind of object: Block, Tag, Udt, or SystemConstant")] string objectKind,
    [Description("Optional filter: AllObjects, ObjectsWithReferences, ObjectsWithoutReferences, UnusedObjects")] string filter = "AllObjects") {
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
