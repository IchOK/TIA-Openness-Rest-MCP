using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class UDTTools {
  [McpServerTool, Description(
    "Returns a list of all PLC user-defined types (UDTs) of a specific TIA project. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array of objects with: " +
    "DeviceName (device / station name, required for GetUdt), " +
    "DeviceItemName (CPU / device item name, required for GetUdt), " +
    "PlcName (PLC software name), " +
    "UdtName (UDT name, required for GetUdt).")]
  public static async Task<McpApiResponse> ListUdts(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName) {
    try {
      Console.WriteLine("Datenbausteine werden abgerufen...");
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{UDTRoutes.List}?processId={processId}&projectName={safeProjectName}";

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

  [McpServerTool, Description(
    "Returns the structure/content of a PLC user-defined type (UDT). " +
    "Use DeviceName, DeviceItemName and UdtName from ListUdts. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName (device / station name), " +
    "DeviceItemName (CPU / device item name), " +
    "PlcName (PLC software name), " +
    "UdtName (UDT name), " +
    "Format (export format; typically \"SimaticData/SD\"), " +
    "Content (full UDT text in SIMATIC SD / .s7dcl form: type declaration and members).")]
  public static async Task<McpApiResponse> GetUdt(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the UDT")] string udtName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
    try {
      Console.WriteLine("Datenbaustein wird abgerufen...");
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{UDTRoutes.Get}?processId={processId}&projectName={safeProjectName}&udtName={Uri.EscapeDataString(udtName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

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