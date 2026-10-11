using System.Collections.Generic;

namespace Tophinke.TiaOpenness.Tool.Types.TopCtrl.PLC.Message {
  /// <summary>
  /// Ergebnis der Meldungsliste: Meldungen aller HMI-Meldetreiber und Hinweise zu nicht auflösbaren Verknüpfungen.
  /// </summary>
  public class ListResult {
    public List<Info> Messages { get; set; } = new List<Info>();
    public List<string> Warnings { get; set; } = new List<string>();
  }

  /// <summary>
  /// Ergebnis des Exports der Meldungsliste in eine CSV-Datei (GetFile).
  /// </summary>
  public class FileData {
    public string Format { get; set; }         // "CSV"
    public string Directory { get; set; }      // Root\Projekt
    public string[] Files { get; set; }        // vollständiger Pfad der CSV-Datei
    public int MessageCount { get; set; }      // Anzahl der Datenzeilen
    public List<string> Warnings { get; set; } = new List<string>();
  }

  /// <summary>
  /// Eine Meldung: Bit einer HMI-Treibervariablen (tbMsg_Drv16..._T), zurückverfolgt bis zur Meldevariablen (tbMsg_Element_T).
  /// </summary>
  public class Info {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }

    /// <summary>HMI-Art des Treibers: Unified, SCADA oder CP.</summary>
    public string Driver { get; set; }

    /// <summary>Aufgerufener Treiber-FC, z. B. tbMsg_Drv16Unified.</summary>
    public string DriverFunction { get; set; }

    /// <summary>Baustein, der den Treiber aufruft.</summary>
    public string DriverBlock { get; set; }

    /// <summary>HMI-Treibervariable, z. B. "uvHmiMsgPer".DI_001 oder "pvHmiMsgArea_HMI_Uni".PN1_Diag[3].</summary>
    public string HmiVariable { get; set; }

    /// <summary>Bitnummer der Meldung in den Worten der HMI-Treibervariablen (0..15).</summary>
    public int HmiBit { get; set; }

    /// <summary>Buselement der Meldung, z. B. "DI_Msg".DI_001.aMsg[2].</summary>
    public string BusElement { get; set; }

    /// <summary>Meldevariable (tbMsg_Element_T), z. B. "DI_001".SstMsg.stHwFault.</summary>
    public string Element { get; set; }

    /// <summary>Datenbaustein, der die Meldevariable enthält.</summary>
    public string ElementBlock { get; set; }

    /// <summary>FB der Instanz, falls die Meldevariable in einem Instanz-DB liegt.</summary>
    public string ElementFb { get; set; }

    /// <summary>Aufgerufener Melde-Handler, z. B. tbMsg_Handler_OneModul.</summary>
    public string Handler { get; set; }

    /// <summary>Baustein, der den Melde-Handler aufruft.</summary>
    public string HandlerBlock { get; set; }

    /// <summary>Herkunft der Konfiguration, wenn stConfig im Code von einer anderen Variablen kopiert wird.</summary>
    public string ConfigSource { get; set; }

    /// <summary>Meldetext (Kommentar der Meldevariablen) in der Sprache de-DE bzw. der ersten vorhandenen Sprache.</summary>
    public string Text { get; set; }

    /// <summary>Meldetext je Sprache.</summary>
    public Dictionary<string, string> Texts { get; set; }

    /// <summary>Startwert stConfig.xSelect: Meldung ist angewählt.</summary>
    public bool Select { get; set; }

    /// <summary>Startwert stConfig.iType (Meldeart); null, wenn nicht auflösbar.</summary>
    public int? Type { get; set; }

    /// <summary>Konstante, mit der stConfig.iType belegt ist (z. B. MSG_TYPE_OPER_MODE); sonst null.</summary>
    public string TypeConstant { get; set; }

    /// <summary>Startwert stConfig.xAutoQuit.</summary>
    public bool AutoQuit { get; set; }

    /// <summary>Startwert stConfig.xSync.</summary>
    public bool Sync { get; set; }

    /// <summary>Startwert stConfig.xDoReset.</summary>
    public bool DoReset { get; set; }

    /// <summary>Startwert stConfig.xIsFault.</summary>
    public bool IsFault { get; set; }
  }
}
