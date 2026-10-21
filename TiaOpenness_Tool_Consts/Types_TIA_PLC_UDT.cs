using System;
using System.Collections.Generic;
using System.Text;
using Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch;
using Tophinke.TiaOpenness.Tool.Types.PLC.Library;

namespace Tophinke.TiaOpenness.Tool.Types.PLC.UDT {
  /// <summary>
  /// Datentypen einer PLC-Software (Ergebnis der List-Funktion).
  /// </summary>
  public class PlcList {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public List<Info> Udts { get; set; }
  }

  public class Info {
    public string UdtName { get; set; }
    public string[] Path { get; set; }         // z. B. ["Plant", "Sub", "Unit", ...]
    public LibraryInfo Library { get; set; }   // null = keine Instanz eines Bibliothekstyps
    public string ModifiedDate { get; set; }   // UTC ISO-8601 (Openness PlcType.ModifiedDate)
  }

  public class  Data {
    // Eindeutige Hardware-Kennungen
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }

    // Software- & Bausteininformationen
    public string PlcName { get; set; }
    public string UdtName { get; set; }
    public string[] Path { get; set; }         // z. B. ["Plant", "Sub", "Unit", ...]
    public LibraryInfo Library { get; set; }   // null = keine Instanz eines Bibliothekstyps
    public string ModifiedDate { get; set; }   // UTC ISO-8601 (Openness PlcType.ModifiedDate)
    public string Format { get; set; }
    public string Content { get; set; }
    public string MultiLingualText { get; set; }
  }

  /// <summary>
  /// Ergebnis des Exports eines Datentyps in Dateien (GetFile).
  /// </summary>
  public class FileData : Info {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string Format { get; set; }
    public string Directory { get; set; }      // absolutes Dateisystem-Verzeichnis der Dateien
    public string[] Files { get; set; }        // vollständige Pfade der exportierten Dateien
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Überschreiben eines Datentyps (PUT).
  /// </summary>
  public class PutRequest {
    public string[] Path { get; set; }         // Zielordner für neue UDTs; null = Wurzelordner
    public string Format { get; set; }         // "SimaticData/SD" oder "SimaticML/XML"; leer = aus Content ableiten
    public string Content { get; set; }        // .s7dcl- oder SimaticML-Inhalt
    public string MultiLingualText { get; set; } // optionaler .s7res-Inhalt (nur SD)
    public string LibraryMode { get; set; }    // nur für Instanzen von Bibliothekstypen: "InTest" oder "Release"; leer = ablehnen
    public string LibraryVersion { get; set; } // Versionsnummer für "Release"; leer = Nummer der In-Test-Version
    public string LibraryAuthor { get; set; }  // Autor für "Release"; leer = Autor der bisherigen Version
    public string LibraryComment { get; set; } // Kommentar für "Release"
  }

  /// <summary>
  /// Ergebnis des Anlegens oder Überschreibens eines Datentyps (PUT).
  /// </summary>
  public class PutResult : Info {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public bool Created { get; set; }          // true = neu angelegt, false = überschrieben
    public string Format { get; set; }
    public string[] Messages { get; set; }     // Meldungen des TIA-Imports
  }

  /// <summary>
  /// Request-Body für den Export eines Datentyps in Dateien (GetFile, HTTP POST).
  /// Alle Parameter liegen im Body; die URL enthält nur den Routenpfad.
  /// </summary>
  public class GetFileRequest : PlcTargetBody {
    public string Directory { get; set; }      // absolutes Dateisystem-Verzeichnis; Dateien werden direkt dort geschrieben
    public string UdtName { get; set; }
    public string[] Path { get; set; }         // TIA-Ordner; null = ganzer PLC
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Überschreiben eines Datentyps aus Dateien (PUT).
  /// Directory ist das absolute Dateisystem-Verzeichnis mit den Dateien; Path ist der TIA-Zielordner für neue UDTs.
  /// </summary>
  public class PutFileRequest {
    public string Directory { get; set; }      // absolutes Dateisystem-Verzeichnis auf dem TIA/REST-Rechner
    public string[] Path { get; set; }         // TIA-Ordner für neue UDTs; null = bisheriger Ordner bzw. Wurzelordner
    public string Format { get; set; }         // leer = aus den vorhandenen Dateien ableiten
    public string LibraryMode { get; set; }
    public string LibraryVersion { get; set; }
    public string LibraryAuthor { get; set; }
    public string LibraryComment { get; set; }
  }

  /// <summary>
  /// Ergebnis des Verwerfens der In-Test-Version eines Bibliothekstyps.
  /// </summary>
  public class DiscardTypeVersionResult : Info {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string DiscardedVersion { get; set; } // verworfene In-Test-Version
  }

  /// <summary>
  /// Ergebnis des Anlegens oder Überschreibens eines Datentyps aus Dateien (PUT).
  /// </summary>
  public class PutFileResult : PutResult {
    public string Directory { get; set; }      // absolutes Dateisystem-Verzeichnis der Dateien
    public string[] Files { get; set; }        // vollständige Pfade der importierten Dateien
  }

  /// <summary>
  /// Ein Datentyp eines Imports aus mehreren Dateien (PutFileList); Felder wie bei PutFile.
  /// </summary>
  public class PutFileListItem {
    public string Directory { get; set; }      // absolutes Dateisystem-Verzeichnis der Dateien dieses Elements
    public string UdtName { get; set; }
    public string[] Path { get; set; }         // TIA-Ordner für neue UDTs; null = bisheriger Ordner bzw. Wurzelordner
    public string Format { get; set; }
  }

  /// <summary>
  /// Request-Body für den Import mehrerer Datentypen aus Dateien (PutFileList).
  /// </summary>
  public class PutFileListRequest {
    public List<PutFileListItem> Items { get; set; }
    public string LibraryMode { get; set; }
    public string LibraryVersion { get; set; }
    public string LibraryAuthor { get; set; }
    public string LibraryComment { get; set; }
  }
}
