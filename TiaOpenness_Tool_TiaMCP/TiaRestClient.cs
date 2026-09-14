using System;
using System.Net.Http;
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
}