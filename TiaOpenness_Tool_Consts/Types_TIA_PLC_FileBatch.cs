using System.Collections.Generic;

namespace Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch {
  /// <summary>
  /// Gemeinsame Felder der PLC-Datei-Export-Bodies: TIA-Sitzung und PLC-Ziel.
  /// </summary>
  public class PlcTargetBody {
    public int ProcessId { get; set; }
    public string ProjectName { get; set; }
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
  }

  /// <summary>
  /// Request-Body für den Import aller Dateien eines Verzeichnisses (PutDir).
  /// Directory ist das absolute Dateisystem-Verzeichnis; Path ist der TIA-Ordner, in den die Dateien dieses Verzeichnisses gehören.
  /// Unterverzeichnisse entsprechen Unterordnern unter Path.
  /// </summary>
  public class PutDirRequest {
    public string Directory { get; set; }      // absolutes Dateisystem-Verzeichnis auf dem TIA/REST-Rechner
    public string[] Path { get; set; }         // TIA-Ordner; null/leer = Wurzelordner
    public bool? Recursive { get; set; }       // Unterverzeichnisse einbeziehen; null = true
    public string Format { get; set; }         // bei .s7dcl und .xml mit gleichem Namen: zu verwendendes Format; leer = Fehler für dieses Element
  }

  /// <summary>
  /// Request-Body für den Export eines TIA-Ordners in ein Dateisystem-Verzeichnis (GetDir, HTTP POST).
  /// Alle Parameter liegen im Body; die URL enthält nur den Routenpfad.
  /// </summary>
  public class GetDirRequest : PlcTargetBody {
    public string Directory { get; set; }      // absolutes Dateisystem-Verzeichnis, das Path entspricht
    public string[] Path { get; set; }         // TIA-Startordner; null/leer = PLC-Wurzel
    public bool? Recursive { get; set; }       // Unterordner einbeziehen; null = true
  }

  /// <summary>
  /// Request-Body für den Export mehrerer Elemente in Dateien (GetFileList, HTTP POST).
  /// Alle Parameter liegen im Body; die URL enthält nur den Routenpfad.
  /// </summary>
  public class GetFileListRequest<TItem> : PlcTargetBody {
    public List<TItem> Items { get; set; }
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
    public string Directory { get; set; }      // nur bei GetDir/PutDir: das angegebene Dateisystem-Verzeichnis
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<T> Items { get; set; } = new List<T>();
    public List<FileBatchError> Errors { get; set; } = new List<FileBatchError>();
    public List<FileBatchSkipped> Skipped { get; set; } = new List<FileBatchSkipped>();
  }
}
