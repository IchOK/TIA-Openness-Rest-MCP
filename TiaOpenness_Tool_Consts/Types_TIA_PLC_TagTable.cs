using System.Collections.Generic;

namespace Tophinke.TiaOpenness.Tool.Types.PLC.TagTable {
  /// <summary>
  /// Tagtabellen einer PLC-Software (Ergebnis der List-Funktion).
  /// </summary>
  public class PlcList {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public List<Info> TagTables { get; set; }
  }

  public class Info {
    public string TagTableName { get; set; }
    public bool IsDefault { get; set; }
    public string[] Path { get; set; }         // z. B. ["Plant", "Sub", "Unit", ...]
  }

  public class TagInfo {
    public string Name { get; set; }
    public string DataTypeName { get; set; }
    public string LogicalAddress { get; set; }
    public string Comment { get; set; }        // Text in der Editiersprache des Projekts
  }

  public class ConstantInfo {
    public string Name { get; set; }
    public string DataTypeName { get; set; }
    public string Value { get; set; }
    public string Comment { get; set; }        // Text in der Editiersprache des Projekts
  }

  public class Data {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string TagTableName { get; set; }
    public bool IsDefault { get; set; }
    public string[] Path { get; set; }
    public List<TagInfo> Tags { get; set; }
    public List<ConstantInfo> UserConstants { get; set; }
    public List<ConstantInfo> SystemConstants { get; set; }
  }

  /// <summary>
  /// Request-Body für das Anlegen oder Überschreiben einer Variablentabelle (PUT).
  /// Tags und UserConstants beschreiben den vollständigen Soll-Inhalt der Tabelle.
  /// </summary>
  public class PutRequest {
    public string[] Path { get; set; }         // Zielordner für neue Tabellen; null = Wurzelordner
    public List<TagInfo> Tags { get; set; }
    public List<ConstantInfo> UserConstants { get; set; }
  }

  /// <summary>
  /// Request-Body für das Ändern einzelner Variablen und Konstanten (PATCH).
  /// Felder mit null bleiben unverändert.
  /// </summary>
  public class PatchRequest {
    public List<TagInfo> Tags { get; set; }
    public List<ConstantInfo> UserConstants { get; set; }
  }

  /// <summary>
  /// Ergebnis für eine einzelne Variable oder Konstante.
  /// </summary>
  public class Change {
    public string Kind { get; set; }           // "Tag" oder "UserConstant"
    public string Name { get; set; }
    public string Action { get; set; }         // "Created", "Updated", "Deleted", "Unchanged"
    public string Details { get; set; }        // geänderte Felder, z. B. "LogicalAddress: %M10.0 -> %M12.0"
    public string Error { get; set; }          // null bei Erfolg
  }

  /// <summary>
  /// Ergebnis des Anlegens oder Überschreibens einer Variablentabelle (PUT).
  /// </summary>
  public class PutResult {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string TagTableName { get; set; }
    public string[] Path { get; set; }
    public bool Created { get; set; }          // true = Tabelle neu angelegt
    public List<Change> Changes { get; set; }
    public string[] Messages { get; set; }
  }

  /// <summary>
  /// Ergebnis des Änderns einzelner Variablen und Konstanten (PATCH).
  /// </summary>
  public class PatchResult {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string TagTableName { get; set; }
    public List<Change> Changes { get; set; }
  }
}
