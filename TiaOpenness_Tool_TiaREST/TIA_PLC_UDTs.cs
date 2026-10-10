using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Library.Types;
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
using Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch;
using Tophinke.TiaOpenness.Tool.Types.PLC.UDT;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC {
  static internal class cTiaUDTs {
    /// <summary>
    /// Dateiendungen, die beim Export eines Datentyps in Dateien entstehen können.
    /// </summary>
    static private readonly string[] TypeFileExtensions = { ".s7dcl", ".s7res", ".xml" };

    /// <summary>
    /// Gibt die Liste der Datentypen (UDTs) in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="path">Optionaler Ordnerfilter, ein Parameter je Ordnername (path=A&amp;path=B); liefert die Datentypen dieses Ordners inkl. Unterordnern</query>
    /// <returns>JSON-String mit der Liste der Datentypen je PLC oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.List)]
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
        return JsonConvert.SerializeObject(retValue, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
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
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="udtName">Name des Datentyps</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Gerätelements</query>
    /// <returns>JSON-String mit dem Datentyp als SD-Dokument oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.Get)]
    static public string Get(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string typeName = context.Request.QueryString["udtName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      return cTiaFileBatchHelpers.ExecuteItem(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
        if (errorMessage != null) {
          return ItemResult<Data>.Fail(HttpStatusCode.NotFound, errorMessage);
        }

        PlcType type = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
        if (type == null) {
          return ItemResult<Data>.Fail(HttpStatusCode.NotFound, $"Error: Type with name {typeName} not found in PLC software {plcSoftware.Name}.");
        }

        errorMessage = ExportAsDocuments(type, typeName, out string fileContent, out string multiLingualText);
        if (errorMessage != null) {
          return ItemResult<Data>.Fail(HttpStatusCode.InternalServerError, errorMessage);
        }

        return ItemResult<Data>.Ok(new Data {
          DeviceName = deviceName,
          DeviceItemName = deviceItemName,
          PlcName = plcSoftware.Name,
          UdtName = type.Name,
          Path = GetTypePath(type),
          Library = cTiaLibraryHelpers.GetLibraryInfo(type),
          Format = FormatSd,
          Content = fileContent,
          MultiLingualText = multiLingualText
        });
      });
    }

    /// <summary>
    /// Legt einen Datentyp (UDT) neu an oder überschreibt einen bestehenden Datentyp (HTTP PUT).
    /// Ein bestehender Datentyp bleibt in seinem Ordner; Path wird nur beim Neuanlegen verwendet.
    /// Instanzen von Bibliothekstypen werden nur mit LibraryMode InTest/Release geschrieben.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="udtName">Name des Datentyps</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PutRequest mit Content (SD oder XML), optional Format, MultiLingualText, Path und Bibliotheksoptionen</body>
    /// <returns>JSON-String mit PutResult oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.Put, "PUT")]
    static public string Put(HttpListenerContext context) {
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
      string libraryError = cTiaLibraryHelpers.ResolveWriteOptions(request.LibraryMode, request.LibraryVersion, request.LibraryAuthor, request.LibraryComment, out LibraryWriteOptions library);
      if (libraryError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, libraryError);
      }

      var source = new ImportSource {
        Format = format,
        Content = request.Content,
        MultiLingualText = request.MultiLingualText
      };
      return ImportUdt(context, processIdStr, projectName, deviceName, deviceItemName, typeName, request.Path, library,
        (project, filePath) => ItemResult<ImportSource>.Ok(source));
    }

    /// <summary>
    /// Exportiert einen Datentyp als SD-Dokument in Dateien unter Root\Projekt\Ordnerpfad des Datentyps.
    /// Vorhandene Dateien des Datentyps (.s7dcl, .s7res, .xml) werden ersetzt.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="udtName">Name des Datentyps</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <query name="rootDirectory">Absoluter Pfad des Wurzelverzeichnisses</query>
    /// <returns>JSON-String mit FileData oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.GetFile)]
    static public string GetFile(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string typeName = context.Request.QueryString["udtName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];
      string rootError = cTiaExportHelpers.ResolveRootDirectory(context.Request.QueryString["rootDirectory"], out string fullRoot);
      if (rootError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, rootError);
      }

      return cTiaFileBatchHelpers.ExecuteItem(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
        if (errorMessage != null) {
          return ItemResult<FileData>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        return ExportUdtFiles(project, plcSoftware, plcTypeGroup, deviceName, deviceItemName, typeName, fullRoot);
      });
    }

    /// <summary>
    /// Exportiert alle Datentypen eines Datentyp-Ordners in Dateien (wie GetFile für jeden Datentyp).
    /// Know-how-geschützte Datentypen werden übersprungen.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <query name="rootDirectory">Absoluter Pfad des Wurzelverzeichnisses</query>
    /// <query name="path">Datentyp-Ordner, ein Parameter je Ordnername; ohne Angabe der Wurzelordner</query>
    /// <query name="recursive">Unterordner einbeziehen (Standard: true)</query>
    /// <returns>JSON-String mit FileBatchResult von FileData oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.GetDir)]
    static public string GetDir(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];
      string[] folderPath = ReadPathFilter(context);
      bool recursive = cTiaFileBatchHelpers.ReadRecursive(context, out string recursiveError);
      if (recursiveError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, recursiveError);
      }
      string rootError = cTiaExportHelpers.ResolveRootDirectory(context.Request.QueryString["rootDirectory"], out string fullRoot);
      if (rootError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, rootError);
      }

      return cTiaFileBatchHelpers.ExecuteBatch(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<FileData>>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        PlcTypeGroup group = ResolveGroup<PlcTypeGroup>(plcTypeGroup, folderPath, g => g.Groups, g => g.Name, out string[] groupPath);
        if (group == null) {
          return ItemResult<FileBatchResult<FileData>>.Fail(HttpStatusCode.NotFound, $"Error: Type folder '{string.Join("/", folderPath)}' not found in PLC software {plcSoftware.Name}.");
        }

        var batch = cTiaFileBatchHelpers.NewBatch<FileData>(deviceName, deviceItemName, plcSoftware);
        errorMessage = cTiaExportHelpers.GetFileDirectory(fullRoot, project.Name, groupPath, out string directory);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<FileData>>.Fail(HttpStatusCode.BadRequest, errorMessage);
        }
        batch.Directory = directory;
        foreach (PlcType type in EnumerateTypes(group, recursive)) {
          if (type.IsKnowHowProtected) {
            batch.Skipped.Add(new FileBatchSkipped { Name = type.Name, Reason = "know-how-protected" });
            continue;
          }
          cTiaFileBatchHelpers.Collect(batch, ExportUdtFiles(project, plcSoftware, type, deviceName, deviceItemName, fullRoot), type.Name, GetTypePath(type));
        }
        return ItemResult<FileBatchResult<FileData>>.Ok(batch);
      });
    }

    /// <summary>
    /// Exportiert mehrere Datentypen in Dateien (wie GetFile für jeden Datentyp).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="udtName">Namen der Datentypen, ein Parameter je Datentyp (udtName=A&amp;udtName=B)</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <query name="rootDirectory">Absoluter Pfad des Wurzelverzeichnisses</query>
    /// <returns>JSON-String mit FileBatchResult von FileData oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.GetFileList)]
    static public string GetFileList(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];
      string[] typeNames = cTiaFileBatchHelpers.ReadNameList(context, "udtName");
      if (typeNames == null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: At least one udtName must be provided (one parameter per UDT).");
      }
      string rootError = cTiaExportHelpers.ResolveRootDirectory(context.Request.QueryString["rootDirectory"], out string fullRoot);
      if (rootError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, rootError);
      }

      return cTiaFileBatchHelpers.ExecuteBatch(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<FileData>>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        var batch = cTiaFileBatchHelpers.NewBatch<FileData>(deviceName, deviceItemName, plcSoftware);
        foreach (string typeName in typeNames) {
          cTiaFileBatchHelpers.Collect(batch, ExportUdtFiles(project, plcSoftware, plcTypeGroup, deviceName, deviceItemName, typeName, fullRoot), typeName, null);
        }
        return ItemResult<FileBatchResult<FileData>>.Ok(batch);
      });
    }

    /// <summary>
    /// Legt einen Datentyp aus Dateien neu an oder überschreibt einen bestehenden Datentyp (HTTP PUT).
    /// Die Dateien werden aus Root\Projekt\Ordnerpfad gelesen; Ordnerpfad ist Path aus dem Request,
    /// sonst der bisherige Ordner des Datentyps bzw. der Wurzelordner. Ein bestehender Datentyp wird nicht verschoben.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="udtName">Name des Datentyps</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PutFileRequest mit RootDirectory, optional Format, Path und Bibliotheksoptionen</body>
    /// <returns>JSON-String mit PutFileResult oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.PutFile, "PUT")]
    static public string PutFile(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string typeName = context.Request.QueryString["udtName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      if (string.IsNullOrWhiteSpace(typeName)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: udtName must be provided.");
      }

      PutFileRequest request;
      try {
        request = ReadBody<PutFileRequest>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      if (request == null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain RootDirectory.");
      }
      if (request.Path != null && request.Path.Any(string.IsNullOrWhiteSpace)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Path must not contain empty folder names.");
      }
      string rootError = cTiaExportHelpers.ResolveRootDirectory(request.RootDirectory, out string fullRoot);
      if (rootError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, rootError);
      }
      string requestedFormat = null;
      if (!string.IsNullOrWhiteSpace(request.Format)) {
        requestedFormat = ResolveImportFormat(request.Format, "");
        if (requestedFormat == null) {
          return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Unknown format '{request.Format}'. Allowed: {FormatSd}, {FormatXml}.");
        }
      }
      string libraryError = cTiaLibraryHelpers.ResolveWriteOptions(request.LibraryMode, request.LibraryVersion, request.LibraryAuthor, request.LibraryComment, out LibraryWriteOptions library);
      if (libraryError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, libraryError);
      }

      return ImportUdt(context, processIdStr, projectName, deviceName, deviceItemName, typeName, request.Path, library,
        (project, filePath) => cTiaBlocks.LoadImportDocuments(fullRoot, project.Name, filePath, typeName, requestedFormat, "UDT"));
    }

    /// <summary>
    /// Importiert alle Datentyp-Dateien aus Root\Projekt\Ordnerpfad (optional mit Unterverzeichnissen) (HTTP PUT).
    /// Jede Datei &lt;Name&gt;.s7dcl bzw. &lt;Name&gt;.xml mit Datentyp-Inhalt wird wie bei PutFile mit Path = Verzeichnispfad importiert;
    /// Dateien von Bausteinen und Tagtabellen werden übersprungen. Instanzen von Bibliothekstypen werden
    /// als In-Test-Version in ihren Typ geschrieben.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PutDirRequest mit RootDirectory, optional Path, Recursive und Format</body>
    /// <returns>JSON-String mit FileBatchResult von PutFileResult oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.PutDir, "PUT")]
    static public string PutDir(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      string requestError = cTiaBlocks.ReadPutDirRequest(context, out PutDirRequest request, out string fullRoot, out string preferredFormat);
      if (requestError != null) {
        return requestError;
      }
      LibraryWriteOptions library = cTiaLibraryHelpers.InTestForInstances();

      return cTiaFileBatchHelpers.ExecuteBatch(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<PutResult>>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        var batch = cTiaFileBatchHelpers.NewBatch<PutResult>(deviceName, deviceItemName, plcSoftware);
        var scan = cTiaFileBatchHelpers.ScanDirectory(fullRoot, project.Name, request.Path, request.Recursive ?? true, cTiaFileBatchHelpers.KindUdt, preferredFormat, batch);
        if (scan.Failed) {
          return ItemResult<FileBatchResult<PutResult>>.Fail(scan.Status, scan.Error);
        }
        cTiaFileBatchHelpers.ImportAll(batch, scan.Value,
          entry => ImportUdt(project, plcSoftware, plcTypeGroup, deviceName, deviceItemName, entry.Name, entry.Path, library,
            (p, filePath) => cTiaBlocks.LoadImportDocuments(fullRoot, p.Name, filePath, entry.Name, entry.Format, "UDT")),
          entry => entry.Name, entry => entry.Path);
        return ItemResult<FileBatchResult<PutResult>>.Ok(batch);
      });
    }

    /// <summary>
    /// Importiert mehrere Datentypen aus Dateien (wie PutFile für jeden Eintrag) (HTTP PUT).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PutFileListRequest mit RootDirectory, Items und optional den Bibliotheksoptionen</body>
    /// <returns>JSON-String mit FileBatchResult von PutFileResult oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.PutFileList, "PUT")]
    static public string PutFileList(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      PutFileListRequest request;
      try {
        request = ReadBody<PutFileListRequest>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      if (request?.Items == null || request.Items.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain RootDirectory and at least one entry in Items.");
      }
      if (request.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.UdtName))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Every entry in Items needs a UdtName.");
      }
      if (request.Items.Any(i => i.Path != null && i.Path.Any(string.IsNullOrWhiteSpace))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Path must not contain empty folder names.");
      }
      string rootError = cTiaExportHelpers.ResolveRootDirectory(request.RootDirectory, out string fullRoot);
      if (rootError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, rootError);
      }
      var formats = new Dictionary<PutFileListItem, string>();
      foreach (PutFileListItem item in request.Items) {
        if (string.IsNullOrWhiteSpace(item.Format)) {
          formats[item] = null;
        } else {
          formats[item] = ResolveImportFormat(item.Format, "");
          if (formats[item] == null) {
            return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Unknown format '{item.Format}' for UDT {item.UdtName}. Allowed: {FormatSd}, {FormatXml}.");
          }
        }
      }
      string libraryError = cTiaLibraryHelpers.ResolveWriteOptions(request.LibraryMode, request.LibraryVersion, request.LibraryAuthor, request.LibraryComment, out LibraryWriteOptions library);
      if (libraryError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, libraryError);
      }

      return cTiaFileBatchHelpers.ExecuteBatch(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<PutResult>>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        var batch = cTiaFileBatchHelpers.NewBatch<PutResult>(deviceName, deviceItemName, plcSoftware);
        cTiaFileBatchHelpers.ImportAll(batch, request.Items,
          item => ImportUdt(project, plcSoftware, plcTypeGroup, deviceName, deviceItemName, item.UdtName.Trim(), item.Path, library,
            (p, filePath) => cTiaBlocks.LoadImportDocuments(fullRoot, p.Name, filePath, item.UdtName.Trim(), formats[item], "UDT")),
          item => item.UdtName, item => item.Path);
        return ItemResult<FileBatchResult<PutResult>>.Ok(batch);
      });
    }

    /// <summary>
    /// Verwirft die In-Test-Version des Bibliothekstyps, dessen Instanz der Datentyp ist (HTTP POST).
    /// Die Instanzen fallen auf die zuletzt freigegebene Version zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="udtName">Name des Datentyps (Instanz des Bibliothekstyps)</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <returns>JSON-String mit DiscardTypeVersionResult oder Fehlermeldung</returns>
    [RestRoute(UDTRoutes.DiscardTypeVersion, "POST")]
    static public string DiscardTypeVersion(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string typeName = context.Request.QueryString["udtName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      return cTiaFileBatchHelpers.ExecuteItem(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
        if (errorMessage != null) {
          return ItemResult<DiscardTypeVersionResult>.Fail(HttpStatusCode.NotFound, errorMessage);
        }

        PlcType type = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
        if (type == null) {
          return ItemResult<DiscardTypeVersionResult>.Fail(HttpStatusCode.NotFound, $"Error: Type with name {typeName} not found in PLC software {plcSoftware.Name}.");
        }
        LibraryTypeInstanceInfo instanceInfo = cTiaLibraryHelpers.GetInstanceInfo(type);
        if (instanceInfo == null) {
          return ItemResult<DiscardTypeVersionResult>.Fail(HttpStatusCode.BadRequest, $"Error: Type {typeName} is not connected to a library type.");
        }
        LibraryTypeVersion version = instanceInfo.LibraryTypeVersion;
        if (version.State != LibraryTypeVersionState.InWork) {
          return ItemResult<DiscardTypeVersionResult>.Fail(HttpStatusCode.Conflict, $"Error: Type {typeName} is connected to released {cTiaLibraryHelpers.Describe(version)}; there is no in-test version to discard.");
        }

        string discardedVersion = version.VersionNumber?.ToString();
        try {
          version.Discard();
        } catch (Exception ex) {
          return ItemResult<DiscardTypeVersionResult>.Fail(HttpStatusCode.InternalServerError, $"Error: Discarding {cTiaLibraryHelpers.Describe(version)} failed: {ex.Message}");
        }

        PlcType current = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
        return ItemResult<DiscardTypeVersionResult>.Ok(new DiscardTypeVersionResult {
          DeviceName = deviceName,
          DeviceItemName = deviceItemName,
          PlcName = plcSoftware.Name,
          UdtName = current?.Name ?? typeName,
          Path = current != null ? GetTypePath(current) : null,
          Library = cTiaLibraryHelpers.GetLibraryInfo(current),
          DiscardedVersion = discardedVersion
        });
      });
    }

    #region Hilfsmethoden für TIA Openness Objektstruktur

    /// <summary>
    /// Führt den Import eines Datentyps als Einzelaufruf aus und setzt die Antwort.
    /// </summary>
    static private string ImportUdt(HttpListenerContext context, string processIdStr, string projectName, string deviceName, string deviceItemName,
      string typeName, string[] requestPath, LibraryWriteOptions library, Func<Project, string[], ItemResult<ImportSource>> loadSource) {
      return cTiaFileBatchHelpers.ExecuteItem(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcSystemTypeGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup);
        if (errorMessage != null) {
          return ItemResult<PutResult>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        return ImportUdt(project, plcSoftware, plcTypeGroup, deviceName, deviceItemName, typeName, requestPath, library, loadSource);
      });
    }

    /// <summary>
    /// Legt einen Datentyp neu an oder überschreibt einen bestehenden Datentyp.
    /// Ein bestehender Datentyp bleibt in seinem Ordner (Verschieben würde Verwendungsstellen in Bausteinen brechen).
    /// Instanzen von Bibliothekstypen werden nur mit library.Mode InTest/Release geschrieben, und zwar als In-Test-Version des Typs.
    /// </summary>
    /// <param name="requestPath">Zielordner für neue Datentypen; null = Wurzelordner bzw. bisheriger Ordner</param>
    /// <param name="loadSource">Liefert Format und Inhalt zum Dateiordner (Path aus dem Request, sonst bisheriger Ordner bzw. Wurzelordner)</param>
    /// <returns>PutResult bzw. PutFileResult oder Fehler</returns>
    static private ItemResult<PutResult> ImportUdt(Project project, PlcSoftware plcSoftware, PlcTypeGroup plcTypeGroup, string deviceName, string deviceItemName,
      string typeName, string[] requestPath, LibraryWriteOptions library, Func<Project, string[], ItemResult<ImportSource>> loadSource) {
      PlcType existing = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
      if (existing != null && existing.IsKnowHowProtected) {
        return ItemResult<PutResult>.Fail(HttpStatusCode.Conflict, $"Error: Type {typeName} is know-how-protected and cannot be overwritten.");
      }

      LibraryTypeInstanceInfo instanceInfo = cTiaLibraryHelpers.GetInstanceInfo(existing);
      string libraryMode = library.ModeFor(instanceInfo);
      if (instanceInfo != null && libraryMode == cTiaLibraryHelpers.ModeNone) {
        return ItemResult<PutResult>.Fail(HttpStatusCode.Conflict,
          $"Error: Type {typeName} is an instance of {cTiaLibraryHelpers.Describe(instanceInfo.LibraryTypeVersion)} and cannot be overwritten by a plain import. " +
          $"Pass libraryMode '{cTiaLibraryHelpers.ModeInTest}' to write the content as in-test version of the type, " +
          $"or '{cTiaLibraryHelpers.ModeRelease}' to write and release it as a new version.");
      }
      if (instanceInfo == null && libraryMode != cTiaLibraryHelpers.ModeNone) {
        return ItemResult<PutResult>.Fail(HttpStatusCode.BadRequest,
          $"Error: libraryMode is only allowed for existing instances of library types; type {typeName} " +
          (existing == null ? "does not exist." : "is not connected to a library type."));
      }

      string[] existingPath = existing != null ? GetTypePath(existing) : null;
      ItemResult<ImportSource> loaded = loadSource(project, requestPath ?? existingPath ?? new string[0]);
      if (loaded.Failed) {
        return ItemResult<PutResult>.Fail(loaded.Status, loaded.Error);
      }
      ImportSource source = loaded.Value;

      var messages = new List<string>();
      if (existing != null && requestPath != null && !PathEquals(existingPath, requestPath)) {
        messages.Add($"Type {existing.Name} already exists in folder '{string.Join("/", existingPath)}'; it was overwritten there and not moved to '{string.Join("/", requestPath)}'.");
      }

      PlcType imported;
      string[] importMessages;
      string errorMessage;
      if (instanceInfo != null) {
        errorMessage = ImportTypeVersion(existing, instanceInfo, typeName, source, library, out importMessages);
        if (errorMessage != null) {
          return ItemResult<PutResult>.Fail(HttpStatusCode.InternalServerError, errorMessage + cTiaBlocks.FormatMessages(importMessages));
        }
        imported = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
      } else {
        // Verschieben würde Löschen und Neuanlegen bedeuten und Verwendungsstellen in Bausteinen brechen.
        PlcTypeGroup targetGroup = existing != null
          ? GetTypeGroup(existing) ?? plcTypeGroup
          : EnsureTypeGroup(plcTypeGroup, requestPath ?? new string[0]);
        errorMessage = Import(targetGroup, typeName, source.Format, source.Content, source.MultiLingualText, out imported, out importMessages);
        if (errorMessage != null) {
          return ItemResult<PutResult>.Fail(HttpStatusCode.InternalServerError, errorMessage);
        }
        if (imported == null) {
          imported = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
        }
      }
      messages.AddRange(importMessages);
      if (imported == null) {
        return ItemResult<PutResult>.Fail(HttpStatusCode.InternalServerError, $"Error: Import finished, but type {typeName} was not found afterwards.");
      }
      if (!imported.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)) {
        messages.Add($"Imported type is named '{imported.Name}', not '{typeName}'.");
      }

      PutResult result = source.Files == null
        ? new PutResult()
        : new PutFileResult { Directory = source.Directory, Files = source.Files };
      result.DeviceName = deviceName;
      result.DeviceItemName = deviceItemName;
      result.PlcName = plcSoftware.Name;
      result.UdtName = imported.Name;
      result.Path = GetTypePath(imported);
      result.Library = cTiaLibraryHelpers.GetLibraryInfo(imported);
      result.Created = existing == null;
      result.Format = source.Format;
      result.Messages = messages.ToArray();
      return ItemResult<PutResult>.Ok(result);
    }

    /// <summary>
    /// Schreibt den Inhalt einer Instanz eines Bibliothekstyps als In-Test-Version in den Typ (optional mit Freigabe).
    /// </summary>
    static private string ImportTypeVersion(PlcType type, LibraryTypeInstanceInfo instanceInfo, string typeName, ImportSource source, LibraryWriteOptions library, out string[] messages) {
      messages = new string[0];
      string safeName = cTiaExportHelpers.SanitizeFileName(typeName);
      DirectoryInfo importDir = cTiaExportHelpers.EnsureImportDirectory();
      try {
        cTiaExportHelpers.TryDeleteExportedDocument(importDir.FullName, safeName);
        cTiaBlocks.WriteImportDocuments(importDir, safeName, source.Format, source.Content, source.MultiLingualText);
        return cTiaLibraryHelpers.WriteTypeVersion(instanceInfo, type.Parent, importDir, safeName, library, out messages);
      } catch (Exception ex) {
        return "Error: Writing library type version failed: " + ex.Message;
      } finally {
        cTiaExportHelpers.TryDeleteExportedDocument(importDir.FullName, safeName);
      }
    }

    /// <summary>
    /// Exportiert einen Datentyp (gesucht über den Namen) in Dateien unter Root\Projekt\Ordnerpfad des Datentyps.
    /// </summary>
    static private ItemResult<FileData> ExportUdtFiles(Project project, PlcSoftware plcSoftware, PlcTypeGroup plcTypeGroup, string deviceName, string deviceItemName, string typeName, string fullRoot) {
      PlcType type = cTiaFindHelpers.FindType(plcTypeGroup, typeName);
      if (type == null) {
        return ItemResult<FileData>.Fail(HttpStatusCode.NotFound, $"Error: Type with name {typeName} not found in PLC software {plcSoftware.Name}.");
      }
      return ExportUdtFiles(project, plcSoftware, type, deviceName, deviceItemName, fullRoot);
    }

    /// <summary>
    /// Exportiert einen Datentyp als SD-Dokument (.s7dcl, optional .s7res) unter Root\Projekt\Ordnerpfad des Datentyps.
    /// </summary>
    static private ItemResult<FileData> ExportUdtFiles(Project project, PlcSoftware plcSoftware, PlcType type, string deviceName, string deviceItemName, string fullRoot) {
      if (type.IsKnowHowProtected) {
        return ItemResult<FileData>.Fail(HttpStatusCode.Conflict, $"Error: Type {type.Name} is know-how-protected and cannot be exported.");
      }
      string[] typePath = GetTypePath(type);
      string errorMessage = cTiaExportHelpers.GetFileDirectory(fullRoot, project.Name, typePath, out string directory);
      if (errorMessage != null) {
        return ItemResult<FileData>.Fail(HttpStatusCode.BadRequest, errorMessage);
      }

      string safeName = cTiaExportHelpers.SanitizeFileName(type.Name);
      string[] files;
      try {
        Directory.CreateDirectory(directory);
        // Openness prüft DirectoryInfo.ToString(); das DirectoryInfo aus CreateDirectory liefert dort nur den Ordnernamen.
        var exportDir = new DirectoryInfo(directory);
        cTiaExportHelpers.DeleteFiles(exportDir.FullName, safeName, TypeFileExtensions);
        DocumentExportResult result = type.ExportAsDocuments(exportDir, safeName);
        if (result == null || result.State != DocumentResultState.Success) {
          return ItemResult<FileData>.Fail(HttpStatusCode.InternalServerError, $"Error exporting type {type.Name}: ExportAsDocuments failed: {cTiaExportHelpers.FormatExportMessages(result)}");
        }
        files = TypeFileExtensions
          .Select(extension => Path.Combine(exportDir.FullName, safeName + extension))
          .Where(File.Exists)
          .ToArray();
      } catch (Exception ex) {
        return ItemResult<FileData>.Fail(HttpStatusCode.InternalServerError, $"Error exporting type {type.Name}: {ex.Message}");
      }
      if (files.Length == 0) {
        return ItemResult<FileData>.Fail(HttpStatusCode.InternalServerError, $"Error exporting type {type.Name}: no file was written to {directory}.");
      }

      return ItemResult<FileData>.Ok(new FileData {
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
        PlcName = plcSoftware.Name,
        UdtName = type.Name,
        Path = typePath,
        Library = cTiaLibraryHelpers.GetLibraryInfo(type),
        Format = FormatSd,
        Directory = directory,
        Files = files
      });
    }

    /// <summary>
    /// Liefert die Datentypen eines Ordners, optional mit allen Unterordnern.
    /// </summary>
    static private IEnumerable<PlcType> EnumerateTypes(PlcTypeGroup group, bool recursive) {
      foreach (PlcType type in group.Types) {
        yield return type;
      }
      if (recursive) {
        foreach (PlcTypeUserGroup userGroup in group.Groups) {
          foreach (PlcType type in EnumerateTypes(userGroup, true)) {
            yield return type;
          }
        }
      }
    }

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
          Path = path,
          Library = cTiaLibraryHelpers.GetLibraryInfo(type)
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
