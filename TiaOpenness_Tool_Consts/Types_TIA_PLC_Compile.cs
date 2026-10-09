using System.Collections.Generic;

namespace Tophinke.TiaOpenness.Tool.Types.PLC.Compile {
  /// <summary>
  /// Einzelne Meldung des Übersetzens (Fehler oder Warnung).
  /// </summary>
  public class CompileMessage {
    public string[] Location { get; set; }     // Pfad der Meldung im Übersetzungsergebnis, z. B. ["PLC_1", "Program blocks", "Main (OB1)", "12"]
    public string BlockName { get; set; }      // Baustein aus Location ("Main (OB1)" -> "Main"); null, wenn die Meldung keinem Baustein zugeordnet ist
    public string State { get; set; }          // "Error" oder "Warning"
    public string Description { get; set; }    // Meldungstext von TIA Portal
  }

  /// <summary>
  /// Ergebnis des Übersetzens eines Bausteins, Datentyps oder einer PLC.
  /// </summary>
  public class CompileResult {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string Target { get; set; }         // "Block", "UDT" oder "PLC"
    public string Name { get; set; }           // Baustein-/Datentypname bzw. PLC-Name
    public string[] Path { get; set; }         // Ordner des Bausteins/Datentyps; null bei PLC
    public string State { get; set; }          // "Success", "Information", "Warning" oder "Error"
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<CompileMessage> Messages { get; set; }
  }
}
