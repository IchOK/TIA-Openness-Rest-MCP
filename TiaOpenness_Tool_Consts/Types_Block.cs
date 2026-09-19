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
    public string Format { get; set; }         // "SimaticData/SD" oder "SimaticML/XML"
    public string Content { get; set; }
  }
}

