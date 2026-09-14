using System;
using System.Net.Http;
using Tophinke.TopCtrl.Tool.Consts;

namespace Tophinke.TopCtrl.Tool.TiaMCP;

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