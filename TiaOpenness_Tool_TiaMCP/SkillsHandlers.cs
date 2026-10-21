#pragma warning disable MCPEXP002

using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

/// <summary>
/// Raw JSON-RPC handlers for the MCP Skills Extension (<c>skills/list</c>, <c>skills/get</c>).
/// </summary>
public static class SkillsHandlers {
  private const int TtlMs = 300_000;

  public static IList<McpServerRequestHandler> Create(SkillCatalog catalog) => [
    new McpServerRequestHandler {
      Method = "skills/list",
      Handler = (request, ct) => {
        JsonNode result = new JsonObject {
          ["resultType"] = "complete",
          ["skills"] = new JsonArray(catalog.Entries.Select(e => (JsonNode)e.ToJson()).ToArray()),
          ["ttlMs"] = TtlMs,
          ["cacheScope"] = "public"
        };
        return ValueTask.FromResult<JsonNode?>(result);
      }
    },
    new McpServerRequestHandler {
      Method = "skills/get",
      Handler = (request, ct) => {
        var uri = request.Params?["uri"]?.GetValue<string>();
        if (uri is null || !catalog.TryGet(uri, out var entry)) {
          throw new McpProtocolException($"Unknown skill: {uri}", McpErrorCode.InvalidParams);
        }

        JsonNode result = new JsonObject {
          ["resultType"] = "complete",
          ["skill"] = entry.ToJson(),
          ["ttlMs"] = TtlMs,
          ["cacheScope"] = "public"
        };
        return ValueTask.FromResult<JsonNode?>(result);
      }
    }
  ];
}
