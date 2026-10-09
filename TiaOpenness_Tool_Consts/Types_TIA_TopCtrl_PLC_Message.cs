using System;

namespace Tophinke.TiaOpenness.Tool.Types.TopCtrl.PLC.Message {
  /// <summary>
  /// Eine gefundene Meldung aus einer Meldekonfiguration (tbMsg_ConfElement_T).
  /// </summary>
  public class Info {
    /// <summary>Kommentar des Konfig-Elements (Meldetext).</summary>
    public string Meldetext { get; set; }

    /// <summary>Startwert von iType im Konfig-Element.</summary>
    public string Meldetype { get; set; }

    /// <summary>
    /// Array-Index der HMI-Schnittstelle, falls diese in einem Array liegt; sonst null.
    /// </summary>
    public int? Meldeblock { get; set; }

    /// <summary>
    /// Position innerhalb des 16er-Konfigblocks (0..15).
    /// </summary>
    public int Meldeindex { get; set; }

    /// <summary>Name des Datenbausteins, der die Konfiguration enthält.</summary>
    public string DbName { get; set; }

    /// <summary>Gruppenpfad des DBs vom BlockGroup-Root bis zum DB (ohne DB-Namen).</summary>
    public string[] Path { get; set; }

    /// <summary>Optional: Device / PLC-Kontext.</summary>
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }

    /// <summary>Member-Pfad der Konfiguration im DB (z. B. IstConf[3]).</summary>
    public string ConfMemberPath { get; set; }
  }
}
