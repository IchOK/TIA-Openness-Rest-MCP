using System;
using System.Collections.Generic;
using System.Text;

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
  }

  public class  Data {
    // Eindeutige Hardware-Kennungen
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }

    // Software- & Bausteininformationen
    public string PlcName { get; set; }
    public string UdtName { get; set; }
    public string[] Path { get; set; }         // z. B. ["Plant", "Sub", "Unit", ...]
    public string Format { get; set; }
    public string Content { get; set; }
    public string MultiLingualText { get; set; }
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Überschreiben eines Datentyps (PUT).
  /// </summary>
  public class PutRequest {
    public string[] Path { get; set; }         // Zielordner für neue UDTs; null = Wurzelordner
    public string Format { get; set; }         // "SimaticData/SD" oder "SimaticML/XML"; leer = aus Content ableiten
    public string Content { get; set; }        // .s7dcl- oder SimaticML-Inhalt
    public string MultiLingualText { get; set; } // optionaler .s7res-Inhalt (nur SD)
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
}

