using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

[McpServerToolType]
public class SkillsTools {
  [McpServerTool(Name = "skill-download"), Description(
    "Downloads all Agent Skills served by this MCP server as a single ZIP archive onto the machine that runs TiaMCP. " +
    "The archive contains each skill folder (e.g. tia-plc-workspace/SKILL.md and supporting files) with the same UTF-8 content " +
    "that resources/read returns. " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON object with: Path (absolute path of the written ZIP), SkillCount, FileCount.")]
  public static CallToolResult Download(
    SkillCatalog catalog,
    [Description("Absolute filesystem directory on the TiaMCP machine where the ZIP is written")] string directory,
    [Description("Optional ZIP file name (default tia-opencode-skills.zip); .zip is appended if missing")] string? fileName = null) {
    var activity = ToolActivity.Start<SkillsTools>("Skills ZIP wird erstellt");
    try {
      if (string.IsNullOrWhiteSpace(directory)) {
        return activity.Complete(TiaRestClient.FromValidationError("directory is required and must be an absolute path."));
      }
      if (!Path.IsPathRooted(directory)) {
        return activity.Complete(TiaRestClient.FromValidationError("directory must be an absolute path."));
      }

      string zipPath = catalog.WriteZip(directory, fileName);
      return activity.Complete(TiaRestClient.FromSuccess(new {
        Path = zipPath,
        SkillCount = catalog.Entries.Count,
        FileCount = catalog.AllFiles.Count()
      }));
    } catch (ArgumentException ex) {
      return activity.Complete(TiaRestClient.FromValidationError(ex.Message));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
