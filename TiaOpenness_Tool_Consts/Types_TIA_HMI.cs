using System.Collections.Generic;

namespace Tophinke.TiaOpenness.Tool.Types.HMI {
  /// <summary>
  /// Ein Gerät mit WinCC Unified-Software (Panel oder PC-Runtime).
  /// </summary>
  public class DeviceInfo {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string HmiName { get; set; }
    public string TypeIdentifier { get; set; }   // Bestellnummer/Version des Geräts
    public List<ConnectionInfo> Connections { get; set; }
    public int TagTableCount { get; set; }
    public int TagCount { get; set; }
    public int DiscreteAlarmCount { get; set; }
    public int AnalogAlarmCount { get; set; }
    public int AlarmClassCount { get; set; }
    public int DataLogCount { get; set; }
    public int AlarmLogCount { get; set; }
    public int ScreenCount { get; set; }
  }

  /// <summary>
  /// Eine HMI-Variable. Beim Schreiben (PUT) bleiben Felder mit null unverändert; Werte werden als Text übergeben.
  /// </summary>
  public class TagInfo {
    public string Name { get; set; }
    public string TagTableName { get; set; }     // beim Anlegen: Zieltabelle (wird bei Bedarf angelegt); Standard "Default tag table"
    public string Connection { get; set; }       // HMI-Verbindung, z. B. "Conn_PLC_LIB"; leer = interne Variable
    public string PlcTag { get; set; }           // PLC-Variable bei symbolischem Zugriff, z. B. "pvHmiMsgArea_HMI_Uni.PN1_Diag[0]"
    public string Address { get; set; }          // Adresse bei absolutem Zugriff
    public string AccessMode { get; set; }       // "SymbolicAccess" oder "AbsoluteAccess"
    public string DataType { get; set; }         // PLC-Datentyp bzw. Datentyp interner Variablen, z. B. "Word"
    public string HmiDataType { get; set; }      // Datentyp in der HMI
    public string AcquisitionMode { get; set; }  // "OnDemand", "CyclicOnUse", "CyclicContinuous"
    public string AcquisitionCycle { get; set; } // z. B. "T1s"
    public string InitialValue { get; set; }
    public bool? Persistent { get; set; }
    public bool? LinearScaling { get; set; }
    public string PlcStartValue { get; set; }
    public string PlcEndValue { get; set; }
    public string HmiStartValue { get; set; }
    public string HmiEndValue { get; set; }
    public string MinValue { get; set; }         // Untergrenze (Konstante)
    public string MaxValue { get; set; }         // Obergrenze (Konstante)
    public string Comment { get; set; }          // Text in der Editiersprache des Projekts
    public List<LoggingTagInfo> LoggingTags { get; set; }  // beim Schreiben: Archivvariablen anlegen/ändern (keine Löschung)
  }

  /// <summary>
  /// Archivvariable einer HMI-Variablen.
  /// </summary>
  public class LoggingTagInfo {
    public string Name { get; set; }
    public string DataLog { get; set; }          // Datenarchiv
    public string LoggingMode { get; set; }      // "Cyclic", "OnChange", "OnDemand"
    public string Cycle { get; set; }            // Erfassungszyklus, z. B. "T1s"
    public uint? CycleFactor { get; set; }
    public string AggregationMode { get; set; }  // z. B. "NoAggregation", "Average", "Maximum"
    public string SmoothingMode { get; set; }    // z. B. "NoSmoothing", "Value", "SwingingDoor"
    public double? SmoothingDeltaValue { get; set; }
    public string LimitScope { get; set; }       // z. B. "NoLimitsUsed", "WithinLimits"
    public string LowLimit { get; set; }
    public string HighLimit { get; set; }
    public string TriggerMode { get; set; }      // "None", "RisingEdge", "FallingEdge", "RisingAndFallingEdge"
    public string TriggerTag { get; set; }
    public uint? TriggerTagBitNumber { get; set; }
  }

  /// <summary>
  /// Bitmeldung (Kind "Discrete") oder Analogmeldung (Kind "Analog"). Beim Schreiben bleiben Felder mit null unverändert.
  /// </summary>
  public class AlarmInfo {
    public string Kind { get; set; }             // "Discrete" oder "Analog"
    public string Name { get; set; }
    public uint? Id { get; set; }
    public string AlarmClass { get; set; }
    public byte? Priority { get; set; }
    public string Area { get; set; }
    public string Origin { get; set; }
    public string RaisedStateTag { get; set; }   // Triggervariable
    public uint? RaisedStateTagBitNumber { get; set; }   // nur Bitmeldung
    public string TriggerMode { get; set; }      // nur Bitmeldung: "OnRisingEdge" oder "OnFallingEdge"
    public string AcknowledgmentControlTag { get; set; }
    public int? AcknowledgmentControlTagBitNumber { get; set; }
    public string AcknowledgmentStateTag { get; set; }
    public int? AcknowledgmentStateTagBitNumber { get; set; }
    public string Condition { get; set; }        // nur Analogmeldung: "UpperLimit", "LowerLimit", "Equal", ...
    public string ConditionValue { get; set; }   // nur Analogmeldung
    public string EventText { get; set; }        // Meldetext in der Editiersprache
    public Dictionary<string, string> EventTexts { get; set; }  // Meldetext je Sprache, z. B. "de-DE"
    public string InfoText { get; set; }         // Infotext in der Editiersprache
  }

  /// <summary>
  /// Meldeklasse. Beim Schreiben bleiben Felder mit null unverändert.
  /// </summary>
  public class AlarmClassInfo {
    public string Name { get; set; }
    public uint? Id { get; set; }
    public byte? Priority { get; set; }
    public string StateMachine { get; set; }     // z. B. "RaiseClearRequiresAcknowledgement"
    public string Log { get; set; }              // Meldearchiv
    public bool? IsSystem { get; set; }
    public string CommonAlarmClass { get; set; }
  }

  /// <summary>
  /// Datenarchiv (Kind "Data") oder Meldearchiv (Kind "Alarm"). Beim Schreiben bleiben Felder mit null unverändert.
  /// </summary>
  public class LogInfo {
    public string Kind { get; set; }             // "Data" oder "Alarm"
    public string Name { get; set; }
    public string StorageDevice { get; set; }    // z. B. "Default", "Local", "SDX51", "USBX61"
    public string StorageFolder { get; set; }
    public uint? LogMaxSize { get; set; }        // MB
    public string LogTimePeriod { get; set; }    // Dauer "d.hh:mm:ss"
    public uint? SegmentMaxSize { get; set; }    // MB
    public string SegmentTimePeriod { get; set; } // Dauer "d.hh:mm:ss"
    public string BackupMode { get; set; }       // "NoBackup" oder "PrimaryPath"
    public string BackupPath { get; set; }
  }

  /// <summary>
  /// Ergebnis für ein einzelnes Objekt eines Schreib- oder Löschauftrags.
  /// </summary>
  public class Change {
    public string Kind { get; set; }             // z. B. "Tag", "LoggingTag", "DiscreteAlarm", "AnalogAlarm", "AlarmClass", "DataLog", "AlarmLog"
    public string Name { get; set; }
    public string Action { get; set; }           // "Created", "Updated", "Deleted", "Unchanged"
    public string Details { get; set; }          // geänderte Felder
    public string Error { get; set; }            // null bei Erfolg
  }

  /// <summary>
  /// Ergebnis eines Schreib- oder Löschauftrags.
  /// </summary>
  public class ChangeResult {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string HmiName { get; set; }
    public List<Change> Changes { get; set; }
  }

  /// <summary>
  /// Request-Body für das Löschen von Objekten über ihren Namen.
  /// </summary>
  public class DeleteRequest {
    public List<string> Names { get; set; }
  }

  /// <summary>
  /// Ergebnis einer List-Funktion eines Unified-Geräts.
  /// </summary>
  public class ListResult<T> {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string HmiName { get; set; }
    public List<T> Items { get; set; }
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Ändern von HMI-Variablen (PUT).
  /// </summary>
  public class TagPutRequest {
    public List<TagInfo> Tags { get; set; }
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Ändern von Meldungen (PUT).
  /// </summary>
  public class AlarmPutRequest {
    public List<AlarmInfo> Alarms { get; set; }
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Ändern von Meldeklassen (PUT).
  /// </summary>
  public class AlarmClassPutRequest {
    public List<AlarmClassInfo> AlarmClasses { get; set; }
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Ändern von Archiven (PUT).
  /// </summary>
  public class LogPutRequest {
    public List<LogInfo> Logs { get; set; }
  }

  /// <summary>
  /// Request-Body für den Import von HMI-Variablen aus Dateien (PUT).
  /// </summary>
  public class TagPutFileRequest {
    public string RootDirectory { get; set; }    // absoluter Pfad des Wurzelverzeichnisses
  }

  /// <summary>
  /// Ergebnis des Exports bzw. Imports der HMI-Variablen als Dateien.
  /// </summary>
  public class TagFileData {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string HmiName { get; set; }
    public string Directory { get; set; }        // Root\Projekt\HmiName\Tags
    public string[] Files { get; set; }
  }

  /// <summary>
  /// Eine HMI-Verbindung (zur PLC).
  /// </summary>
  public class ConnectionInfo {
    public string Name { get; set; }
    public string CommunicationDriver { get; set; }
    public string Partner { get; set; }         // verbundene PLC
    public string Station { get; set; }
    public string Node { get; set; }
    public string Comment { get; set; }
  }
}
