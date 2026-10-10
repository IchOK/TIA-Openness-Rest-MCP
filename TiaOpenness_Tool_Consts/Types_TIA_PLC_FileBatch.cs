using System.Collections.Generic;

namespace Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch {
  /// <summary>
  /// Request-Body für den Import aller Dateien eines Verzeichnisses (PutDir).
  /// Die Dateien werden aus Root\Projekt\Ordnerpfad gelesen; Unterverzeichnisse entsprechen Unterordnern im Projekt.
  /// </summary>
  public class PutDirRequest {
    public string RootDirectory { get; set; }  // absoluter Pfad des Wurzelverzeichnisses
    public string[] Path { get; set; }         // Ordnerpfad; null/leer = Wurzelordner
    public bool? Recursive { get; set; }       // Unterverzeichnisse einbeziehen; null = true
    public string Format { get; set; }         // bei .s7dcl und .xml mit gleichem Namen: zu verwendendes Format; leer = Fehler für dieses Element
  }

  /// <summary>
  /// Fehler eines einzelnen Elements in einer Batch-Operation.
  /// </summary>
  public class FileBatchError {
    public string Name { get; set; }           // Name des Elements (bei Import: Dateiname ohne Endung)
    public string[] Path { get; set; }         // Ordnerpfad des Elements
    public string[] Files { get; set; }        // betroffene Dateien, falls bekannt
    public int StatusCode { get; set; }        // HTTP-Status, den der Einzelaufruf geliefert hätte
    public string Error { get; set; }
  }

  /// <summary>
  /// Übersprungene Datei bzw. übersprungenes Element einer Batch-Operation.
  /// </summary>
  public class FileBatchSkipped {
    public string Name { get; set; }
    public string File { get; set; }           // nur bei Import aus Verzeichnissen
    public string Reason { get; set; }
  }

  /// <summary>
  /// Ergebnis einer Batch-Operation (GetDir, PutDir, GetFileList, PutFileList).
  /// </summary>
  public class FileBatchResult<T> {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string Directory { get; set; }      // nur bei GetDir/PutDir: Verzeichnis zum angegebenen Ordnerpfad
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<T> Items { get; set; } = new List<T>();
    public List<FileBatchError> Errors { get; set; } = new List<FileBatchError>();
    public List<FileBatchSkipped> Skipped { get; set; } = new List<FileBatchSkipped>();
  }
}
