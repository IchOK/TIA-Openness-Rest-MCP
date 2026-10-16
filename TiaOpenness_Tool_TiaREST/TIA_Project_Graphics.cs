using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.Hmi.Globalization;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaREST.HMI;
using Tophinke.TiaOpenness.Tool.TiaREST.PLC;
using Tophinke.TiaOpenness.Tool.Types.HMI;
using Tophinke.TiaOpenness.Tool.Types.PLC.Compile;
using Tophinke.TiaOpenness.Tool.Types.Project.Graphics;
using static Tophinke.TiaOpenness.Tool.TiaREST.HMI.cTiaHmi;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.ProjectGraphics {
  /// <summary>
  /// Projekt-Grafiksammlung (Sprachen &amp; Ressourcen → Projektgrafiken): listen und doppelte Einträge aufräumen.
  /// </summary>
  static internal class cTiaProjectGraphics {
    static readonly Regex SuffixNumber = new Regex(@"^(?<base>.+)_(?<n>\d+)$", RegexOptions.CultureInvariant);
    static readonly BindingFlags PropFlags = BindingFlags.Instance | BindingFlags.Public;

    /// <summary>
    /// Listet alle Einträge der Projekt-Grafiksammlung.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="namePattern">Optionaler Namensfilter mit * und ?</query>
    /// <returns>JSON-Array von GraphicInfo oder Fehlermeldung</returns>
    [RestRoute(ProjectRoutes.GraphicsList)]
    static public string List(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      Func<string, bool> filter = NameFilter(context.Request.QueryString["namePattern"]);
      return Execute(context, processIdStr, projectName, project => {
        var items = project.Graphics
          .Where(g => filter(g.Name))
          .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
          .Select(g => new GraphicInfo { Name = g.Name })
          .ToList();
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(items);
      });
    }

    /// <summary>
    /// Entfernt doppelte Projektgrafiken (Name_1, Name_2, … wenn Name existiert und – standardmäßig –
    /// der Bildinhalt dem Original gleicht): zuerst Verweise in allen Unified-Bildern auf das Original
    /// umbiegen, dann die Duplikate löschen. Optional anschließend jedes Unified-Gerät übersetzen.
    /// Effektiver als erst löschen und aus Übersetzungsfehlern nachzuziehen.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <body>CleanupDuplicatesRequest (optional; ohne Body wie DryRun=false, CompileAfter=false)</body>
    /// <returns>JSON-String mit CleanupDuplicatesResult oder Fehlermeldung</returns>
    [RestRoute(ProjectRoutes.GraphicsCleanupDuplicates, "POST")]
    static public string CleanupDuplicates(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      CleanupDuplicatesRequest request = ReadOptionalRequest(context, out string readError);
      if (readError != null) {
        return readError;
      }
      if (request == null) {
        request = new CleanupDuplicatesRequest();
      }

      return Execute(context, processIdStr, projectName, project => {
        var result = new CleanupDuplicatesResult { DryRun = request.DryRun };
        HashSet<string> names = new HashSet<string>(project.Graphics.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        result.GraphicCount = names.Count;

        bool compareContent = request.CompareContent ?? true;
        Func<string, bool> originalFilter = NameFilter(request.NamePattern);
        var nameCandidates = new List<DuplicateMapping>();
        foreach (string name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) {
          string original = ResolveOriginal(name, names);
          if (original == null || !originalFilter(original)) {
            continue;
          }
          nameCandidates.Add(new DuplicateMapping { Duplicate = name, Original = original });
        }
        if (nameCandidates.Count == 0) {
          result.Warnings.Add("No name candidates found (names ending with _<number> whose base name exists).");
          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        var duplicates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (compareContent) {
          result.Warnings.Add($"Comparing export content for {nameCandidates.Count} name candidate(s); this may take several minutes.");
          string tempRoot = Path.Combine(Path.GetTempPath(), "tia-graphics-cleanup-" + Guid.NewGuid().ToString("N"));
          Directory.CreateDirectory(tempRoot);
          try {
            var originalFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int skippedDifferent = 0;
            int exportFailed = 0;
            foreach (DuplicateMapping candidate in nameCandidates) {
              if (!originalFingerprints.TryGetValue(candidate.Original, out string originalFp)) {
                originalFp = TryFingerprint(project.Graphics.Find(candidate.Original), Path.Combine(tempRoot, "orig-" + SafeFile(candidate.Original)), out string originalError);
                if (originalFp == null) {
                  exportFailed++;
                  result.Warnings.Add($"Could not export original graphic {candidate.Original}: {originalError}");
                  originalFingerprints[candidate.Original] = "";
                  continue;
                }
                originalFingerprints[candidate.Original] = originalFp;
              }
              if (string.IsNullOrEmpty(originalFp)) {
                continue;
              }
              string duplicateFp = TryFingerprint(project.Graphics.Find(candidate.Duplicate), Path.Combine(tempRoot, "dup-" + SafeFile(candidate.Duplicate)), out string duplicateError);
              if (duplicateFp == null) {
                exportFailed++;
                result.Warnings.Add($"Could not export duplicate graphic {candidate.Duplicate}: {duplicateError}");
                continue;
              }
              if (!string.Equals(originalFp, duplicateFp, StringComparison.Ordinal)) {
                skippedDifferent++;
                continue;
              }
              candidate.ContentMatch = true;
              duplicates[candidate.Duplicate] = candidate.Original;
              result.Duplicates.Add(candidate);
            }
            if (skippedDifferent > 0) {
              result.Warnings.Add($"{skippedDifferent} name candidate(s) differ in image content from their base name and were kept.");
            }
            if (exportFailed > 0) {
              result.Warnings.Add($"{exportFailed} graphic export(s) failed during content comparison.");
            }
          } finally {
            try { Directory.Delete(tempRoot, true); } catch { /* ignore cleanup */ }
          }
        } else {
          result.Warnings.Add("CompareContent=false: treating every Name_<number> as duplicate of Name without checking image content.");
          foreach (DuplicateMapping candidate in nameCandidates) {
            candidate.ContentMatch = false;
            duplicates[candidate.Duplicate] = candidate.Original;
            result.Duplicates.Add(candidate);
          }
        }
        result.DuplicateCount = duplicates.Count;
        if (duplicates.Count == 0) {
          result.Warnings.Add("No content-identical duplicate project graphics found.");
          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        }

        List<UnifiedTarget> targets = cTiaHmi.FindAll(project);
        if (!string.IsNullOrEmpty(request.DeviceName) || !string.IsNullOrEmpty(request.DeviceItemName)) {
          targets = targets
            .Where(t => string.IsNullOrEmpty(request.DeviceName) || t.Device.Name.Equals(request.DeviceName, StringComparison.OrdinalIgnoreCase))
            .Where(t => string.IsNullOrEmpty(request.DeviceItemName) || t.DeviceItem.Name.Equals(request.DeviceItemName, StringComparison.OrdinalIgnoreCase))
            .ToList();
          if (targets.Count == 0) {
            return ErrorResponse(context, HttpStatusCode.NotFound,
              $"Error: No matching WinCC Unified device for {request.DeviceName}/{request.DeviceItemName}.");
          }
        }
        if (targets.Count == 0) {
          result.Warnings.Add("No WinCC Unified device in the project; only duplicate graphics in the collection can be deleted.");
        }

        foreach (UnifiedTarget target in targets) {
          ScanAndFixDevice(target, duplicates, request.DryRun, result);
        }

        foreach (var entry in duplicates.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) {
          if (result.Usages.Any(u => string.Equals(u.OldValue, entry.Key, StringComparison.OrdinalIgnoreCase)
                                     && (u.Action == "Error" || u.Action == "SkippedReadOnly"))) {
            result.Warnings.Add($"Graphic {entry.Key} was not deleted because at least one reference could not be updated.");
            result.DeletedGraphics.Add(new Change {
              Kind = "Graphic",
              Name = entry.Key,
              Action = request.DryRun ? "WouldDelete" : "Skipped",
              Details = $"Original: {entry.Value}",
              Error = "Remaining references prevent deletion."
            });
            continue;
          }
          if (request.DryRun) {
            result.DeletedGraphics.Add(new Change {
              Kind = "Graphic",
              Name = entry.Key,
              Action = "WouldDelete",
              Details = $"Original: {entry.Value}"
            });
            continue;
          }
          MultiLingualGraphic graphic = project.Graphics.Find(entry.Key);
          if (graphic == null) {
            result.DeletedGraphics.Add(new Change {
              Kind = "Graphic",
              Name = entry.Key,
              Action = "Deleted",
              Error = "Graphic not found."
            });
            continue;
          }
          try {
            graphic.Delete();
            result.DeletedGraphics.Add(new Change {
              Kind = "Graphic",
              Name = entry.Key,
              Action = "Deleted",
              Details = $"Original: {entry.Value}"
            });
          } catch (Exception ex) {
            result.DeletedGraphics.Add(new Change {
              Kind = "Graphic",
              Name = entry.Key,
              Action = "Deleted",
              Details = $"Original: {entry.Value}",
              Error = ex.Message
            });
          }
        }

        if (request.CompileAfter && !request.DryRun) {
          result.Compiles = new List<CompileResult>();
          foreach (UnifiedTarget target in targets) {
            result.Compiles.Add(CompileHmi(target));
          }
        } else if (request.CompileAfter && request.DryRun) {
          result.Warnings.Add("CompileAfter is ignored with DryRun.");
        }

        if (result.Usages.Any(u => u.Error != null) || result.DeletedGraphics.Any(c => c.Error != null)
            || (result.Compiles != null && result.Compiles.Any(c => c.ErrorCount > 0))) {
          context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
      });
    }

    #region Duplikate und Verweise

    /// <summary>
    /// Liefert das Original für "Name_1", "Name_2", … wenn das Basis-Objekt in der Sammlung existiert
    /// (ggf. über mehrere Suffixe, z. B. Name_1_2 → Name).
    /// </summary>
    static internal string ResolveOriginal(string name, ISet<string> names) {
      Match match = SuffixNumber.Match(name ?? "");
      if (!match.Success) {
        return null;
      }
      string candidate = match.Groups["base"].Value;
      if (names.Contains(candidate)) {
        return ResolveOriginal(candidate, names) ?? candidate;
      }
      return ResolveOriginal(candidate, names);
    }

    static void ScanAndFixDevice(UnifiedTarget target, Dictionary<string, string> duplicates, bool dryRun, CleanupDuplicatesResult result) {
      HmiSoftware hmi = target.Software;
      foreach (var screenEntry in AllScreens(hmi)) {
        HmiScreen screen = screenEntry.Screen;
        string screenPath = screenEntry.Path;
        // Nur Eigenschaften des Bildes selbst (z. B. BackGraphic), nicht ScreenItems – die folgen separat.
        FixGraphicProperties(target, screen, screenPath, "(Screen)", screen, duplicates, dryRun, result, recurseChildren: false);
        foreach (HmiScreenItemBase item in screen.ScreenItems) {
          FixGraphicProperties(target, screen, screenPath, item.Name, item, duplicates, dryRun, result, recurseChildren: true);
        }
      }
    }

    static IEnumerable<(HmiScreen Screen, string Path)> AllScreens(HmiSoftware hmi) {
      foreach (HmiScreen screen in hmi.Screens) {
        yield return (screen, screen.Name);
      }
      var stack = new Stack<(HmiScreenGroup Group, string Path)>();
      foreach (HmiScreenGroup group in hmi.ScreenGroups) {
        stack.Push((group, group.Name));
      }
      while (stack.Count > 0) {
        var entry = stack.Pop();
        foreach (HmiScreen screen in entry.Group.Screens) {
          yield return (screen, entry.Path + "/" + screen.Name);
        }
        foreach (HmiScreenGroup child in entry.Group.Groups) {
          stack.Push((child, entry.Path + "/" + child.Name));
        }
      }
    }

    /// <summary>
    /// Setzt alle schreibbaren String-Eigenschaften, deren Name auf "Graphic" endet (Graphic, AlternateGraphic, BackGraphic, …).
    /// Nested Parts werden über öffentliche Properties mit IEnumerable&lt;Engineering-Objekt&gt; bzw. Einzelobjekt erreicht.
    /// </summary>
    static void FixGraphicProperties(UnifiedTarget target, HmiScreen screen, string screenPath, string itemName, object root,
                                     Dictionary<string, string> duplicates, bool dryRun, CleanupDuplicatesResult result, bool recurseChildren) {
      var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
      var queue = new Queue<(object Obj, string Path, int Depth)>();
      queue.Enqueue((root, itemName, 0));
      while (queue.Count > 0) {
        var entry = queue.Dequeue();
        if (entry.Obj == null || entry.Depth > 4 || !seen.Add(entry.Obj)) {
          continue;
        }
        Type type = entry.Obj.GetType();
        foreach (PropertyInfo property in type.GetProperties(PropFlags)) {
          if (!property.CanRead || property.GetIndexParameters().Length > 0) {
            continue;
          }
          if (string.Equals(property.Name, "Parent", StringComparison.Ordinal)
              || string.Equals(property.Name, "ScreenItems", StringComparison.Ordinal)) {
            continue;
          }
          Type propType = property.PropertyType;
          if (propType == typeof(string) && property.Name.EndsWith("Graphic", StringComparison.Ordinal)
              && !string.Equals(property.Name, "GraphicStretchMode", StringComparison.Ordinal)) {
            HandleGraphicProperty(target, screen, screenPath, entry.Path, type.Name, property, entry.Obj, duplicates, dryRun, result);
            continue;
          }

          if (!recurseChildren || entry.Depth >= 4) {
            continue;
          }
          if (!typeof(IEngineeringObject).IsAssignableFrom(propType)
              && !(typeof(IEnumerable).IsAssignableFrom(propType) && propType != typeof(string))) {
            continue;
          }
          if (typeof(HmiSoftware).IsAssignableFrom(propType)
              || typeof(Project).IsAssignableFrom(propType)
              || typeof(HmiScreen).IsAssignableFrom(propType)
              || typeof(HmiScreenItemBaseComposition).IsAssignableFrom(propType)) {
            continue;
          }
          object child;
          try {
            child = property.GetValue(entry.Obj);
          } catch {
            continue;
          }
          if (child == null || ReferenceEquals(child, entry.Obj)) {
            continue;
          }
          if (child is IEngineeringObject) {
            queue.Enqueue((child, entry.Path + "." + property.Name, entry.Depth + 1));
          } else if (child is IEnumerable enumerable) {
            int index = 0;
            foreach (object element in enumerable) {
              if (element is IEngineeringObject) {
                queue.Enqueue((element, $"{entry.Path}.{property.Name}[{index}]", entry.Depth + 1));
              }
              index++;
            }
          }
        }
      }
    }

    /// <summary>
    /// Exportiert eine Projektgrafik und bildet einen Fingerprint nur über die Bilddateien (nicht über XML mit Namen).
    /// </summary>
    static string TryFingerprint(MultiLingualGraphic graphic, string directory, out string error) {
      error = null;
      if (graphic == null) {
        error = "graphic not found.";
        return null;
      }
      try {
        Directory.CreateDirectory(directory);
        foreach (string file in Directory.GetFiles(directory)) {
          File.Delete(file);
        }
        string xmlPath = Path.Combine(directory, SafeFile(graphic.Name) + ".xml");
        graphic.Export(new FileInfo(xmlPath), ExportOptions.WithDefaults);
        string[] files = Directory.GetFiles(directory)
          .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
          .ToArray();
        if (files.Length == 0) {
          error = "export produced no files.";
          return null;
        }
        // Unified-Export liefert oft nur XML (Bilddaten darin); Namen werden neutralisiert, damit Original und Name_1 vergleichbar sind.
        using (var sha = SHA256.Create()) {
          foreach (string file in files) {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            byte[] extBytes = System.Text.Encoding.UTF8.GetBytes(ext);
            sha.TransformBlock(extBytes, 0, extBytes.Length, null, 0);
            byte[] content;
            if (ext == ".xml" || ext == ".yml" || ext == ".txt") {
              string text = File.ReadAllText(file);
              text = NeutralizeGraphicExport(text, graphic.Name);
              content = System.Text.Encoding.UTF8.GetBytes(text);
            } else {
              content = File.ReadAllBytes(file);
            }
            sha.TransformBlock(content, 0, content.Length, null, 0);
          }
          sha.TransformFinalBlock(new byte[0], 0, 0);
          return BitConverter.ToString(sha.Hash).Replace("-", "");
        }
      } catch (Exception ex) {
        error = ex.Message;
        return null;
      }
    }

    /// <summary>
    /// Entfernt den Grafiknamen aus dem Export, damit inhaltlich gleiche Grafiken mit unterschiedlichem Namen denselben Fingerprint bekommen.
    /// </summary>
    static string NeutralizeGraphicExport(string text, string graphicName) {
      if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(graphicName)) {
        return text;
      }
      text = Regex.Replace(text, Regex.Escape(graphicName), "", RegexOptions.IgnoreCase);
      text = Regex.Replace(text, @"\bName\s*=\s*""[^""]*""", "Name=\"\"");
      text = Regex.Replace(text, @"<Name>\s*[^<]*\s*</Name>", "<Name></Name>");
      text = Regex.Replace(text, @"\b(?:ID|Id|Guid|GUID)\s*=\s*""[^""]*""", "Id=\"\"");
      text = Regex.Replace(text, @"<(?:ID|Id|Guid|GUID)>\s*[^<]*\s*</(?:ID|Id|Guid|GUID)>", "<Id></Id>");
      text = Regex.Replace(text, @"\d{4}-\d{2}-\d{2}T[\d:.+-]+Z?", "");
      text = Regex.Replace(text, @"\s+", " ");
      return text;
    }

    static string SafeFile(string name) {
      foreach (char c in Path.GetInvalidFileNameChars()) {
        name = name.Replace(c, '_');
      }
      return name;
    }

    static void HandleGraphicProperty(UnifiedTarget target, HmiScreen screen, string screenPath, string itemPath, string itemType,
                                      PropertyInfo property, object owner, Dictionary<string, string> duplicates, bool dryRun,
                                      CleanupDuplicatesResult result) {
      string value;
      try {
        value = property.GetValue(owner) as string;
      } catch {
        return;
      }
      if (string.IsNullOrEmpty(value) || !duplicates.TryGetValue(value, out string original)) {
        return;
      }
      var usage = new GraphicUsage {
        DeviceName = target.Device.Name,
        DeviceItemName = target.DeviceItem.Name,
        HmiName = target.Software.Name,
        ScreenName = screen.Name,
        ScreenPath = screenPath,
        ItemName = itemPath,
        ItemType = itemType,
        Property = property.Name,
        OldValue = value,
        NewValue = original
      };
      if (!property.CanWrite) {
        usage.Action = "SkippedReadOnly";
        usage.Error = $"Property {property.Name} is read-only.";
        result.Usages.Add(usage);
        return;
      }
      if (dryRun) {
        usage.Action = "WouldReplace";
        result.Usages.Add(usage);
        return;
      }
      try {
        property.SetValue(owner, original);
        usage.Action = "Replace";
      } catch (Exception ex) {
        usage.Action = "Error";
        usage.Error = ex.InnerException?.Message ?? ex.Message;
      }
      result.Usages.Add(usage);
    }

    #endregion

    #region Übersetzen

    static CompileResult CompileHmi(UnifiedTarget target) {
      var result = new CompileResult {
        DeviceName = target.Device.Name,
        DeviceItemName = target.DeviceItem.Name,
        PlcName = target.Software.Name,
        Target = "HMI",
        Name = target.Software.Name,
        Messages = new List<CompileMessage>()
      };
      try {
        ICompilable compilable = GetCompilable(target.Software) ?? GetCompilable(target.DeviceItem) ?? GetCompilable(target.Device);
        if (compilable == null) {
          result.State = "Error";
          result.ErrorCount = 1;
          result.Messages.Add(new CompileMessage {
            State = "Error",
            Description = "No ICompilable service on HmiSoftware, DeviceItem or Device."
          });
          return result;
        }
        CompilerResult compilerResult = compilable.Compile();
        result.State = compilerResult.State.ToString();
        result.ErrorCount = compilerResult.ErrorCount;
        result.WarningCount = compilerResult.WarningCount;
        cTiaCompile.CollectMessages(compilerResult, result.Messages);
      } catch (Exception ex) {
        result.State = "Error";
        result.ErrorCount = 1;
        result.Messages.Add(new CompileMessage {
          State = "Error",
          Description = ex.Message
        });
      }
      return result;
    }

    static ICompilable GetCompilable(object engineeringObject) {
      if (engineeringObject is IEngineeringServiceProvider provider) {
        return provider.GetService<ICompilable>();
      }
      // HmiSoftware implementiert IEngineeringServiceProvider nicht explizit; GetService liegt am Runtime-Objekt.
      MethodInfo method = engineeringObject?.GetType().GetMethod("GetService", new[] { typeof(Type) })
        ?? engineeringObject?.GetType().GetMethods().FirstOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
      if (method == null) {
        return null;
      }
      try {
        if (method.IsGenericMethodDefinition) {
          return method.MakeGenericMethod(typeof(ICompilable)).Invoke(engineeringObject, null) as ICompilable;
        }
        return method.Invoke(engineeringObject, new object[] { typeof(ICompilable) }) as ICompilable;
      } catch {
        return null;
      }
    }

    #endregion

    static CleanupDuplicatesRequest ReadOptionalRequest(HttpListenerContext context, out string errorResponse) {
      errorResponse = null;
      if (context.Request.ContentLength64 == 0 && !context.Request.HasEntityBody) {
        return null;
      }
      try {
        using (var reader = new System.IO.StreamReader(context.Request.InputStream, context.Request.ContentEncoding)) {
          string body = reader.ReadToEnd();
          if (string.IsNullOrWhiteSpace(body) || body == "{}") {
            return new CleanupDuplicatesRequest();
          }
          return JsonConvert.DeserializeObject<CleanupDuplicatesRequest>(body);
        }
      } catch (Exception ex) {
        errorResponse = ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
        return null;
      }
    }

    sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
      public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
      bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
      int IEqualityComparer<object>.GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
  }
}
