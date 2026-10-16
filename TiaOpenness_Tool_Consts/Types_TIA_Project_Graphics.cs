using System.Collections.Generic;
using Tophinke.TiaOpenness.Tool.Types.HMI;
using Tophinke.TiaOpenness.Tool.Types.PLC.Compile;

namespace Tophinke.TiaOpenness.Tool.Types.Project.Graphics {
  /// <summary>
  /// Eintrag der Projekt-Grafiksammlung (Sprachen & Ressourcen → Projektgrafiken).
  /// </summary>
  public class GraphicInfo {
    public string Name { get; set; }
  }

  /// <summary>
  /// Request-Body für das Aufräumen doppelter Projektgrafiken (Name / Name_1 / Name_2).
  /// </summary>
  public class CleanupDuplicatesRequest {
    /// <summary>Nur planen, nichts ändern.</summary>
    public bool DryRun { get; set; }

    /// <summary>Nach dem Aufräumen jedes Unified-Gerät übersetzen und Meldungen zurückgeben.</summary>
    public bool CompileAfter { get; set; }

    /// <summary>
    /// true (Standard): nur Namenskandidaten löschen, deren exportierter Bildinhalt dem Original gleicht.
    /// false: jeder Name_&lt;Zahl&gt; gilt als Duplikat, sobald Name existiert (riskant bei bewussten Varianten).
    /// </summary>
    public bool? CompareContent { get; set; }

    /// <summary>Optional: nur dieses Unified-Gerät für Verweisersetzung und Übersetzen.</summary>
    public string DeviceName { get; set; }

    /// <summary>Optional: HMI-Element; zusammen mit DeviceName.</summary>
    public string DeviceItemName { get; set; }

    /// <summary>
    /// Optional: nur Duplikate, deren Originalname dem Muster entspricht (* und ?), z. B. "Icon_*".
    /// </summary>
    public string NamePattern { get; set; }
  }

  /// <summary>
  /// Zuordnung einer doppelten Grafik (z. B. "Motor_1") zum Original (z. B. "Motor").
  /// </summary>
  public class DuplicateMapping {
    public string Duplicate { get; set; }
    public string Original { get; set; }
    public bool ContentMatch { get; set; }       // true, wenn Bilddaten dem Original gleichen (oder CompareContent=false)
  }

  /// <summary>
  /// Eine gefundene oder geänderte Verwendung einer Grafik in einem Unified-Bild.
  /// </summary>
  public class GraphicUsage {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string HmiName { get; set; }
    public string ScreenName { get; set; }
    public string ScreenPath { get; set; }       // Gruppenpfad, z. B. "Folder/Screen_1"
    public string ItemName { get; set; }         // ScreenItem oder "(Screen)" bei Hintergrundgrafik
    public string ItemType { get; set; }         // Openness-Typname, z. B. "HmiGraphicView"
    public string Property { get; set; }         // z. B. "Graphic", "AlternateGraphic", "BackGraphic"
    public string OldValue { get; set; }
    public string NewValue { get; set; }
    public string Action { get; set; }           // "Replace", "WouldReplace", "SkippedReadOnly", "Error"
    public string Error { get; set; }
  }

  /// <summary>
  /// Ergebnis des Aufräumens doppelter Projektgrafiken.
  /// </summary>
  public class CleanupDuplicatesResult {
    public bool DryRun { get; set; }
    public int GraphicCount { get; set; }
    public int DuplicateCount { get; set; }
    public List<DuplicateMapping> Duplicates { get; set; } = new List<DuplicateMapping>();
    public List<GraphicUsage> Usages { get; set; } = new List<GraphicUsage>();
    public List<Change> DeletedGraphics { get; set; } = new List<Change>();
    public List<CompileResult> Compiles { get; set; }
    public List<string> Warnings { get; set; } = new List<string>();
  }
}
