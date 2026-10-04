using ModelContextProtocol.Protocol;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

public static class TiaRestClient {
  private static readonly HttpClient _client = new HttpClient {};
  private static readonly JsonSerializerOptions _resultJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) {
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  /// <summary>
  /// Konfiguriert den zentralen HttpClient einmalig beim Start.
  /// </summary>
  public static void Initialize(string apiKey, string restPort) {
    _client.BaseAddress = new Uri($"http://localhost:{restPort}");
    _client.DefaultRequestHeaders.Remove("X-API-Key");
    _client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
  }

  public static HttpClient Client => _client;

  /// <summary>
  /// Builds the folder filter query part ("&amp;path=A&amp;path=B"); empty if no folder is given.
  /// </summary>
  public static string PathQuery(string[]? path) {
    if (path == null) {
      return "";
    }
    return string.Concat(path.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => "&path=" + Uri.EscapeDataString(p.Trim())));
  }

  /// <summary>
  /// Maps a REST response to a tool result with McpApiResponse as JSON content.
  /// IsError is set when the REST call failed, so MCP clients see the failure without parsing the content.
  /// Error bodies are plain text from TiaREST and must not be JSON-deserialized
  /// (that produced "'E' is an invalid start of a value").
  /// </summary>
  public static async Task<CallToolResult> FromHttpResponseAsync(HttpResponseMessage response) {
    string body = await response.Content.ReadAsStringAsync();
    int statusCode = (int)response.StatusCode;

    if (!response.IsSuccessStatusCode) {
      return ToToolResult(new McpApiResponse {
        StatusCode = statusCode,
        IsSuccess = false,
        Error = string.IsNullOrWhiteSpace(body)
          ? (response.ReasonPhrase ?? ("HTTP " + statusCode))
          : body.Trim()
      });
    }

    if (string.IsNullOrWhiteSpace(body)) {
      return ToToolResult(new McpApiResponse {
        StatusCode = statusCode,
        IsSuccess = true
      });
    }

    try {
      return ToToolResult(new McpApiResponse {
        StatusCode = statusCode,
        IsSuccess = true,
        Data = JsonSerializer.Deserialize<JsonElement>(body)
      });
    } catch (JsonException) {
      return ToToolResult(new McpApiResponse {
        StatusCode = statusCode,
        IsSuccess = false,
        Error = body.Trim()
      });
    }
  }

  public static CallToolResult FromException(Exception ex) {
    if (ex is HttpRequestException) {
      return ToToolResult(new McpApiResponse {
        StatusCode = (int)HttpStatusCode.BadGateway,
        IsSuccess = false,
        Error = "The TIA Openness REST API is not running or is not reachable."
      });
    }
    return ToToolResult(new McpApiResponse {
      StatusCode = (int)HttpStatusCode.InternalServerError,
      IsSuccess = false,
      Error = "An unexpected error occurred: " + ex.Message
    });
  }

  /// <summary>
  /// Tool result for invalid tool arguments that are rejected before calling the REST API.
  /// </summary>
  public static CallToolResult FromValidationError(string message) {
    return ToToolResult(new McpApiResponse {
      StatusCode = (int)HttpStatusCode.BadRequest,
      IsSuccess = false,
      Error = message
    });
  }

  private static CallToolResult ToToolResult(McpApiResponse apiResponse) {
    return new CallToolResult {
      Content = [new TextContentBlock { Text = JsonSerializer.Serialize(apiResponse, _resultJsonOptions) }],
      IsError = !apiResponse.IsSuccess
    };
  }
}
