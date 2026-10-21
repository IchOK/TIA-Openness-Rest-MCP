using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;

namespace Tophinke.TiaOpenness.Tool.TiaMCP;

/// <summary>
/// Loads Agent Skills from disk, computes SHA-256 digests over the exact UTF-8 bytes served,
/// and builds MCP Skills Extension entries (<c>io.modelcontextprotocol/skills</c>).
/// </summary>
public sealed record SkillFile(string Uri, string Text, string Digest, int Size, string MimeType);

public sealed record SkillEntry(string Uri, JsonObject Frontmatter, IReadOnlyList<SkillFile> Files) {
  public JsonObject ToJson() => new() {
    ["uri"] = Uri,
    ["frontmatter"] = Frontmatter.DeepClone(),
    ["resources"] = new JsonArray(Files.Select(f => (JsonNode)new JsonObject {
      ["uri"] = f.Uri,
      ["digest"] = f.Digest,
      ["size"] = f.Size
    }).ToArray())
  };
}

public sealed class SkillCatalog {
  private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
  private readonly Dictionary<string, SkillEntry> _byUri = new(StringComparer.Ordinal);

  public IReadOnlyCollection<SkillEntry> Entries => _byUri.Values;
  public IEnumerable<SkillFile> AllFiles => _byUri.Values.SelectMany(e => e.Files);

  public bool TryGet(string uri, out SkillEntry entry) => _byUri.TryGetValue(uri, out entry!);

  public static SkillCatalog Load(string skillsRoot) {
    if (!Directory.Exists(skillsRoot)) {
      throw new InvalidOperationException($"Skills directory not found: {skillsRoot}");
    }

    var catalog = new SkillCatalog();
    foreach (var skillDir in Directory.GetDirectories(skillsRoot).OrderBy(d => d, StringComparer.Ordinal)) {
      var skillName = Path.GetFileName(skillDir);
      var files = new List<SkillFile>();
      foreach (var path in Directory.GetFiles(skillDir, "*", SearchOption.AllDirectories)
                                   .OrderBy(p => p, StringComparer.Ordinal)) {
        var bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith(Utf8Bom)) {
          bytes = bytes[Utf8Bom.Length..];
        }

        var relative = Path.GetRelativePath(skillDir, path).Replace('\\', '/');
        var uri = $"skill://{skillName}/{relative}";
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var mime = path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "text/markdown" : "text/plain";
        files.Add(new SkillFile(uri, Encoding.UTF8.GetString(bytes), digest, bytes.Length, mime));
      }

      var skillMd = files.SingleOrDefault(f => f.Uri.EndsWith("/SKILL.md", StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Skill '{skillName}' has no SKILL.md.");
      var frontmatter = ParseFrontmatter(skillMd.Text);
      if (frontmatter["name"]?.GetValue<string>() != skillName) {
        throw new InvalidOperationException(
          $"Skill folder '{skillName}' does not match its frontmatter name '{frontmatter["name"]}'.");
      }

      if (files.Count > 512 || files.Sum(f => (long)f.Size) > 16L * 1024 * 1024) {
        throw new InvalidOperationException($"Skill '{skillName}' exceeds the 512 file / 16 MiB limit.");
      }

      catalog._byUri[skillMd.Uri] = new SkillEntry(skillMd.Uri, frontmatter, files);
    }

    return catalog;
  }

  /// <summary>
  /// Writes a ZIP of all skills (paths as <c>{skillName}/…</c>, BOM-stripped UTF-8 content) into
  /// <paramref name="directory"/>. Returns the absolute path of the created archive.
  /// </summary>
  public string WriteZip(string directory, string? fileName = null) {
    if (string.IsNullOrWhiteSpace(directory)) {
      throw new ArgumentException("directory is required.", nameof(directory));
    }
    if (!Path.IsPathRooted(directory)) {
      throw new ArgumentException("directory must be an absolute path.", nameof(directory));
    }

    Directory.CreateDirectory(directory);
    fileName = string.IsNullOrWhiteSpace(fileName) ? "tia-opencode-skills.zip" : fileName.Trim();
    if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
      fileName += ".zip";
    }

    var zipPath = Path.GetFullPath(Path.Combine(directory, fileName));
    if (File.Exists(zipPath)) {
      File.Delete(zipPath);
    }

    using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
      foreach (var skill in Entries.OrderBy(e => e.Uri, StringComparer.Ordinal)) {
        foreach (var file in skill.Files) {
          // skill://name/rel → name/rel
          var entryName = file.Uri.StartsWith("skill://", StringComparison.Ordinal)
            ? file.Uri["skill://".Length..]
            : file.Uri;
          var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
          using var stream = entry.Open();
          var bytes = Encoding.UTF8.GetBytes(file.Text);
          stream.Write(bytes, 0, bytes.Length);
        }
      }
    }

    return zipPath;
  }

  private static JsonObject ParseFrontmatter(string markdown) {
    var lines = markdown.Split('\n');
    if (lines.Length == 0 || lines[0].TrimEnd('\r') != "---") {
      throw new InvalidOperationException("SKILL.md must start with YAML frontmatter.");
    }

    var end = Array.FindIndex(lines, 1, l => l.TrimEnd('\r') == "---");
    if (end < 0) {
      throw new InvalidOperationException("SKILL.md frontmatter is not closed.");
    }

    var yaml = string.Join('\n', lines[1..end]);
    var raw = new DeserializerBuilder().Build().Deserialize<object>(yaml)
      ?? throw new InvalidOperationException("SKILL.md frontmatter is empty.");
    var jsonText = new SerializerBuilder().JsonCompatible().Build().Serialize(raw);
    var node = JsonNode.Parse(jsonText)!.AsObject();
    if (node["name"] is null || node["description"] is null) {
      throw new InvalidOperationException("Frontmatter requires 'name' and 'description'.");
    }

    return node;
  }
}
