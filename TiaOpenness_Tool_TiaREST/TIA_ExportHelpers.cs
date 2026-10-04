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
      // Openness ExportAsDocuments verlangt einen absoluten Pfad (kein relativer directoryInfo).
      string exportRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export"));
      Directory.CreateDirectory(exportRoot);
      return new DirectoryInfo(exportRoot);
    }

    /// <summary>
    /// Erstellt das Import-Verzeichnis, falls es nicht existiert.
    /// </summary>
    /// <returns>Import-Verzeichnis</returns>
    static public DirectoryInfo EnsureImportDirectory() {
      string importRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "import"));
      Directory.CreateDirectory(importRoot);
      return new DirectoryInfo(importRoot);
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
