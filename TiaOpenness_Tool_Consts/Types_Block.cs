using System;
using System.Collections.Generic;
using System.Text;

namespace Tophinke.TiaOpenness.Tool.Types.Block {
  /// <summary>
  /// Bausteine einer PLC-Software (Ergebnis der List-Funktion).
  /// </summary>
  public class PlcList {
    public string DeviceName { get; set; }     // z. B. "PLC_1" (Ebene 1: Device)
    public string DeviceItemName { get; set; } // z. B. "CPU 1516-3 PN/DP" (Ebene 2: DeviceItem)
    public string PlcName { get; set; }        // z. B. "PLC_1" (PlcSoftware)
    public List<Info> Blocks { get; set; }
  }

  public class Info {
    public string BlockName { get; set; }      // z. B. "MotorData"
    public int BlockNumber { get; set; }       // z. B. 10
    public string BlockType { get; set; }      // z. B. "GlobalDB"
    public string ProgrammingLanguage { get; set; } // z. B. "LAD", "SCL", "DB"
    public string[] Path { get; set; }         // z. B. ["Plant", "Sub", "Unit", ...]
  }

  public class  Data {
    // Eindeutige Hardware-Kennungen
    public string DeviceName { get; set; }     // z. B. "PLC_1" (Ebene 1: Device)
    public string DeviceItemName { get; set; } // z. B. "CPU 1516-3 PN/DP" (Ebene 2: DeviceItem)

    // Software- & Bausteininformationen
    public string PlcName { get; set; }        // z. B. "PLC_1" (PlcSoftware)
    public string BlockName { get; set; }      // z. B. "MotorData"
    public int BlockNumber { get; set; }       // z. B. 10
    public string BlockType { get; set; }      // z. B. "GlobalDB"
    public string ProgrammingLanguage { get; set; } // z. B. "LAD", "SCL", "DB"
    public string[] Path { get; set; }         // z. B. ["Plant", "Sub", "Unit", ...]
    public string Format { get; set; }         // "SimaticData/SD" oder "SimaticML/XML"
    public string Content { get; set; }
    public string MultiLingualText { get; set; } 
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Überschreiben eines Bausteins (PUT).
  /// </summary>
  public class PutRequest {
    public string[] Path { get; set; }         // Zielordner; null = bisheriger Ordner bzw. Wurzelordner
    public string Format { get; set; }         // "SimaticData/SD" oder "SimaticML/XML"; leer = aus Content ableiten
    public string Content { get; set; }        // .s7dcl- oder SimaticML-Inhalt
    public string MultiLingualText { get; set; } // optionaler .s7res-Inhalt (nur SD)
    public int? BlockNumber { get; set; }      // gewünschte Nummer; null = bisherige Nummer bzw. automatisch
  }

  /// <summary>
  /// Ergebnis des Anlegens oder Überschreibens eines Bausteins (PUT).
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
  /// Neuer Startwert für eine Variable eines Datenbausteins.
  /// </summary>
  public class StartValueUpdate {
    public string Member { get; set; }         // z. B. "Motor.Speed" oder "Values[3]"
    public string Value { get; set; }          // z. B. "100", "TRUE", "16#FF"
  }

  /// <summary>
  /// Request-Body für das Anpassen von Startwerten (PATCH).
  /// </summary>
  public class PatchRequest {
    public List<StartValueUpdate> StartValues { get; set; }
  }

  /// <summary>
  /// Ergebnis einer einzelnen Startwert-Änderung.
  /// </summary>
  public class StartValueChange {
    public string Member { get; set; }
    public string OldValue { get; set; }
    public string NewValue { get; set; }
    public string Error { get; set; }          // null bei Erfolg
  }

  /// <summary>
  /// Ergebnis des Anpassens von Startwerten (PATCH).
  /// </summary>
  public class PatchResult {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string BlockName { get; set; }
    public int BlockNumber { get; set; }
    public string BlockType { get; set; }
    public List<StartValueChange> Changes { get; set; }
  }

  /// <summary>
  /// Ergebnis der Export-Probe für eine ProgrammingLanguage (ein Beispielbaustein).
  /// </summary>
  public class ExportCapabilityInfo {
    public string ProgrammingLanguage { get; set; }
    public string SampleBlockName { get; set; }
    public string SampleBlockType { get; set; }
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }

    public bool DocumentsSupported { get; set; }
    public string[] DocumentsExtensions { get; set; }
    public string DocumentsError { get; set; }

    public bool XmlSupported { get; set; }
    public string XmlExtension { get; set; }
    public string XmlError { get; set; }
  }
}

