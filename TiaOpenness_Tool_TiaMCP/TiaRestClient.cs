using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

public static class TiaRestClient {
  private static readonly HttpClient _client = new HttpClient {};

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
  /// Maps a REST response to McpApiResponse. Error bodies are plain text from TiaREST
  /// and must not be JSON-deserialized (that produced "'E' is an invalid start of a value").
  /// </summary>
  public static async Task<McpApiResponse> FromHttpResponseAsync(HttpResponseMessage response) {
    string body = await response.Content.ReadAsStringAsync();
    int statusCode = (int)response.StatusCode;

    if (!response.IsSuccessStatusCode) {
      return new McpApiResponse {
        StatusCode = statusCode,
        IsSuccess = false,
        Error = string.IsNullOrWhiteSpace(body)
          ? (response.ReasonPhrase ?? ("HTTP " + statusCode))
          : body.Trim()
      };
    }

    if (string.IsNullOrWhiteSpace(body)) {
      return new McpApiResponse {
        StatusCode = statusCode,
        IsSuccess = true
      };
    }

    try {
      return new McpApiResponse {
        StatusCode = statusCode,
        IsSuccess = true,
        Data = JsonSerializer.Deserialize<JsonElement>(body)
      };
    } catch (JsonException) {
      return new McpApiResponse {
        StatusCode = statusCode,
        IsSuccess = false,
        Error = body.Trim()
      };
    }
  }

  public static McpApiResponse FromException(Exception ex) {
    if (ex is HttpRequestException) {
      return new McpApiResponse {
        StatusCode = (int)HttpStatusCode.BadGateway,
        IsSuccess = false,
        Error = "The TIA Openness REST API is not running or is not reachable."
      };
    }
    return new McpApiResponse {
      StatusCode = (int)HttpStatusCode.InternalServerError,
      IsSuccess = false,
      Error = "An unexpected error occurred: " + ex.Message
    };
  }
}
