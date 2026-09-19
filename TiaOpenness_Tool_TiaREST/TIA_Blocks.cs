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

          errorMessage = ExportBlock(block, blockName, out string fileContent, out string format);
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
            Format = format,
            Content = fileContent
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

    #region Hilfsmethoden für TIA Openness Objektstruktur

    /// <summary>
    /// Durchsucht die Ordnerstruktur eines PLC-Block-Groups rekursiv nach Bausteinen und fügt die gefundenen Informationen in eine Liste ein.
    /// </summary>
    static public List<Info> GetAll(PlcBlockGroup group, string deviceName, string deviceItemName, string plcName, string[] filterTypes = null, string[] path = null) {
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
    static private string ExportBlock(PlcBlock block, string blockName, out string content, out string format) {
      content = null;
      format = null;

      if (block == null) {
        return "Error exporting block " + blockName + ": block is null";
      }
      if (block.IsKnowHowProtected) {
        return "Error exporting block " + blockName + ": block is know-how-protected";
      }

      if (IsExportBlockAsDocuments(block, blockName)) {
        string error = ReadExportedDocument(blockName, out content);
        if (error != null) {
          return error;
        }
        format = "SimaticData/SD";
        return null;
      }

      string xmlError = TryExportBlockAsXml(block, blockName, out content);
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
    static private bool IsExportBlockAsDocuments(PlcBlock block, string blockName) {
      string safeName = cTiaExportHelpers.SanitizeFileName(blockName);
      DirectoryInfo exportDir = cTiaExportHelpers.EnsureExportDirectory();
      try {
        cTiaExportHelpers.TryDeleteExportedDocument(exportDir.FullName, safeName);
        DocumentExportResult exportResult = block.ExportAsDocuments(exportDir, safeName);
        return exportResult != null && exportResult.State == DocumentResultState.Success;
      } catch {
        return false;
      }
    }

    /// <summary>
    /// Liest den Inhalt eines exportierten SD-Dokuments.
    /// </summary>
    /// <param name="blockName">Name des PLC-Bausteins</param>
    /// <param name="content">Out-Parameter für den Inhalt des SD-Dokuments</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static private string ReadExportedDocument(string blockName, out string content) {
      content = null;
      string safeName = cTiaExportHelpers.SanitizeFileName(blockName);
      DirectoryInfo exportDir = cTiaExportHelpers.EnsureExportDirectory();
      FileInfo exportedFile = cTiaExportHelpers.FindExportedDocument(exportDir, safeName);
      if (exportedFile == null || !exportedFile.Exists) {
        return "ExportAsDocuments failed: exported document file not found.";
      }
      content = File.ReadAllText(exportedFile.FullName);
      return null;
    }

    /// <summary>
    /// Exportiert einen PLC-Baustein als XML.
    /// </summary>
    /// <param name="block">PLC-Baustein</param>
    /// <param name="blockName">Name des PLC-Bausteins</param>
    /// <param name="content">Out-parameter für den Inhalt des XML-Dokuments</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static private string TryExportBlockAsXml(PlcBlock block, string blockName, out string content) {
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

    #endregion
  }
}
