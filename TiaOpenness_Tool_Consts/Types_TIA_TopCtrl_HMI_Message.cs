using System.Collections.Generic;
using Tophinke.TiaOpenness.Tool.Types.HMI;

namespace Tophinke.TiaOpenness.Tool.Types.TopCtrl.HMI.Message {
  /// <summary>
  /// Request-Body für den Abgleich der Unified-Bitmeldungen mit der PLC-Meldungsliste.
  /// </summary>
  public class SyncRequest {
    public string ConnectionName { get; set; }      // HMI-Verbindung zur PLC; ohne Angabe die einzige Verbindung mit Partner
    public string PlcDeviceName { get; set; }       // ohne Angabe: Station der Verbindung
    public string PlcDeviceItemName { get; set; }   // ohne Angabe: Partner der Verbindung
    public string TagTableName { get; set; }        // Tabelle für neue Variablen; Standard "tbMsg_Unified"
    public string HmiVariablePattern { get; set; }  // Filter auf die Treibervariable mit * und ?, z. B. "*PN1_Diag*"
    public Dictionary<string, string> AlarmClassMap { get; set; }  // Meldeart (Zahl oder Konstante) -> Meldeklasse; ergänzt die Standardzuordnung
    public string DefaultAlarmClass { get; set; }   // Meldeklasse für Meldearten ohne Zuordnung
    public bool IncludeUnselected { get; set; }     // auch Meldungen mit stConfig.xSelect = FALSE
    public bool DeleteObsolete { get; set; }        // Bitmeldungen an den Treibervariablen ohne PLC-Meldung löschen
    public bool DryRun { get; set; }                // nur planen, nichts ändern
  }

  /// <summary>
  /// Ergebnis des Abgleichs.
  /// </summary>
  public class SyncResult {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string HmiName { get; set; }
    public string Connection { get; set; }
    public string PlcDeviceName { get; set; }
    public string PlcDeviceItemName { get; set; }
    public string TagTableName { get; set; }
    public bool DryRun { get; set; }
    public int MessageCount { get; set; }           // Meldungen der Unified-Treiber dieser PLC
    public int TagCount { get; set; }
    public int AlarmCount { get; set; }
    public List<TagInfo> Tags { get; set; }         // nur bei DryRun: geplante Variablen
    public List<AlarmInfo> Alarms { get; set; }     // nur bei DryRun: geplante Bitmeldungen
    public int UnchangedCount { get; set; }         // Objekte ohne Änderung (nicht in Changes aufgeführt)
    public List<Change> Changes { get; set; } = new List<Change>();
    public List<string> Warnings { get; set; } = new List<string>();
  }
}
