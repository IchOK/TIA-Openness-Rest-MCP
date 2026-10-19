using System;
using System.Collections.Generic;
using System.Text;

namespace Tophinke.TiaOpenness.Tool.Types.Project {
  public class Info {
    public int ProcessId { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    /// <summary>
    /// "Project" | "LocalSession" (siehe ProjectRoutes.Kind*).
    /// </summary>
    public string Kind { get; set; }
  }

  /// <summary>
  /// Ergebnis des Öffnens eines Projekts.
  /// </summary>
  public class OpenResult : Info {
    public string Mode { get; set; }              // "Open", "OpenWithUpgrade", "Retrieve", "RetrieveWithUpgrade" oder "AlreadyOpen"
    public bool StartedTiaPortal { get; set; }    // true = TIA Portal wurde für das Projekt neu gestartet
  }

  /// <summary>
  /// Ergebnis des Speicherns eines Projekts.
  /// </summary>
  public class SaveResult : Info {
    public bool WasModified { get; set; }         // true = das Projekt hatte ungespeicherte Änderungen
  }

  /// <summary>
  /// Ergebnis des Schließens eines Projekts.
  /// </summary>
  public class CloseResult : Info {
    public bool Saved { get; set; }               // true = vor dem Schließen gespeichert
    public bool DiscardedChanges { get; set; }    // true = ungespeicherte Änderungen wurden verworfen
  }
}
