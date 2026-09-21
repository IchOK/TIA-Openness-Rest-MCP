using System;
using System.Collections.Generic;
using System.Text;

namespace Tophinke.TiaOpenness.Tool.Types.Block {
  public class Info {
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
    public string Format { get; set; }         // "SimaticData/SD" oder "SimaticML/XML"
    public string Content { get; set; }
    public string MultiLingualText { get; set; } 
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

