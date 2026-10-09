namespace Tophinke.TiaOpenness.Tool.Types.PLC.Library {
  /// <summary>
  /// Verbindung eines PLC-Objekts zu einem Typ der Projektbibliothek.
  /// </summary>
  public class LibraryInfo {
    public string TypeName { get; set; }       // Name des Bibliothekstyps
    public string Version { get; set; }        // z. B. "0.1.36"
    public string State { get; set; }          // "Committed" = freigegeben, "InWork" = in Test/Bearbeitung
  }
}
