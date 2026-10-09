using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper;
using Tophinke.TiaOpenness.Tool.Types.PLC.UDT;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC {
  static internal class cTiaUDTs {
    /// <summary>
    /// Gibt die Liste der Datentypen (UDTs) in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="path">Optionaler Ordnerfilter, ein Parameter je Ordnername (path=A&amp;path=B); liefert die Datentypen dieses Ordners inkl. Unterordnern</query>
    /// <returns>JSON-String mit der Liste der Datentypen je PLC oder Fehlermeldung</returns>
    static public string List(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string[] filterPath = ReadPathFilter(context);

      try {
        // TIA-Zugriff via Connection Manager, der Attach-Lifetime handhabt
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var list = new List<PlcList>();
          // Suche in allen Geräten nach einer PLC (Steuerung)
          foreach (Device device in project.Devices) {
            foreach (DeviceItem deviceItem in device.DeviceItems) {
              // Prüfen ob das Gerät Software (also Bausteine) enthält
              SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
              if (container != null && container.Software is PlcSoftware plcSoftware) {
                PlcTypeGroup group = ResolveGroup<PlcTypeGroup>(plcSoftware.TypeGroup, filterPath, g => g.Groups, g => g.Name, out string[] groupPath);
                if (group == null) {
                  continue;
                }
                var udts = new List<Info>();
                List(group, udts, groupPath);
                list.Add(new PlcList {
                  DeviceName = device.Name,
                  DeviceItemName = deviceItem.Name,
                  PlcName = plcSoftware.Name,
                  Udts = udts
                });
              }
            }
          }
          return list;
        });

        if (filterPath != null && retValue.Count == 0) {
          return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Type folder '{string.Join("/", filterPath)}' not found in any PLC.");
        }
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
    /// Gibt den Datentyp (UDT) mit dem angegebenen Namen in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="udtName">Name des Datentyps</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Gerätelements</query>
    /// <returns>JSON-String mit dem Datentyp als SD-Dokument oder Fehlermeldung</returns>
    static public string Get(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string typeName = context.Request.QueryString["udtName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
          if (errorMessage != null) {
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          // Suche nach dem angegebenen Block in der PLC-Software
          PlcType type = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
          if (type == null) {
            errorMessage = $"Error: Type with name {typeName} not found in PLC software {plcSoftware.Name}.";
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          errorMessage = ExportAsDocuments(type, typeName, out string fileContent, out string multiLingualText);
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
            UdtName = type.Name,
            Path = GetTypePath(type),
            Format = FormatSd,
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
    /// Legt einen Datentyp (UDT) neu an oder überschreibt einen bestehenden Datentyp (HTTP PUT).
    /// Ein bestehender Datentyp bleibt in seinem Ordner; Path wird nur beim Neuanlegen verwendet.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="udtName">Name des Datentyps</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PutRequest mit Content (SD oder XML), optional Format, MultiLingualText und Path</body>
    /// <returns>JSON-String mit PutResult oder Fehlermeldung</returns>
    static public string Put(HttpListenerContext context) {
      string methodError = RequireMethod(context, "PUT", UDTRoutes.Put);
      if (methodError != null) {
        return methodError;
      }

      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string typeName = context.Request.QueryString["udtName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      if (string.IsNullOrWhiteSpace(typeName)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: udtName must be provided.");
      }

      PutRequest request;
      try {
        request = ReadBody<PutRequest>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      if (request == null || string.IsNullOrWhiteSpace(request.Content)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain Content.");
      }
      if (request.Path != null && request.Path.Any(string.IsNullOrWhiteSpace)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Path must not contain empty folder names.");
      }

      string format = ResolveImportFormat(request.Format, request.Content);
      if (format == null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Unknown format '{request.Format}'. Allowed: {FormatSd}, {FormatXml}.");
      }
      string contentError = ValidateImportContent(format, request.Content, request.MultiLingualText);
      if (contentError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, contentError);
      }

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
          if (errorMessage != null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, errorMessage);
          }

          PlcType existing = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
          if (existing != null && existing.IsKnowHowProtected) {
            return ErrorResponse(context, HttpStatusCode.Conflict, $"Error: Type {typeName} is know-how-protected and cannot be overwritten.");
          }

          var messages = new List<string>();
          PlcTypeGroup targetGroup;
          if (existing != null) {
            // Verschieben würde Löschen und Neuanlegen bedeuten und Verwendungsstellen in Bausteinen brechen.
            targetGroup = GetTypeGroup(existing) ?? plcTypeGroup;
            string[] existingPath = GetTypePath(existing);
            if (request.Path != null && !PathEquals(existingPath, request.Path)) {
              messages.Add($"Type {existing.Name} already exists in folder '{string.Join("/", existingPath)}'; it was overwritten there and not moved to '{string.Join("/", request.Path)}'.");
            }
          } else {
            targetGroup = EnsureTypeGroup(plcTypeGroup, request.Path ?? new string[0]);
          }

          errorMessage = Import(targetGroup, typeName, format, request.Content, request.MultiLingualText, out PlcType imported, out string[] importMessages);
          if (errorMessage != null) {
            return ErrorResponse(context, HttpStatusCode.InternalServerError, errorMessage);
          }
          messages.AddRange(importMessages);
          if (imported == null) {
            imported = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
          }
          if (imported == null) {
            return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error: Import finished, but type {typeName} was not found afterwards.");
          }
          if (!imported.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)) {
            messages.Add($"Imported type is named '{imported.Name}', not '{typeName}'.");
          }

          var result = new PutResult {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            UdtName = imported.Name,
            Path = GetTypePath(imported),
            Created = existing == null,
            Format = format,
            Messages = messages.ToArray()
          };

          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(result);
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
    /// Exportiert einen Datentyp (UDT) als SD-Dokument.
    /// </summary>
    /// <param name="type">Datentyp (UDT)</param>
    /// <param name="typeName">Name des Datentyps</param>
    /// <param name="content">Out-Parameter für den Inhalt des SD-Dokuments</param>
    /// <returns>Fehlermeldung, falls das Exportieren fehlschlägt, sonst null</returns>
    static private string ExportAsDocuments(PlcType type, string typeName, out string content, out string multiLingualText) {
      content = null;
      multiLingualText = null;
      string safeName = cTiaExportHelpers.SanitizeFileName(typeName);
      DirectoryInfo exportDir = cTiaExportHelpers.EnsureExportDirectory();
      try {
        cTiaExportHelpers.TryDeleteExportedDocument(exportDir.FullName, safeName);

        DocumentExportResult exportResult = type.ExportAsDocuments(exportDir, safeName);
        if (exportResult != null && exportResult.State == DocumentResultState.Success) {
          // Context File
          FileInfo exportedFile = new FileInfo(Path.Combine(exportDir.FullName, safeName + ".s7dcl"));
          if (!exportedFile.Exists) {
            return "Error exporting type " + typeName + ": exported document file not found.";
          }
          content = File.ReadAllText(exportedFile.FullName);

          // Multi-Lingual Text File
          exportedFile = new FileInfo(Path.Combine(exportDir.FullName, safeName + ".s7res"));
          if (exportedFile.Exists) {
            multiLingualText = File.ReadAllText(exportedFile.FullName);
          }
          return null;
        } else {
          string details = cTiaExportHelpers.FormatExportMessages(exportResult);
          return string.IsNullOrEmpty(details)
            ? ("Error exporting type " + typeName + ".")
            : ("Error exporting type " + typeName + ": " + details);
        }
      } catch (Exception ex) {
        return "Error exporting type " + typeName + ": " + ex.Message;
      }
    }

    /// <summary>
    /// Durchsucht die Ordnerstruktur eines PLC-Block-Groups rekursiv nach Datenbausteinen (DBs) und fügt die gefundenen Informationen in eine Liste ein.
    /// </summary>
    /// <param name="group">aktuell zu durchsuchender Block-Group</param>
    /// <param name="list">Liste, in die die gefundenen Informationen hinzugefügt werden</param>
    /// <param name="path">Ordnerpfad der Gruppe</param>
    static private void List(PlcTypeGroup group, List<Info> list, string[] path = null) {
      if (path == null) {
        path = new string[0];
      }
      foreach (PlcType type in group.Types) {
        list.Add(new Info {
          UdtName = type.Name,
          Path = path
        });
      }

      // Rekursiv in Unterordnern suchen
      foreach (PlcTypeUserGroup userGroup in group.Groups) {
        List(userGroup, list, path.Concat(new[] { userGroup.Name }).ToArray());
      }
    }

    /// <summary>
    /// Ermittelt den Ordner, in dem ein Datentyp liegt.
    /// </summary>
    static private PlcTypeGroup GetTypeGroup(PlcType type) {
      IEngineeringObject current = type.Parent;
      while (current != null && !(current is PlcTypeGroup)) {
        current = current.Parent;
      }
      return current as PlcTypeGroup;
    }

    /// <summary>
    /// Ermittelt den Ordnerpfad eines Datentyps innerhalb der Datentyp-Ordner.
    /// </summary>
    /// <returns>Namen der Benutzerordner vom Wurzelordner bis zum Datentyp; leer, wenn der Datentyp im Wurzelordner liegt</returns>
    static internal string[] GetTypePath(PlcType type) {
      var path = new List<string>();
      IEngineeringObject current = type.Parent;
      while (current != null && !(current is PlcTypeSystemGroup)) {
        if (current is PlcTypeUserGroup userGroup) {
          path.Insert(0, userGroup.Name);
        }
        current = current.Parent;
      }
      return path.ToArray();
    }

    /// <summary>
    /// Liefert den Datentyp-Ordner zum angegebenen Pfad und legt fehlende Ordner an.
    /// </summary>
    static private PlcTypeGroup EnsureTypeGroup(PlcTypeGroup root, string[] path) {
      PlcTypeGroup group = root;
      foreach (string name in path) {
        group = (PlcTypeGroup)group.Groups.Find(name) ?? group.Groups.Create(name);
      }
      return group;
    }

    /// <summary>
    /// Importiert einen Datentyp als SD-Dokument oder XML in einen Datentyp-Ordner.
    /// Bestehende Datentypen gleichen Namens werden überschrieben.
    /// </summary>
    /// <param name="group">Zielordner</param>
    /// <param name="fileName">Basisname der temporären Importdatei</param>
    /// <param name="format">FormatSd oder FormatXml</param>
    /// <param name="content">Inhalt des .s7dcl- bzw. XML-Dokuments</param>
    /// <param name="multiLingualText">Optionaler Inhalt der .s7res-Datei (nur SD)</param>
    /// <param name="type">Out-Parameter für den importierten Datentyp</param>
    /// <param name="messages">Out-Parameter für die Meldungen des Imports</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static private string Import(PlcTypeGroup group, string fileName, string format, string content, string multiLingualText, out PlcType type, out string[] messages) {
      type = null;
      messages = new string[0];
      string safeName = cTiaExportHelpers.SanitizeFileName(fileName);
      DirectoryInfo importDir = cTiaExportHelpers.EnsureImportDirectory();
      var encoding = new UTF8Encoding(false);
      try {
        cTiaExportHelpers.TryDeleteExportedDocument(importDir.FullName, safeName);

        if (format == FormatSd) {
          File.WriteAllText(Path.Combine(importDir.FullName, safeName + ".s7dcl"), content, encoding);
          if (!string.IsNullOrEmpty(multiLingualText)) {
            File.WriteAllText(Path.Combine(importDir.FullName, safeName + ".s7res"), multiLingualText, encoding);
          }
          DocumentImportResultForTypes result = group.Types.ImportFromDocuments(importDir, safeName, ImportDocumentOptions.Override);
          if (result == null || result.State == DocumentResultState.Failure) {
            return "ImportFromDocuments failed: " + cTiaExportHelpers.FormatImportMessages(result);
          }
          messages = cTiaExportHelpers.GetMessages(result.Messages);
          type = result.ImportedPlcTypes?.FirstOrDefault();
        } else {
          FileInfo xmlFile = new FileInfo(Path.Combine(importDir.FullName, safeName + ".xml"));
          File.WriteAllText(xmlFile.FullName, content, encoding);
          IList<PlcType> imported = group.Types.Import(xmlFile, ImportOptions.Override);
          type = imported?.FirstOrDefault();
        }
        return null;
      } catch (Exception ex) {
        return "Import failed: " + ex.Message;
      } finally {
        cTiaExportHelpers.TryDeleteExportedDocument(importDir.FullName, safeName);
      }
    }

    #endregion
  }
}
