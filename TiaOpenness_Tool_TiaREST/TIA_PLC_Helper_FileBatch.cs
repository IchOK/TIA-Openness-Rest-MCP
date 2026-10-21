using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper {
  /// <summary>
  /// Ergebnis eines einzelnen Schritts: Wert oder Fehler mit dem HTTP-Status, den ein Einzelaufruf liefern würde.
  /// </summary>
  internal class ItemResult<T> {
    public T Value;
    public HttpStatusCode Status;
    public string Error;

    public bool Failed => Error != null;

    static public ItemResult<T> Ok(T value) {
      return new ItemResult<T> { Value = value, Status = HttpStatusCode.OK };
    }

    static public ItemResult<T> Fail(HttpStatusCode status, string error) {
      return new ItemResult<T> { Status = status, Error = error };
    }
  }

  /// <summary>
  /// Inhalt für den Import eines Bausteins oder Datentyps.
  /// </summary>
  internal class ImportSource {
    public string Format;
    public string Content;
    public string MultiLingualText;
    public string Directory;   // nur bei Import aus Dateien
    public string[] Files;     // nur bei Import aus Dateien; null = Inhalt aus dem Request-Body
  }

  /// <summary>
  /// Element, das beim Import aus einem Verzeichnis gefunden wurde.
  /// </summary>
  internal class DirEntry {
    public string Name;        // Dateiname ohne Endung = Name des Elements
    public string[] Path;      // TIA-Ordner (Request-Path plus Unterverzeichnisse)
    public string Directory;   // Dateisystem-Verzeichnis der Dateien dieses Elements
    public string Format;      // FormatSd oder FormatXml
    public string File;        // Inhaltsdatei (.s7dcl bzw. .xml)
  }

  /// <summary>
  /// Gemeinsame Hilfsmethoden für Datei-Operationen auf mehreren Elementen (GetDir, PutDir, GetFileList, PutFileList).
  /// </summary>
  static internal class cTiaFileBatchHelpers {
    public const string KindBlock = "block";
    public const string KindUdt = "UDT";
    public const string KindTagTable = "tag table";

    /// <summary>
    /// Führt einen Einzelaufruf im TIA-Projekt aus und setzt die Antwort.
    /// </summary>
    static public string ExecuteItem<T>(HttpListenerContext context, string processIdStr, string projectName, Func<ProjectBase, ItemResult<T>> action) {
      return Execute(context, processIdStr, projectName, action, value => {
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(value);
      });
    }

    /// <summary>
    /// Führt eine Batch-Operation im TIA-Projekt aus und setzt die Antwort.
    /// Sind einzelne Elemente fehlgeschlagen, ist der Status 400 und der Body enthält trotzdem das vollständige Ergebnis.
    /// </summary>
    static public string ExecuteBatch<T>(HttpListenerContext context, string processIdStr, string projectName, Func<ProjectBase, ItemResult<FileBatchResult<T>>> action) {
      return Execute(context, processIdStr, projectName, action, batch => {
        batch.Succeeded = batch.Items.Count;
        batch.Failed = batch.Errors.Count;
        if (batch.Failed > 0) {
          context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(batch, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
      });
    }

    static private string Execute<T>(HttpListenerContext context, string processIdStr, string projectName, Func<ProjectBase, ItemResult<T>> action, Func<T, string> serialize) {
      try {
        string kind = context.Request.QueryString["kind"];
        ItemResult<T> result = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, action, kind);
        return result.Failed ? ErrorResponse(context, result.Status, result.Error) : serialize(result.Value);
      } catch (EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        return ErrorResponse(context, HttpStatusCode.ServiceUnavailable, "Error: TIA session unavailable. Please re-open TIA Portal.");
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error accessing TIA project: {ex.Message}");
      }
    }

    static public FileBatchResult<T> NewBatch<T>(string deviceName, string deviceItemName, PlcSoftware plcSoftware) {
      return new FileBatchResult<T> {
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
        PlcName = plcSoftware.Name
      };
    }

    /// <summary>
    /// Übernimmt das Ergebnis eines Elements in das Batch-Ergebnis.
    /// </summary>
    static public void Collect<T>(FileBatchResult<T> batch, ItemResult<T> result, string name, string[] path) {
      if (result.Failed) {
        batch.Errors.Add(new FileBatchError { Name = name, Path = path, StatusCode = (int)result.Status, Error = result.Error });
      } else {
        batch.Items.Add(result.Value);
      }
    }

    /// <summary>
    /// Importiert mehrere Elemente. Elemente, deren Import in TIA scheiterte (Status 500), werden erneut versucht,
    /// solange in einer Runde mindestens ein anderes Element importiert wurde; so lösen sich Abhängigkeiten
    /// zwischen den Elementen (z. B. ein UDT, der einen anderen UDT der Liste verwendet) unabhängig von der Reihenfolge.
    /// </summary>
    static public void ImportAll<TItem, TResult>(FileBatchResult<TResult> batch, IList<TItem> items, Func<TItem, ItemResult<TResult>> import, Func<TItem, string> name, Func<TItem, string[]> path) {
      var failures = new Dictionary<int, ItemResult<TResult>>();
      var pending = Enumerable.Range(0, items.Count).ToList();
      while (pending.Count > 0) {
        var retry = new List<int>();
        bool progress = false;
        foreach (int index in pending) {
          ItemResult<TResult> result = import(items[index]);
          if (result.Failed) {
            failures[index] = result;
            if (result.Status == HttpStatusCode.InternalServerError) {
              retry.Add(index);
            }
          } else {
            failures.Remove(index);
            batch.Items.Add(result.Value);
            progress = true;
          }
        }
        if (!progress) {
          break;
        }
        pending = retry;
      }
      foreach (var failure in failures.OrderBy(f => f.Key)) {
        TItem item = items[failure.Key];
        batch.Errors.Add(new FileBatchError { Name = name(item), Path = path(item), StatusCode = (int)failure.Value.Status, Error = failure.Value.Error });
      }
    }

    /// <summary>
    /// Liest eine Namensliste aus der Query; jeder Name ist ein eigener Parameter (blockName=A&amp;blockName=B).
    /// </summary>
    /// <returns>Getrimmte, eindeutige Namen oder null, wenn keiner angegeben ist</returns>
    static public string[] ReadNameList(HttpListenerContext context, string parameter) {
      string[] values = context.Request.QueryString.GetValues(parameter)?
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
      return values != null && values.Length > 0 ? values : null;
    }

    /// <summary>
    /// Liest den Query-Parameter "recursive" (Standard: true).
    /// </summary>
    static public bool ReadRecursive(HttpListenerContext context, out string error) {
      error = null;
      string value = context.Request.QueryString["recursive"];
      if (string.IsNullOrWhiteSpace(value)) {
        return true;
      }
      if (bool.TryParse(value.Trim(), out bool recursive)) {
        return recursive;
      }
      error = $"Error: Invalid value '{value}' for recursive. Allowed: true, false.";
      return true;
    }

    /// <summary>
    /// Prüft die PLC-Zielangaben eines Export-Bodies (ProcessId, ProjectName, DeviceName, DeviceItemName).
    /// </summary>
    /// <returns>Fehlermeldung oder null</returns>
    static public string ValidatePlcTarget(PlcTargetBody target, out string processIdStr, out string projectName, out string deviceName, out string deviceItemName) {
      processIdStr = null;
      projectName = null;
      deviceName = null;
      deviceItemName = null;
      if (target == null) {
        return "Error: Request body is required.";
      }
      if (target.ProcessId <= 0) {
        return "Error: ProcessId must be a positive integer.";
      }
      if (string.IsNullOrWhiteSpace(target.ProjectName)) {
        return "Error: ProjectName must be provided.";
      }
      if (string.IsNullOrWhiteSpace(target.DeviceName)) {
        return "Error: DeviceName must be provided.";
      }
      if (string.IsNullOrWhiteSpace(target.DeviceItemName)) {
        return "Error: DeviceItemName must be provided.";
      }
      processIdStr = target.ProcessId.ToString();
      projectName = target.ProjectName.Trim();
      deviceName = target.DeviceName.Trim();
      deviceItemName = target.DeviceItemName.Trim();
      return null;
    }

    /// <summary>
    /// Liest und prüft einen GetDir-Request-Body.
    /// </summary>
    /// <returns>Fehlermeldung oder null</returns>
    static public string ReadGetDirRequest(HttpListenerContext context, out GetDirRequest request, out string processIdStr, out string projectName, out string deviceName, out string deviceItemName, out string directory, out bool recursive) {
      request = null;
      processIdStr = null;
      projectName = null;
      deviceName = null;
      deviceItemName = null;
      directory = null;
      recursive = true;
      try {
        request = ReadBody<GetDirRequest>(context);
      } catch (Exception ex) {
        return $"Error: Invalid request body: {ex.Message}";
      }
      string targetError = ValidatePlcTarget(request, out processIdStr, out projectName, out deviceName, out deviceItemName);
      if (targetError != null) {
        return targetError;
      }
      if (request.Path != null && request.Path.Any(string.IsNullOrWhiteSpace)) {
        return "Error: Path must not contain empty folder names.";
      }
      string dirError = cTiaExportHelpers.ResolveDirectory(request.Directory, out directory);
      if (dirError != null) {
        return dirError;
      }
      recursive = request.Recursive ?? true;
      return null;
    }

    /// <summary>
    /// Ermittelt die Objektart einer Export-Datei anhand ihres Inhalts.
    /// </summary>
    /// <returns>KindBlock, KindUdt, KindTagTable oder null, wenn die Art nicht erkannt wird</returns>
    static public string DetectKind(string file) {
      string text;
      try {
        text = File.ReadAllText(file);
      } catch {
        return null;
      }
      if (file.EndsWith(".s7dcl", StringComparison.OrdinalIgnoreCase)) {
        Match keyword = Regex.Match(text, @"^\s*(TYPE|FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK)\b", RegexOptions.Multiline);
        if (!keyword.Success) {
          return null;
        }
        return keyword.Groups[1].Value == "TYPE" ? KindUdt : KindBlock;
      }
      Match element = Regex.Match(text, @"<SW\.(Blocks|Types|Tags)\.(\w+)");
      if (!element.Success) {
        return null;
      }
      switch (element.Groups[1].Value) {
        case "Blocks":
          return KindBlock;
        case "Types":
          return KindUdt;
        default:
          return element.Groups[2].Value == "PlcTagTable" ? KindTagTable : null;
      }
    }

    /// <summary>
    /// Sucht in einem absoluten Dateisystem-Verzeichnis (optional mit Unterverzeichnissen) die Dateien einer Objektart.
    /// path ist der TIA-Ordner, der diesem Verzeichnis entspricht; Unterverzeichnisse werden an path angehängt.
    /// Je Element gibt es eine Inhaltsdatei &lt;Name&gt;.s7dcl (SD, nicht bei Tagtabellen) oder &lt;Name&gt;.xml;
    /// Dateien anderer Objektarten werden übersprungen.
    /// </summary>
    static public ItemResult<List<DirEntry>> ScanDirectory<T>(string directory, string[] path, bool recursive, string kind, string preferredFormat, FileBatchResult<T> batch) {
      string errorMessage = cTiaExportHelpers.ResolveDirectory(directory, out string fullDirectory);
      if (errorMessage != null) {
        return ItemResult<List<DirEntry>>.Fail(HttpStatusCode.BadRequest, errorMessage);
      }
      batch.Directory = fullDirectory;
      if (!Directory.Exists(fullDirectory)) {
        return ItemResult<List<DirEntry>>.Fail(HttpStatusCode.NotFound, $"Error: Directory not found: {fullDirectory}.");
      }
      var entries = new List<DirEntry>();
      ScanDirectory(fullDirectory, path ?? new string[0], recursive, kind, preferredFormat, entries, batch);
      return ItemResult<List<DirEntry>>.Ok(entries);
    }

    static private void ScanDirectory<T>(string directory, string[] path, bool recursive, string kind, string preferredFormat, List<DirEntry> entries, FileBatchResult<T> batch) {
      var baseNames = Directory.GetFiles(directory)
        .Where(f => f.EndsWith(".s7dcl", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFileNameWithoutExtension)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

      foreach (string name in baseNames) {
        var candidates = new List<DirEntry>();
        foreach (var option in new[] { new { Format = FormatSd, Extension = ".s7dcl" }, new { Format = FormatXml, Extension = ".xml" } }) {
          string file = Path.Combine(directory, name + option.Extension);
          if (!File.Exists(file)) {
            continue;
          }
          string fileKind = DetectKind(file);
          if (fileKind == kind && !(kind == KindTagTable && option.Format == FormatSd)) {
            candidates.Add(new DirEntry { Name = name, Path = path, Directory = directory, Format = option.Format, File = file });
          } else {
            batch.Skipped.Add(new FileBatchSkipped {
              Name = name,
              File = file,
              Reason = fileKind == null ? "content not recognized as TIA export" : $"{fileKind} file"
            });
          }
        }

        if (candidates.Count == 1) {
          entries.Add(candidates[0]);
        } else if (candidates.Count > 1) {
          DirEntry preferred = candidates.FirstOrDefault(c => c.Format == preferredFormat);
          if (preferred != null) {
            entries.Add(preferred);
          } else {
            batch.Errors.Add(new FileBatchError {
              Name = name,
              Path = path,
              Files = candidates.Select(c => c.File).ToArray(),
              StatusCode = (int)HttpStatusCode.BadRequest,
              Error = $"Error: Both .s7dcl and .xml exist for {name}. Pass Format to choose one."
            });
          }
        }
      }

      if (recursive) {
        foreach (string subDirectory in Directory.GetDirectories(directory).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) {
          string[] subPath = path.Concat(new[] { Path.GetFileName(subDirectory) }).ToArray();
          ScanDirectory(subDirectory, subPath, true, kind, preferredFormat, entries, batch);
        }
      }
    }
  }
}
