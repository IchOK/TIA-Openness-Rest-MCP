using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Types.Block;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Stellt Methoden für den Zugriff auf PLC-Bausteine bereit.
  /// </summary>
  static internal class cTiaBlocks {
    /// <summary>
    /// Gibt die Liste aller PLC-Bausteine in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="filterTypes">Filter für die Bausteintypen</query>
    /// <returns>JSON-String mit der Liste der PLC-Bausteine oder Fehlermeldung</returns>
    static public string GetAll(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string[] filterTypes = context.Request.QueryString["filterTypes"]?.Split(',');

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var list = new List<Info>();
          foreach (Device device in project.Devices) {
            foreach (DeviceItem deviceItem in device.DeviceItems) {
              SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
              if (container != null && container.Software is PlcSoftware plcSoftware) {
                list.AddRange(GetAll(plcSoftware.BlockGroup, device.Name, deviceItem.Name, plcSoftware.Name, filterTypes));
              }
            }
          }
          return list;
        });

        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(retValue);
      } catch (Siemens.Engineering.EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.ContentType = "text/plain";
        return "Error: TIA session unavailable. Please re-open TIA Portal.";
      } catch (Exception ex) {
        Console.Error.WriteLine($"Error accessing TIA project: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "text/plain";
        return $"Error accessing TIA project: {ex.Message}";
      }
    }

    /// <summary>
    /// Gibt den PLC-Baustein mit dem angegebenen Namen in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="blockName">Name des PLC-Bausteins</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <returns>JSON-String mit dem PLC-Baustein als SD-Dokument oder Fehlermeldung</returns>
    static public string Get(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string blockName = context.Request.QueryString["blockName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcSystemBlockGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcBlockGroup plcBlockGroup);
          if (errorMessage != null) {
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          PlcBlock block = cTiaFindHelpers.FindBlock(plcBlockGroup, blockName);
          if (block == null) {
            errorMessage = $"Error: Block with name {blockName} not found in PLC software {plcSoftware.Name}.";
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          errorMessage = Export(block, blockName, out string fileContent, out string multiLingualText, out string format);
          if (errorMessage != null) {
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          var data = new Data {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            BlockName = block.Name,
            BlockNumber = block.Number,
            BlockType = block.GetType().Name,
            ProgrammingLanguage = block.ProgrammingLanguage.ToString(),
            Format = format,
            Content = fileContent,
            MultiLingualText = multiLingualText
          };

          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(data);
        });
        return retValue;
      } catch (Siemens.Engineering.EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.ContentType = "text/plain";
        return "Error: TIA session unavailable. Please re-open TIA Portal.";
      } catch (Exception ex) {
        Console.Error.WriteLine($"Error accessing TIA project: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "text/plain";
        return $"Error accessing TIA project: {ex.Message}";
      }
    }

    /// <summary>
    /// Überprüft für jeden gefundenen ProgrammingLanguage einen Beispielbaustein auf SD- und XML-Export.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <returns>JSON-String mit der Liste der Export-Fähigkeiten oder Fehlermeldung</returns>
    static public string ListExportCapabilities(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          // Ersten nicht-geschützten Baustein je ProgrammingLanguage merken
          var samples = new Dictionary<string, ExportSample>(StringComparer.OrdinalIgnoreCase);

          foreach (Device device in project.Devices) {
            foreach (DeviceItem deviceItem in device.DeviceItems) {
              SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
              if (container == null || !(container.Software is PlcSoftware plcSoftware)) {
                continue;
              }
              CollectExportSamples(plcSoftware.BlockGroup, device.Name, deviceItem.Name, plcSoftware.Name, samples);
            }
          }

          var results = new List<ExportCapabilityInfo>();
          foreach (var pair in samples.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) {
            results.Add(ProbeExportCapability(pair.Value));
          }

          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(results);
        });
        return retValue;
      } catch (Siemens.Engineering.EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.ContentType = "text/plain";
        return "Error: TIA session unavailable. Please re-open TIA Portal.";
      } catch (Exception ex) {
        Console.Error.WriteLine($"Error probing export capabilities: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "text/plain";
        return $"Error probing export capabilities: {ex.Message}";
      }
    }

    #region Hilfsmethoden für TIA Openness Objektstruktur

    /// <summary>
    /// Durchsucht die Ordnerstruktur eines PLC-Block-Groups rekursiv nach Bausteinen und fügt die gefundenen Informationen in eine Liste ein.
    /// </summary>
    static private List<Info> GetAll(PlcBlockGroup group, string deviceName, string deviceItemName, string plcName, string[] filterTypes = null, string[] path = null) {
      var blocks = new List<Info>();
      if (path == null) {
        path = new string[] { };
      }
      foreach (var block in group.Blocks) {
        if (filterTypes != null && !filterTypes.Contains(block.GetType().Name)) {
          continue;
        }
        blocks.Add(new Info {
          DeviceName = deviceName,
          DeviceItemName = deviceItemName,
          PlcName = plcName,
          BlockName = block.Name,
          BlockNumber = block.Number,
          BlockType = block.GetType().Name,
          ProgrammingLanguage = block.ProgrammingLanguage.ToString(),
          Path = path
        });
      }
      foreach (var userGroup in group.Groups) {
        string[] newPath = path.Concat(new string[] { userGroup.Name }).ToArray();
        blocks.AddRange(GetAll(userGroup, deviceName, deviceItemName, plcName, filterTypes, newPath));
      }
      return blocks;
    }

    /// <summary>
    /// Exportiert einen PLC-Baustein als SD-Dokument oder XML.
    /// </summary>
    /// <param name="block">PLC-Baustein</param>
    /// <param name="blockName">Name des PLC-Bausteins</param>
    /// <param name="content">Out-Parameter für den Inhalt des SD-Dokuments oder XML</param>
    /// <param name="format">Out-Parameter für das Format des Export-Ergebnisses</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static private string Export(PlcBlock block, string blockName, out string content, out string multiLingualText, out string format) {
      content = null;
      multiLingualText = null;
      format = null;

      if (block == null) {
        return "Error exporting block " + blockName + ": block is null";
      }
      if (block.IsKnowHowProtected) {
        return "Error exporting block " + blockName + ": block is know-how-protected";
      }

      if (BlockIsDocument(block, blockName)) {
        string error = ExportAsDocuments(block, blockName, out content, out multiLingualText);
        if (error != null) {
          return error;
        }
        format = "SimaticData/SD";
        return null;
      }

      string xmlError = ExportAsXml(block, blockName, out content);
      if (xmlError != null) {
        return xmlError;
      }
      format = "SimaticML/XML";
      return null;
    }

    /// <summary>
    /// Prüft, ob ein PLC-Baustein als SD-Dokument exportiert werden soll.
    /// </summary>
    /// <param name="block">PLC-Baustein</param>
    /// <param name="blockName">Name des PLC-Bausteins</param>
    /// <returns>True, wenn der Baustein als SD-Dokument exportiert werden soll, false sonst</returns>
    static private bool BlockIsDocument(PlcBlock block, string blockName) {
      switch (block.ProgrammingLanguage) {
        case ProgrammingLanguage.LAD:
        case ProgrammingLanguage.FBD:
        case ProgrammingLanguage.DB:
        case ProgrammingLanguage.SCL:
        case ProgrammingLanguage.F_LAD:
        case ProgrammingLanguage.F_FBD:
        case ProgrammingLanguage.F_DB:
          return true;
        default:
          return false;
      }
    }

    /// <summary>
    /// Liest den Inhalt eines exportierten SD-Dokuments.
    /// </summary>
    /// <param name="blockName">Name des PLC-Bausteins</param>
    /// <param name="content">Out-Parameter für den Inhalt des SD-Dokuments</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static private string ExportAsDocuments(PlcBlock block, string blockName, out string content, out string multiLingualText) {
      content = null;
      multiLingualText = null;
      string safeName = cTiaExportHelpers.SanitizeFileName(blockName);
      DirectoryInfo exportDir = cTiaExportHelpers.EnsureExportDirectory();
      try {
        cTiaExportHelpers.TryDeleteExportedDocument(exportDir.FullName, safeName);

        DocumentExportResult result = block.ExportAsDocuments(exportDir, safeName);
        if (result != null && result.State == DocumentResultState.Success) {
          // Context File
          FileInfo exportedFile = new FileInfo(Path.Combine(exportDir.FullName, safeName + ".s7dcl"));
          if (!exportedFile.Exists) {
            return "ExportAsDocuments failed: exported document file not found.";
          }
          content = File.ReadAllText(exportedFile.FullName);

          // Multi-Lingual Text File
          exportedFile = new FileInfo(Path.Combine(exportDir.FullName, safeName + ".s7res"));
          if (exportedFile.Exists) {
            multiLingualText = File.ReadAllText(exportedFile.FullName);
          }

          return null;
        } else {
          return "ExportAsDocuments failed: " + cTiaExportHelpers.FormatExportMessages(result);
        }
      } catch (Exception ex) {
        return "ExportAsDocuments failed: " + ex.Message;
      }
    }

    /// <summary>
    /// Exportiert einen PLC-Baustein als XML.
    /// </summary>
    /// <param name="block">PLC-Baustein</param>
    /// <param name="blockName">Name des PLC-Bausteins</param>
    /// <param name="content">Out-parameter für den Inhalt des XML-Dokuments</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static private string ExportAsXml(PlcBlock block, string blockName, out string content) {
      content = null;
      string safeName = cTiaExportHelpers.SanitizeFileName(blockName);
      DirectoryInfo exportDir = cTiaExportHelpers.EnsureExportDirectory();
      try {
        cTiaExportHelpers.TryDeleteExportedDocument(exportDir.FullName, safeName);

        FileInfo xmlFile = new FileInfo(Path.Combine(exportDir.FullName, safeName + ".xml"));
        block.Export(xmlFile, ExportOptions.WithDefaults);
        xmlFile.Refresh();
        if (!xmlFile.Exists) {
          return "ExportAsXml failed: exported XML file not found.";
        }
        content = File.ReadAllText(xmlFile.FullName);
        return null;
      } catch (Exception ex) {
        return "ExportAsXml failed: " + ex.Message;
      }
    }

    /// <summary>
    /// Datenstruktur für einen Beispielbaustein für die Export-Probe.
    /// </summary>
    private class ExportSample {
      public PlcBlock Block;
      public string DeviceName;
      public string DeviceItemName;
      public string PlcName;
      public string ProgrammingLanguage;
    }

    /// <summary>
    /// Sammelt Beispielbausteine für alle ProgrammingLanguages in einem Block-Group.
    /// </summary>
    /// <param name="group">Block-Group</param>
    /// <param name="deviceName">Name des Geräts</param>
    /// <param name="deviceItemName">Name des Geräteelements</param>
    /// <param name="plcName">Name des PLC-Software</param>
    /// <param name="samples">Dictionary mit den Beispielbausteinen</param>
    static private void CollectExportSamples(
      PlcBlockGroup group,
      string deviceName,
      string deviceItemName,
      string plcName,
      Dictionary<string, ExportSample> samples) {

      foreach (PlcBlock block in group.Blocks) {
        string language;
        try {
          if (block.IsKnowHowProtected) {
            continue;
          }
          language = block.ProgrammingLanguage.ToString();
        } catch {
          continue;
        }
        if (string.IsNullOrEmpty(language) || samples.ContainsKey(language)) {
          continue;
        }
        samples[language] = new ExportSample {
          Block = block,
          DeviceName = deviceName,
          DeviceItemName = deviceItemName,
          PlcName = plcName,
          ProgrammingLanguage = language
        };
      }

      foreach (PlcBlockUserGroup userGroup in group.Groups) {
        CollectExportSamples(userGroup, deviceName, deviceItemName, plcName, samples);
      }
    }

    /// <summary>
    /// Test wie der Baustein exportiert werden kann.
    /// </summary>
    /// <param name="sample">Beispielbaustein</param>
    /// <returns>ExportCapabilityInfo mit den Export-Fähigkeiten</returns>
    static private ExportCapabilityInfo ProbeExportCapability(ExportSample sample) {
      var info = new ExportCapabilityInfo {
        ProgrammingLanguage = sample.ProgrammingLanguage,
        SampleBlockName = sample.Block.Name,
        SampleBlockType = sample.Block.GetType().Name,
        DeviceName = sample.DeviceName,
        DeviceItemName = sample.DeviceItemName,
        PlcName = sample.PlcName,
        DocumentsExtensions = new string[0]
      };

      string baseName = "_cap_" + cTiaExportHelpers.SanitizeFileName(sample.ProgrammingLanguage);
      DirectoryInfo exportDir = cTiaExportHelpers.EnsureExportDirectory();

      // 1) SIMATIC SD / ExportAsDocuments
      cTiaExportHelpers.TryDeleteExportedDocument(exportDir.FullName, baseName);
      try {
        DocumentExportResult sdResult = sample.Block.ExportAsDocuments(exportDir, baseName);
        if (sdResult != null && sdResult.State == DocumentResultState.Success) {
          string[] extensions = ListExportExtensions(exportDir, baseName);
          info.DocumentsSupported = extensions.Length > 0;
          info.DocumentsExtensions = extensions;
          if (!info.DocumentsSupported) {
            info.DocumentsError = "ExportAsDocuments succeeded but no export file was found.";
          }
        } else {
          info.DocumentsSupported = false;
          info.DocumentsError = cTiaExportHelpers.FormatExportMessages(sdResult);
        }
      } catch (Exception ex) {
        info.DocumentsSupported = false;
        info.DocumentsError = ex.Message;
      }

      // 2) Simatic ML XML
      cTiaExportHelpers.TryDeleteExportedDocument(exportDir.FullName, baseName);
      try {
        FileInfo xmlFile = new FileInfo(Path.Combine(exportDir.FullName, baseName + ".xml"));
        sample.Block.Export(xmlFile, ExportOptions.WithDefaults);
        xmlFile.Refresh();
        if (xmlFile.Exists) {
          info.XmlSupported = true;
          info.XmlExtension = xmlFile.Extension;
        } else {
          info.XmlSupported = false;
          info.XmlError = "Export produced no XML file.";
        }
      } catch (Exception ex) {
        info.XmlSupported = false;
        info.XmlError = ex.Message;
      }

      // Probe-Dateien aufräumen
      cTiaExportHelpers.TryDeleteExportedDocument(exportDir.FullName, baseName);
      return info;
    }

    /// <summary>
    /// Listet die Dateiendungen der exportierten Dateien auf.
    /// </summary>
    /// <param name="directory">Verzeichnis</param>
    /// <param name="baseName">Basisname</param>
    /// <returns>Array mit den Dateiendungen</returns>
    static private string[] ListExportExtensions(DirectoryInfo directory, string baseName) {
      var extensions = new List<string>();
      try {
        foreach (FileInfo file in directory.GetFiles(baseName + ".*")) {
          if (!string.IsNullOrEmpty(file.Extension) && !extensions.Contains(file.Extension)) {
            extensions.Add(file.Extension);
          }
        }
      } catch {
      }
      extensions.Sort(StringComparer.OrdinalIgnoreCase);
      return extensions.ToArray();
    }

    #endregion
  }
}
