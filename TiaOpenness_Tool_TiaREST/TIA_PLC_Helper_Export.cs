using Siemens.Engineering;
using Siemens.Engineering.SW;
using System;
using System.IO;
using System.Linq;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper {
  /// <summary>
  /// Gemeinsame Hilfsmethoden fuer Export-Dateien im Verzeichnis "export".
  /// </summary>
  static internal class cTiaExportHelpers {
    /// <summary>
    /// Erstellt das Export-Verzeichnis, falls es nicht existiert.
    /// </summary>
    /// <returns>Export-Verzeichnis</returns>
    static public DirectoryInfo EnsureExportDirectory() {
      // Openness ExportAsDocuments verlangt einen absoluten Pfad (kein relativer directoryInfo).
      string exportRoot = Path.Combine(WorkRoot, "export");
      Directory.CreateDirectory(exportRoot);
      return new DirectoryInfo(exportRoot);
    }

    /// <summary>
    /// Erstellt das Import-Verzeichnis, falls es nicht existiert.
    /// </summary>
    /// <returns>Import-Verzeichnis</returns>
    static public DirectoryInfo EnsureImportDirectory() {
      string importRoot = Path.Combine(WorkRoot, "import");
      Directory.CreateDirectory(importRoot);
      return new DirectoryInfo(importRoot);
    }

    static private string WorkRoot => RestSettings.Current.WorkDirectory;

    /// <summary>
    /// Bereinigt einen Dateinamen, indem ungültige Zeichen durch Unterstriche ersetzt werden.
    /// </summary>
    /// <param name="name">Dateiname</param>
    /// <returns>Sanitisierten Dateinamen</returns>
    static public string SanitizeFileName(string name) {
      if (string.IsNullOrWhiteSpace(name)) {
        return "export";
      }
      foreach (char c in Path.GetInvalidFileNameChars()) {
        name = name.Replace(c, '_');
      }
      return name;
    }

    /// <summary>
    /// Löscht eine Export-Datei mit dem angegebenen Basisnamen in einem Verzeichnis.
    /// </summary>
    /// <param name="dirName">Verzeichnisname</param>
    /// <param name="baseName">Basisname</param>
    static public void TryDeleteExportedDocument(string dirName, string baseName) {
      try {
        if (!Directory.Exists(dirName)) {
          return;
        }
        FileInfo[] files = new DirectoryInfo(dirName).GetFiles(baseName + ".*");
        foreach (FileInfo file in files) {
          file.Delete();
        }
      } catch {
      }
    }

    /// <summary>
    /// Löscht die Dateien mit dem angegebenen Basisnamen und den angegebenen Endungen in einem Verzeichnis.
    /// Andere Dateien gleichen Basisnamens bleiben erhalten.
    /// </summary>
    /// <param name="dirName">Verzeichnisname</param>
    /// <param name="baseName">Basisname</param>
    /// <param name="extensions">Dateiendungen inkl. Punkt, z. B. ".s7dcl"</param>
    static public void DeleteFiles(string dirName, string baseName, params string[] extensions) {
      foreach (string extension in extensions) {
        string file = Path.Combine(dirName, baseName + extension);
        if (File.Exists(file)) {
          File.Delete(file);
        }
      }
    }

    /// <summary>
    /// Prüft das vom Aufrufer übergebene Wurzelverzeichnis für den Dateiaustausch.
    /// </summary>
    /// <param name="rootDirectory">Absoluter Pfad des Wurzelverzeichnisses</param>
    /// <param name="fullRoot">Out-Parameter mit dem normalisierten Pfad</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static public string ResolveRootDirectory(string rootDirectory, out string fullRoot) {
      fullRoot = null;
      if (string.IsNullOrWhiteSpace(rootDirectory)) {
        return "Error: rootDirectory must be provided.";
      }
      if (!Path.IsPathRooted(rootDirectory)) {
        return $"Error: rootDirectory must be an absolute path: {rootDirectory}";
      }
      try {
        fullRoot = Path.GetFullPath(rootDirectory.Trim());
        return null;
      } catch (Exception ex) {
        return $"Error: Invalid rootDirectory '{rootDirectory}': {ex.Message}";
      }
    }

    /// <summary>
    /// Liefert das Dateiverzeichnis eines Objekts: Root\Projekt\Ordnerpfad.
    /// Ungültige Zeichen in Projekt- und Ordnernamen werden durch Unterstriche ersetzt.
    /// </summary>
    /// <param name="fullRoot">Wurzelverzeichnis aus ResolveRootDirectory</param>
    /// <param name="projectName">Name des TIA-Projekts</param>
    /// <param name="path">Ordnernamen vom Wurzelordner des Objekts aus</param>
    /// <param name="directory">Out-Parameter mit dem vollständigen Verzeichnispfad</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static public string GetFileDirectory(string fullRoot, string projectName, string[] path, out string directory) {
      string[] segments = new[] { fullRoot, SanitizeFileName(projectName) }
        .Concat((path ?? new string[0]).Select(SanitizeFileName))
        .ToArray();
      directory = Path.GetFullPath(Path.Combine(segments));
      string rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
      if (!directory.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) {
        string invalid = directory;
        directory = null;
        return $"Error: Folder path resolves to '{invalid}', which is outside rootDirectory '{fullRoot}'.";
      }
      return null;
    }

    /// <summary>
    /// Formatierung der Export-Nachrichten.
    /// </summary>
    /// <param name="exportResult">Export-Ergebnis</param>
    /// <returns>Formatierte Export-Nachrichten</returns>
    static public string FormatExportMessages(DocumentExportResult exportResult) {
      if (exportResult == null) {
        return null;
      }
      string[] parts = GetMessages(exportResult.Messages);
      if (parts.Length == 0) {
        return exportResult.State.ToString();
      }
      return string.Join("; ", parts);
    }

    /// <summary>
    /// Formatierung der Import-Nachrichten.
    /// </summary>
    /// <param name="importResult">Import-Ergebnis</param>
    /// <returns>Formatierte Import-Nachrichten</returns>
    static public string FormatImportMessages(DocumentImportResult importResult) {
      if (importResult == null) {
        return null;
      }
      string[] parts = GetMessages(importResult.Messages);
      if (parts.Length == 0) {
        return importResult.State.ToString();
      }
      return string.Join("; ", parts);
    }

    /// <summary>
    /// Liefert die nicht-leeren Meldungstexte eines Export- oder Import-Ergebnisses.
    /// </summary>
    /// <param name="messages">Meldungen des Ergebnisses</param>
    /// <returns>Array mit den Meldungstexten</returns>
    static public string[] GetMessages(DocumentResultMessageComposition messages) {
      var parts = new System.Collections.Generic.List<string>();
      if (messages == null) {
        return parts.ToArray();
      }
      foreach (DocumentResultMessage message in messages) {
        if (message != null && !string.IsNullOrWhiteSpace(message.Message)) {
          parts.Add(message.Message);
        }
      }
      return parts.ToArray();
    }
  }
}
