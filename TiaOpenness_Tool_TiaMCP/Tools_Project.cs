using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;
using System.Text.Json;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class ProjectTools {
  [McpServerTool, Description(
    "Returns a list of all currently open TIA Portal projects and their Process IDs. " +
    "Call this first to obtain processId and projectName for other tools. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array of objects with: " +
    "ProcessId (TIA Portal process ID; pass as processId to other tools), " +
    "Name (project name; pass as projectName to other tools), " +
    "Path (full file system path of the project).")]
  public static async Task<McpApiResponse> ListProjects() {
    try {
      Console.WriteLine("TIA Portal Projekte werden abgerufen...");
      string route = $"{ProjectRoutes.List}";

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