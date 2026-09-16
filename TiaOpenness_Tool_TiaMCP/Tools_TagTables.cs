using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class TagTableTools {
  [McpServerTool, Description(
    "Returns a list of all PLC tag tables of a specific TIA project. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array of objects with: " +
    "DeviceName (device / station name, required for GetTagTable), " +
    "DeviceItemName (CPU / device item name, required for GetTagTable), " +
    "PlcName (PLC software name), " +
    "TagTableName (tag table name, required for GetTagTable), " +
    "IsDefault (true if this is the PLC's default tag table).")]
  public static async Task<McpApiResponse> ListTagTables(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName) {
    try {
      Console.WriteLine("Variablentabellen werden abgerufen...");
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{TagTableRoutes.List}?processId={processId}&projectName={safeProjectName}";

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
    "Returns tags, user constants and system constants of a PLC tag table. " +
    "Use DeviceName, DeviceItemName and TagTableName from ListTagTables. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: " +
    "DeviceName (device / station name), " +
    "DeviceItemName (CPU / device item name), " +
    "PlcName (PLC software name), " +
    "TagTableName (tag table name), " +
    "IsDefault (true if this is the PLC's default tag table), " +
    "Tags (array of { Name, DataTypeName, LogicalAddress } — PLC tags/variables with data type and absolute address if assigned), " +
    "UserConstants (array of { Name, DataTypeName, Value } — user-defined constants), " +
    "SystemConstants (array of { Name, DataTypeName, Value } — system constants of the table).")]
  public static async Task<McpApiResponse> GetTagTable(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the tag table")] string tagTableName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
    try {
      Console.WriteLine("Variablentabelle wird abgerufen...");
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{TagTableRoutes.Get}?processId={processId}&projectName={safeProjectName}&tagTableName={Uri.EscapeDataString(tagTableName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

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
