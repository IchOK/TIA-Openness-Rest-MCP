using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.ComponentModel;

namespace Tophinke.TopCtrl.Tool.TiaMCP;

// 1. Antwort-Modell für das MCP-SDK definieren
public class McpApiResponse {
  public int StatusCode { get; set; }
  public bool IsSuccess { get; set; }
  public JsonElement? Data { get; set; }
  public string? Error { get; set; }
}