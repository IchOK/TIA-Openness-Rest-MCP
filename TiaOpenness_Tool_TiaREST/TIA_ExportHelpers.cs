using Siemens.Engineering;
using Siemens.Engineering.SW;
using System;
using System.IO;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Gemeinsame Hilfsmethoden fuer Export-Dateien im Verzeichnis "export".
  /// </summary>
  static internal class cTiaExportHelpers {
    /// <summary>
    /// Erstellt das Export-Verzeichnis, falls es nicht existiert.
    /// </summary>
    /// <returns>Export-Verzeichnis</returns>
    static public DirectoryInfo EnsureExportDirectory() {
      string exportRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export");
      return Directory.CreateDirectory(exportRoot);
    }

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
    /// Sucht nach einer Export-Datei mit dem angegebenen Basisnamen in einem Verzeichnis.
    /// </summary>
    /// <param name="directory">Verzeichnis</param>
    /// <param name="baseName">Basisname</param>
    /// <returns>Gefundene Export-Datei oder null</returns>
    static public FileInfo FindExportedDocument(DirectoryInfo directory, string baseName) {
      FileInfo preferred = new FileInfo(Path.Combine(directory.FullName, baseName + ".s7dcl"));
      if (preferred.Exists) {
        return preferred;
      }
      FileInfo[] matches = directory.GetFiles(baseName + ".*");
      if (matches.Length > 0) {
        return matches[0];
      }
      FileInfo[] any = directory.GetFiles();
      return any.Length > 0 ? any[0] : null;
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
    /// Formatierung der Export-Nachrichten.
    /// </summary>
    /// <param name="exportResult">Export-Ergebnis</param>
    /// <returns>Formatierte Export-Nachrichten</returns>
    static public string FormatExportMessages(DocumentExportResult exportResult) {
      if (exportResult == null || exportResult.Messages == null) {
        return exportResult != null ? exportResult.State.ToString() : null;
      }
      var parts = new System.Collections.Generic.List<string>();
      foreach (DocumentResultMessage message in exportResult.Messages) {
        if (message != null && !string.IsNullOrWhiteSpace(message.Message)) {
          parts.Add(message.Message);
        }
      }
      if (parts.Count == 0) {
        return exportResult.State.ToString();
      }
      return string.Join("; ", parts);
    }
  }
}
