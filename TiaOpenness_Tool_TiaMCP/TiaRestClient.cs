using ModelContextProtocol.Protocol;
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

public static class TiaRestClient {
  private static readonly HttpClient _client = new HttpClient();
  private static readonly HttpClient _longRunningClient = new HttpClient();
  private static readonly JsonSerializerOptions _resultJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) {
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
  };

  /// <summary>
  /// Konfiguriert den zentralen HttpClient einmalig beim Start.
  /// </summary>
  public static void Initialize(string apiKey, string restHost, int restPort, int timeoutSeconds, int longRunningTimeoutMinutes) {
    _client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    _longRunningClient.Timeout = TimeSpan.FromMinutes(longRunningTimeoutMinutes);
    foreach (HttpClient client in new[] { _client, _longRunningClient }) {
      client.BaseAddress = new Uri($"http://{restHost}:{restPort}");
      client.DefaultRequestHeaders.Remove("X-API-Key");
      client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }
  }

  public static HttpClient Client => _client;

  /// <summary>
  /// Client for calls that can take several minutes, e.g. compiling a whole PLC.
  /// </summary>
  public static HttpClient LongRunningClient => _longRunningClient;

  /// <summary>
  /// Optional project kind query ("&amp;kind=LocalSession" / Project); empty if omitted.
  /// </summary>
  public static string KindQuery(string? kind) {
    return string.IsNullOrWhiteSpace(kind) ? "" : $"&kind={Uri.EscapeDataString(kind.Trim())}";
  }

  /// <summary>
  /// Builds the folder filter query part ("&amp;path=A&amp;path=B"); empty if no folder is given.
  /// </summary>
  public static string PathQuery(string[]? path) {
    return RepeatedQuery("path", path);
  }

  /// <summary>
  /// Builds a query part with one parameter per value ("&amp;name=A&amp;name=B"); empty if no value is given.
  /// </summary>
  public static string RepeatedQuery(string name, string[]? values) {
    if (values == null) {
      return "";
    }
    return string.Concat(values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => $"&{name}=" + Uri.EscapeDataString(v.Trim())));
  }

  /// <summary>
  /// Description of the result of batch tools (GetXxxDir, PutXxxDir, GetXxxFileList, PutXxxFileList).
  /// </summary>
  public const string BatchResultDescription =
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "Data is a JSON object with DeviceName, DeviceItemName, PlcName, " +
    "Directory (only for Dir tools: the given filesystem directory), " +
    "Succeeded and Failed (counts), Items (one result object per successful element, same fields as the single-element tool returns), " +
    "Errors (one object per failed element: Name, Path, Files, StatusCode the single call would have returned, Error), " +
    "Skipped (Name, File, Reason, e.g. know-how-protected elements or files of another object kind). " +
    "Elements are processed independently: a failed element does not stop the others, and nothing is rolled back. " +
    "If any element failed, IsSuccess is false, StatusCode is 400, Error is a short summary and Data still contains the complete object described above.";

  /// <summary>
  /// Maps a REST response to a tool result with McpApiResponse as JSON content.
  /// IsError is set when the REST call failed, so MCP clients see the failure without parsing the content.
  /// Error bodies are usually plain text from TiaREST and must not be JSON-deserialized blindly
  /// (that produced "'E' is an invalid start of a value"); JSON object bodies (batch results) go to Data.
  /// </summary>
  public static async Task<CallToolResult> FromHttpResponseAsync(HttpResponseMessage response) {
    string body = await response.Content.ReadAsStringAsync();
    int statusCode = (int)response.StatusCode;

    if (!response.IsSuccessStatusCode) {
      if (TryParseJsonObject(body, out JsonElement errorData)) {
        return ToToolResult(new McpApiResponse {
          StatusCode = statusCode,
          IsSuccess = false,
          Error = DescribeJsonError(errorData, statusCode),
          Data = errorData
        });
      }
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

  private static bool TryParseJsonObject(string body, out JsonElement data) {
    data = default;
    if (string.IsNullOrWhiteSpace(body) || body.TrimStart()[0] != '{') {
      return false;
    }
    try {
      data = JsonSerializer.Deserialize<JsonElement>(body);
      return data.ValueKind == JsonValueKind.Object;
    } catch (JsonException) {
      return false;
    }
  }

  private static string DescribeJsonError(JsonElement data, int statusCode) {
    if (data.TryGetProperty("Failed", out JsonElement failed) && failed.ValueKind == JsonValueKind.Number
        && data.TryGetProperty("Succeeded", out JsonElement succeeded) && succeeded.ValueKind == JsonValueKind.Number) {
      int failedCount = failed.GetInt32();
      return $"{failedCount} of {failedCount + succeeded.GetInt32()} elements failed; see Data.Errors. The other elements were processed (Data.Items).";
    }
    return $"The request failed with HTTP {statusCode}; see Data for details.";
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
