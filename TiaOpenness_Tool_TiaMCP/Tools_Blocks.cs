using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class BlockTools {
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
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array of objects with: " +
    "DeviceName (device / station name, required for GetBlock), " +
    "DeviceItemName (CPU / device item name, required for GetBlock), " +
    "PlcName (PLC software name), " +
    "BlockName (block name, required for GetBlock), " +
    "BlockNumber (block number in the PLC), " +
    "BlockType (Openness type name such as FC, FB, OB, GlobalDB, …), " +
    "Path (string array of user group/folder names from the block group root to the block; empty if the block is in the root).")]
  public static async Task<McpApiResponse> ListBlocks(
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
      "Example: FC,FB,GlobalDB")] string? filterTypes = null) {

    try {
      Console.WriteLine("Bausteine werden abgerufen...");
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.List}?processId={processId}&projectName={safeProjectName}";
      if (!string.IsNullOrWhiteSpace(filterTypes)) {
        route += $"&filterTypes={Uri.EscapeDataString(filterTypes)}";
      }

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
    "Format (export format; typically \"SimaticData/SD\"), " +
    "Content (full block text in SIMATIC SD / .s7dcl form: interface, attributes and program code where applicable).")]
  public static async Task<McpApiResponse> GetBlock(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName,
    [Description("The name of the block")] string blockName,
    [Description("The name of the Device")] string deviceName,
    [Description("The name of the Device Item")] string deviceItemName) {
    try {
      Console.WriteLine("Baustein wird abgerufen...");
      string safeProjectName = Uri.EscapeDataString(projectName);
      string route = $"{BlockRoutes.Get}?processId={processId}&projectName={safeProjectName}&blockName={Uri.EscapeDataString(blockName)}&deviceName={Uri.EscapeDataString(deviceName)}&deviceItemName={Uri.EscapeDataString(deviceItemName)}";

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
