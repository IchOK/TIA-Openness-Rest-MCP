using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper;
using Tophinke.TiaOpenness.Tool.Types.PLC.FileBatch;
using Tophinke.TiaOpenness.Tool.Types.PLC.TagTable;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC {
  static internal class cTiaTagTables {
    private const string KindTag = "Tag";
    private const string KindUserConstant = "UserConstant";

    /// <summary>
    /// Gibt die Liste der Tagtabellen in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="path">Ordnerpfad in der TIA-Tagtabellen-Struktur (nicht Dateisystem): ein Parameter je Ordnername vom Wurzelordner (path=A&amp;path=B); inkl. Unterordner</query>
    /// <returns>JSON-String mit der Liste der Tagtabellen je PLC oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.List)]
    static public string List(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string[] filterPath = ReadPathFilter(context);

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(context, processIdStr, projectName, project => {
          var list = new List<PlcList>();
          foreach (Device device in project.Devices) {
            foreach (DeviceItem deviceItem in device.DeviceItems) {
              SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
              if (container != null && container.Software is PlcSoftware plcSoftware) {
                PlcTagTableGroup group = ResolveGroup<PlcTagTableGroup>(plcSoftware.TagTableGroup, filterPath, g => g.Groups, g => g.Name, out string[] groupPath);
                if (group == null) {
                  continue;
                }
                var tagTables = new List<Info>();
                List(group, tagTables, groupPath);
                list.Add(new PlcList {
                  DeviceName = device.Name,
                  DeviceItemName = deviceItem.Name,
                  PlcName = plcSoftware.Name,
                  TagTables = tagTables
                });
              }
            }
          }
          return list;
        });

        if (filterPath != null && retValue.Count == 0) {
          return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Tag table folder '{string.Join("/", filterPath)}' not found in any PLC.");
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
    /// Gibt die Tagtabelle mit dem angegebenen Namen in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="tagTableName">Name der Tagtabelle</query>
    /// <query name="deviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</query>
    /// <query name="deviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</query>
    /// <returns>JSON-String mit der Tagtabelle oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.Get)]
    static public string Get(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string tagTableName = context.Request.QueryString["tagTableName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(context, processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
          if (errorMessage != null) {
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          PlcTagTable tagTable = cTiaFindHelpers.FindTagTable(plcTagTableGroup, tagTableName);
          if (tagTable == null) {
            errorMessage = $"Error: Tag table with name {tagTableName} not found in PLC software {plcSoftware.Name}.";
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          Language language = project.LanguageSettings.EditingLanguage;

          var tags = new List<TagInfo>();
          foreach (PlcTag tag in tagTable.Tags) {
            tags.Add(ToTagInfo(tag, language));
          }

          var userConstants = new List<ConstantInfo>();
          foreach (PlcUserConstant constant in tagTable.UserConstants) {
            userConstants.Add(ToConstantInfo(constant, language));
          }

          var systemConstants = new List<ConstantInfo>();
          foreach (PlcSystemConstant constant in tagTable.SystemConstants) {
            systemConstants.Add(new ConstantInfo {
              Name = constant.Name,
              DataTypeName = constant.DataTypeName,
              Value = constant.Value
            });
          }

          var data = new Data {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            TagTableName = tagTable.Name,
            IsDefault = tagTable.IsDefault,
            Path = GetTagTablePath(tagTable),
            ModifiedDate = cTiaExportHelpers.TryGetUtcAttribute(tagTable, "ModifiedTimeStamp"),
            Tags = tags,
            UserConstants = userConstants,
            SystemConstants = systemConstants
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
    /// Legt eine Tagtabelle neu an oder überschreibt deren Inhalt (HTTP PUT).
    /// Tags und UserConstants im Request sind der vollständige Soll-Inhalt: fehlende Einträge werden gelöscht,
    /// geänderte angepasst und neue angelegt. Eine bestehende Tabelle bleibt in ihrem Ordner.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="tagTableName">Name der Tagtabelle</query>
    /// <query name="deviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</query>
    /// <query name="deviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</query>
    /// <body>PutRequest mit Tags, UserConstants und optional Path</body>
    /// <returns>JSON-String mit PutResult oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.Put, "PUT")]
    static public string Put(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string tagTableName = context.Request.QueryString["tagTableName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      if (string.IsNullOrWhiteSpace(tagTableName)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: tagTableName must be provided.");
      }

      PutRequest request;
      try {
        request = ReadBody<PutRequest>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      if (request == null || request.Tags == null || request.UserConstants == null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain Tags and UserConstants (empty lists are allowed; they delete all entries).");
      }
      if (request.Path != null && request.Path.Any(string.IsNullOrWhiteSpace)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Path must not contain empty folder names.");
      }
      string validationError = ValidateEntries(request.Tags, request.UserConstants, true);
      if (validationError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, validationError);
      }

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(context, processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
          if (errorMessage != null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, errorMessage);
          }

          PlcTagTable tagTable = cTiaFindHelpers.FindTagTable(plcTagTableGroup, tagTableName);

          // Namen von Variablen und Konstanten sind in der ganzen PLC eindeutig.
          var requestedNames = request.Tags.Select(t => t.Name).Concat(request.UserConstants.Select(c => c.Name));
          List<string> conflicts = FindNamesInOtherTables(plcTagTableGroup, tagTable, new HashSet<string>(requestedNames, StringComparer.OrdinalIgnoreCase));
          if (conflicts.Count > 0) {
            return ErrorResponse(context, HttpStatusCode.Conflict, $"Error: Names already used in other tag tables: {string.Join(", ", conflicts)}. Nothing was changed.");
          }

          var messages = new List<string>();
          bool created = tagTable == null;
          if (created) {
            PlcTagTableGroup targetGroup = EnsureTagTableGroup(plcTagTableGroup, request.Path ?? new string[0]);
            tagTable = targetGroup.TagTables.Create(tagTableName);
          } else if (request.Path != null && !PathEquals(GetTagTablePath(tagTable), request.Path)) {
            messages.Add($"Tag table {tagTable.Name} already exists in folder '{string.Join("/", GetTagTablePath(tagTable))}'; its content was replaced there and the table was not moved to '{string.Join("/", request.Path)}'.");
          }

          Language language = project.LanguageSettings.EditingLanguage;
          var changes = new List<Change>();

          var requestedTags = new HashSet<string>(request.Tags.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
          var requestedConstants = new HashSet<string>(request.UserConstants.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
          foreach (PlcTag tag in tagTable.Tags.ToList()) {
            if (!requestedTags.Contains(tag.Name)) {
              changes.Add(Delete(KindTag, tag.Name, tag.Delete));
            }
          }
          foreach (PlcUserConstant constant in tagTable.UserConstants.ToList()) {
            if (!requestedConstants.Contains(constant.Name)) {
              changes.Add(Delete(KindUserConstant, constant.Name, constant.Delete));
            }
          }

          foreach (TagInfo tagInfo in request.Tags) {
            PlcTag tag = tagTable.Tags.Find(tagInfo.Name);
            changes.Add(tag != null ? UpdateTag(tag, tagInfo, language) : CreateTag(tagTable, tagInfo, language));
          }
          foreach (ConstantInfo constantInfo in request.UserConstants) {
            PlcUserConstant constant = tagTable.UserConstants.Find(constantInfo.Name);
            changes.Add(constant != null ? UpdateConstant(constant, constantInfo, language) : CreateConstant(tagTable, constantInfo, language));
          }

          var result = new PutResult {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            TagTableName = tagTable.Name,
            Path = GetTagTablePath(tagTable),
            ModifiedDate = cTiaExportHelpers.TryGetUtcAttribute(tagTable, "ModifiedTimeStamp"),
            Created = created,
            Changes = changes,
            Messages = messages.ToArray()
          };

          if (changes.Any(c => c.Error != null)) {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
          }
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

    /// <summary>
    /// Ändert einzelne Felder bestehender Variablen und Konstanten einer Tagtabelle (HTTP PATCH).
    /// Felder mit null bleiben unverändert; Einträge werden weder angelegt noch gelöscht.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="tagTableName">Name der Tagtabelle</query>
    /// <query name="deviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</query>
    /// <query name="deviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</query>
    /// <body>PatchRequest mit den zu ändernden Tags und UserConstants</body>
    /// <returns>JSON-String mit PatchResult oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.Patch, "PATCH")]
    static public string Patch(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string tagTableName = context.Request.QueryString["tagTableName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      if (string.IsNullOrWhiteSpace(tagTableName)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: tagTableName must be provided.");
      }

      PatchRequest request;
      try {
        request = ReadBody<PatchRequest>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      var tagUpdates = request?.Tags ?? new List<TagInfo>();
      var constantUpdates = request?.UserConstants ?? new List<ConstantInfo>();
      if (tagUpdates.Count + constantUpdates.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain at least one entry in Tags or UserConstants.");
      }
      string validationError = ValidateEntries(tagUpdates, constantUpdates, false);
      if (validationError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, validationError);
      }

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(context, processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
          if (errorMessage != null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, errorMessage);
          }

          PlcTagTable tagTable = cTiaFindHelpers.FindTagTable(plcTagTableGroup, tagTableName);
          if (tagTable == null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Tag table with name {tagTableName} not found in PLC software {plcSoftware.Name}.");
          }

          var tags = new List<KeyValuePair<TagInfo, PlcTag>>();
          var constants = new List<KeyValuePair<ConstantInfo, PlcUserConstant>>();
          var missing = new List<string>();
          foreach (TagInfo tagInfo in tagUpdates) {
            PlcTag tag = tagTable.Tags.Find(tagInfo.Name);
            if (tag == null) {
              missing.Add($"tag {tagInfo.Name}");
            } else {
              tags.Add(new KeyValuePair<TagInfo, PlcTag>(tagInfo, tag));
            }
          }
          foreach (ConstantInfo constantInfo in constantUpdates) {
            PlcUserConstant constant = tagTable.UserConstants.Find(constantInfo.Name);
            if (constant == null) {
              missing.Add($"user constant {constantInfo.Name}");
            } else {
              constants.Add(new KeyValuePair<ConstantInfo, PlcUserConstant>(constantInfo, constant));
            }
          }
          if (missing.Count > 0) {
            return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Not found in tag table {tagTable.Name}: {string.Join(", ", missing)}. Nothing was changed. Use PUT to create entries.");
          }

          Language language = project.LanguageSettings.EditingLanguage;
          var changes = new List<Change>();
          foreach (var pair in tags) {
            changes.Add(UpdateTag(pair.Value, pair.Key, language));
          }
          foreach (var pair in constants) {
            changes.Add(UpdateConstant(pair.Value, pair.Key, language));
          }

          var result = new PatchResult {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            TagTableName = tagTable.Name,
            Changes = changes
          };

          if (changes.Any(c => c.Error != null)) {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
          }
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

    /// <summary>
    /// Exportiert eine Tagtabelle als SimaticML-XML in das angegebene Dateisystem-Verzeichnis (HTTP POST).
    /// Alle Parameter liegen im JSON-Body; die URL enthält nur den Routenpfad.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <body name="ProcessId" type="number">Prozess-ID des TIA-Projekts</body>
    /// <body name="ProjectName">Name des TIA-Projekts</body>
    /// <body name="DeviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</body>
    /// <body name="DeviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</body>
    /// <body name="Directory">Absolutes Dateisystem-Verzeichnis; Datei wird direkt dort geschrieben</body>
    /// <body name="TagTableName">Name der Tagtabelle</body>
    /// <body name="Path" type="string[]">Optional: TIA-Ordner (Array); ohne Angabe wird der ganze PLC durchsucht</body>
    /// <returns>JSON-String mit FileData oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.GetFile, "POST")]
    static public string GetFile(HttpListenerContext context) {
      GetFileRequest request;
      try {
        request = ReadBody<GetFileRequest>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      string targetError = cTiaFileBatchHelpers.ValidatePlcTarget(request, out string processIdStr, out string projectName, out string deviceName, out string deviceItemName);
      if (targetError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, targetError);
      }
      if (string.IsNullOrWhiteSpace(request.TagTableName)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: TagTableName must be provided.");
      }
      if (request.Path != null && request.Path.Any(string.IsNullOrWhiteSpace)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Path must not contain empty folder names.");
      }
      string dirError = cTiaExportHelpers.ResolveDirectory(request.Directory, out string directory);
      if (dirError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, dirError);
      }

      string tagTableName = request.TagTableName.Trim();
      string[] tiaPath = request.Path;
      return cTiaFileBatchHelpers.ExecuteItem(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
        if (errorMessage != null) {
          return ItemResult<FileData>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        return ExportTagTableFile(plcSoftware, plcTagTableGroup, deviceName, deviceItemName, tagTableName, directory, tiaPath);
      });
    }

    /// <summary>
    /// Exportiert alle Tagtabellen eines TIA-Tagtabellen-Ordners als XML-Dateien (HTTP POST).
    /// Unterordner im TIA-Pfad relativ zu path werden als Unterverzeichnisse unter directory angelegt.
    /// Alle Parameter liegen im JSON-Body; die URL enthält nur den Routenpfad.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <body name="ProcessId" type="number">Prozess-ID des TIA-Projekts</body>
    /// <body name="ProjectName">Name des TIA-Projekts</body>
    /// <body name="DeviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</body>
    /// <body name="DeviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</body>
    /// <body name="Directory">Absolutes Dateisystem-Verzeichnis, das dem TIA-Ordner Path entspricht</body>
    /// <body name="Path" type="string[]">Optional: TIA-Startordner (Array); ohne Angabe der Tagtabellen-Wurzel</body>
    /// <body name="Recursive" type="boolean">Unterordner einbeziehen (Standard: true)</body>
    /// <returns>JSON-String mit FileBatchResult von FileData oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.GetDir, "POST")]
    static public string GetDir(HttpListenerContext context) {
      string requestError = cTiaFileBatchHelpers.ReadGetDirRequest(context, out GetDirRequest request, out string processIdStr, out string projectName, out string deviceName, out string deviceItemName, out string directory, out bool recursive);
      if (requestError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, requestError);
      }
      string[] folderPath = request.Path;

      return cTiaFileBatchHelpers.ExecuteBatch(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<FileData>>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        PlcTagTableGroup group = ResolveGroup<PlcTagTableGroup>(plcTagTableGroup, folderPath, g => g.Groups, g => g.Name, out string[] groupPath);
        if (group == null) {
          return ItemResult<FileBatchResult<FileData>>.Fail(HttpStatusCode.NotFound, $"Error: Tag table folder '{string.Join("/", folderPath)}' not found in PLC software {plcSoftware.Name}.");
        }

        var batch = cTiaFileBatchHelpers.NewBatch<FileData>(deviceName, deviceItemName, plcSoftware);
        batch.Directory = directory;
        foreach (PlcTagTable table in EnumerateTagTables(group, recursive)) {
          string[] relative = cTiaExportHelpers.SkipPrefix(GetTagTablePath(table), groupPath);
          errorMessage = cTiaExportHelpers.CombineDirectory(directory, relative, out string tableDir);
          if (errorMessage != null) {
            batch.Errors.Add(new FileBatchError { Name = table.Name, Path = GetTagTablePath(table), StatusCode = (int)HttpStatusCode.BadRequest, Error = errorMessage });
            continue;
          }
          cTiaFileBatchHelpers.Collect(batch, ExportTagTableFile(plcSoftware, table, deviceName, deviceItemName, tableDir), table.Name, GetTagTablePath(table));
        }
        return ItemResult<FileBatchResult<FileData>>.Ok(batch);
      });
    }

    /// <summary>
    /// Exportiert mehrere Tagtabellen als XML-Dateien (wie GetFile für jeden Eintrag) (HTTP POST).
    /// Alle Parameter liegen im JSON-Body; die URL enthält nur den Routenpfad.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <body name="ProcessId" type="number">Prozess-ID des TIA-Projekts</body>
    /// <body name="ProjectName">Name des TIA-Projekts</body>
    /// <body name="DeviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</body>
    /// <body name="DeviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</body>
    /// <body name="Items" type="json">Array von Objekten: Directory, TagTableName, optional Path</body>
    /// <returns>JSON-String mit FileBatchResult von FileData oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.GetFileList, "POST")]
    static public string GetFileList(HttpListenerContext context) {
      GetFileListRequest<PutFileListItem> request;
      try {
        request = ReadBody<GetFileListRequest<PutFileListItem>>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      string targetError = cTiaFileBatchHelpers.ValidatePlcTarget(request, out string processIdStr, out string projectName, out string deviceName, out string deviceItemName);
      if (targetError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, targetError);
      }
      if (request.Items == null || request.Items.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain at least one entry in Items.");
      }
      if (request.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.TagTableName) || string.IsNullOrWhiteSpace(i.Directory))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Every entry in Items needs Directory and TagTableName.");
      }

      return cTiaFileBatchHelpers.ExecuteBatch(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<FileData>>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        var batch = cTiaFileBatchHelpers.NewBatch<FileData>(deviceName, deviceItemName, plcSoftware);
        foreach (PutFileListItem item in request.Items) {
          string dirError = cTiaExportHelpers.ResolveDirectory(item.Directory, out string directory);
          if (dirError != null) {
            batch.Errors.Add(new FileBatchError { Name = item.TagTableName, Path = item.Path, StatusCode = (int)HttpStatusCode.BadRequest, Error = dirError });
            continue;
          }
          cTiaFileBatchHelpers.Collect(batch, ExportTagTableFile(plcSoftware, plcTagTableGroup, deviceName, deviceItemName, item.TagTableName.Trim(), directory, item.Path), item.TagTableName, item.Path);
        }
        return ItemResult<FileBatchResult<FileData>>.Ok(batch);
      });
    }

    /// <summary>
    /// Legt eine Tagtabelle aus Directory\&lt;Name&gt;.xml neu an oder überschreibt eine bestehende Tabelle (HTTP PUT).
    /// Path ist der TIA-Zielordner für neue Tabellen; eine bestehende Tabelle bleibt in ihrem Ordner.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="tagTableName">Name der Tagtabelle</query>
    /// <query name="deviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</query>
    /// <query name="deviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</query>
    /// <body name="Directory">Absolutes Dateisystem-Verzeichnis mit der XML-Datei</body>
    /// <body name="Path" type="string[]">Optional: TIA-Ordner für neue Tabellen (Array)</body>
    /// <returns>JSON-String mit PutFileResult oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.PutFile, "PUT")]
    static public string PutFile(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string tagTableName = context.Request.QueryString["tagTableName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      if (string.IsNullOrWhiteSpace(tagTableName)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: tagTableName must be provided.");
      }

      PutFileRequest request;
      try {
        request = ReadBody<PutFileRequest>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      if (request == null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain Directory.");
      }
      if (request.Path != null && request.Path.Any(string.IsNullOrWhiteSpace)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Path must not contain empty folder names.");
      }
      string dirError = cTiaExportHelpers.ResolveDirectory(request.Directory, out string directory);
      if (dirError != null) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, dirError);
      }

      return cTiaFileBatchHelpers.ExecuteItem(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
        if (errorMessage != null) {
          return ItemResult<PutFileResult>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        return ImportTagTableFile(plcSoftware, plcTagTableGroup, deviceName, deviceItemName, tagTableName, request.Path, directory);
      });
    }

    /// <summary>
    /// Importiert alle Tagtabellen-Dateien (&lt;Name&gt;.xml) aus einem Dateisystem-Verzeichnis (optional mit Unterverzeichnissen) (HTTP PUT).
    /// Directory entspricht dem TIA-Ordner Path; Unterverzeichnisse werden an Path angehängt.
    /// Jede Datei wird wie bei PutFile importiert; Dateien von Bausteinen und Datentypen werden übersprungen.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</query>
    /// <query name="deviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</query>
    /// <body name="Directory">Absolutes Dateisystem-Verzeichnis, das dem TIA-Ordner Path entspricht</body>
    /// <body name="Path" type="string[]">Optional: TIA-Zielordner (Array); ohne Angabe die Tagtabellen-Wurzel</body>
    /// <body name="Recursive" type="boolean">Unterverzeichnisse einbeziehen (Standard: true)</body>
    /// <returns>JSON-String mit FileBatchResult von PutFileResult oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.PutDir, "PUT")]
    static public string PutDir(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      string requestError = cTiaBlocks.ReadPutDirRequest(context, out PutDirRequest request, out string directory, out _);
      if (requestError != null) {
        return requestError;
      }

      return cTiaFileBatchHelpers.ExecuteBatch(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<PutFileResult>>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        var batch = cTiaFileBatchHelpers.NewBatch<PutFileResult>(deviceName, deviceItemName, plcSoftware);
        var scan = cTiaFileBatchHelpers.ScanDirectory(directory, request.Path, request.Recursive ?? true, cTiaFileBatchHelpers.KindTagTable, FormatXml, batch);
        if (scan.Failed) {
          return ItemResult<FileBatchResult<PutFileResult>>.Fail(scan.Status, scan.Error);
        }
        cTiaFileBatchHelpers.ImportAll(batch, scan.Value,
          entry => ImportTagTableFile(plcSoftware, plcTagTableGroup, deviceName, deviceItemName, entry.Name, entry.Path, entry.Directory),
          entry => entry.Name, entry => entry.Path);
        return ItemResult<FileBatchResult<PutFileResult>>.Ok(batch);
      });
    }

    /// <summary>
    /// Importiert mehrere Tagtabellen aus XML-Dateien (wie PutFile für jeden Eintrag) (HTTP PUT).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name der PLC-Station (DeviceName aus /api/plc/devices/list)</query>
    /// <query name="deviceItemName">Name der CPU/des Geräteelements (DeviceItemName aus /api/plc/devices/list)</query>
    /// <body name="Items" type="json">Array von Objekten: Directory, TagTableName, optional Path</body>
    /// <returns>JSON-String mit FileBatchResult von PutFileResult oder Fehlermeldung</returns>
    [RestRoute(TagTableRoutes.PutFileList, "PUT")]
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
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain at least one entry in Items.");
      }
      if (request.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.TagTableName) || string.IsNullOrWhiteSpace(i.Directory))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Every entry in Items needs Directory and TagTableName.");
      }
      if (request.Items.Any(i => i.Path != null && i.Path.Any(string.IsNullOrWhiteSpace))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Path must not contain empty folder names.");
      }

      return cTiaFileBatchHelpers.ExecuteBatch(context, processIdStr, projectName, project => {
        var errorMessage = cTiaProject.GetPlcTagTableGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup);
        if (errorMessage != null) {
          return ItemResult<FileBatchResult<PutFileResult>>.Fail(HttpStatusCode.NotFound, errorMessage);
        }
        var batch = cTiaFileBatchHelpers.NewBatch<PutFileResult>(deviceName, deviceItemName, plcSoftware);
        cTiaFileBatchHelpers.ImportAll(batch, request.Items,
          item => {
            string dirError = cTiaExportHelpers.ResolveDirectory(item.Directory, out string directory);
            if (dirError != null) {
              return ItemResult<PutFileResult>.Fail(HttpStatusCode.BadRequest, dirError);
            }
            return ImportTagTableFile(plcSoftware, plcTagTableGroup, deviceName, deviceItemName, item.TagTableName.Trim(), item.Path, directory);
          },
          item => item.TagTableName, item => item.Path);
        return ItemResult<FileBatchResult<PutFileResult>>.Ok(batch);
      });
    }

    #region Hilfsmethoden für TIA Openness Objektstruktur

    /// <summary>
    /// Exportiert eine Tagtabelle (gesucht über den Namen, optional in einem TIA-Ordner) in das angegebene Dateisystem-Verzeichnis.
    /// </summary>
    static private ItemResult<FileData> ExportTagTableFile(PlcSoftware plcSoftware, PlcTagTableGroup plcTagTableGroup, string deviceName, string deviceItemName, string tagTableName, string directory, string[] tiaPath) {
      PlcTagTableGroup searchGroup = plcTagTableGroup;
      if (tiaPath != null && tiaPath.Length > 0) {
        searchGroup = ResolveGroup<PlcTagTableGroup>(plcTagTableGroup, tiaPath, g => g.Groups, g => g.Name, out _);
        if (searchGroup == null) {
          return ItemResult<FileData>.Fail(HttpStatusCode.NotFound, $"Error: Tag table folder '{string.Join("/", tiaPath)}' not found in PLC software {plcSoftware.Name}.");
        }
      }
      PlcTagTable table = cTiaFindHelpers.FindTagTable(searchGroup, tagTableName);
      if (table == null) {
        string where = tiaPath != null && tiaPath.Length > 0 ? $" in folder '{string.Join("/", tiaPath)}'" : "";
        return ItemResult<FileData>.Fail(HttpStatusCode.NotFound, $"Error: Tag table with name {tagTableName} not found{where} in PLC software {plcSoftware.Name}.");
      }
      return ExportTagTableFile(plcSoftware, table, deviceName, deviceItemName, directory);
    }

    /// <summary>
    /// Exportiert eine Tagtabelle als XML-Datei &lt;Name&gt;.xml in das angegebene Dateisystem-Verzeichnis; eine vorhandene Datei wird ersetzt.
    /// </summary>
    static private ItemResult<FileData> ExportTagTableFile(PlcSoftware plcSoftware, PlcTagTable table, string deviceName, string deviceItemName, string directory) {
      string[] tablePath = GetTagTablePath(table);

      string file = Path.Combine(directory, cTiaExportHelpers.SanitizeFileName(table.Name) + ".xml");
      try {
        Directory.CreateDirectory(directory);
        if (File.Exists(file)) {
          File.Delete(file);
        }
        table.Export(new FileInfo(file), ExportOptions.WithDefaults);
      } catch (Exception ex) {
        return ItemResult<FileData>.Fail(HttpStatusCode.InternalServerError, $"Error exporting tag table {table.Name}: {ex.Message}");
      }
      if (!File.Exists(file)) {
        return ItemResult<FileData>.Fail(HttpStatusCode.InternalServerError, $"Error exporting tag table {table.Name}: no file was written to {directory}.");
      }

      return ItemResult<FileData>.Ok(new FileData {
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
        PlcName = plcSoftware.Name,
        TagTableName = table.Name,
        IsDefault = table.IsDefault,
        Path = tablePath,
        ModifiedDate = cTiaExportHelpers.TryGetUtcAttribute(table, "ModifiedTimeStamp"),
        Format = FormatXml,
        Directory = directory,
        Files = new[] { file }
      });
    }

    /// <summary>
    /// Importiert eine Tagtabelle aus Directory\&lt;Name&gt;.xml.
    /// Eine bestehende Tabelle wird in ihrem Ordner überschrieben; eine neue wird im Ordner Path angelegt.
    /// </summary>
    /// <param name="requestPath">TIA-Zielordner für neue Tabellen; null = bisheriger Ordner bzw. Wurzelordner</param>
    static private ItemResult<PutFileResult> ImportTagTableFile(PlcSoftware plcSoftware, PlcTagTableGroup plcTagTableGroup, string deviceName, string deviceItemName,
      string tagTableName, string[] requestPath, string directory) {
      PlcTagTable existing = cTiaFindHelpers.FindTagTable(plcTagTableGroup, tagTableName);
      string[] existingPath = existing != null ? GetTagTablePath(existing) : null;

      string errorMessage = cTiaExportHelpers.ResolveDirectory(directory, out string fullDirectory);
      if (errorMessage != null) {
        return ItemResult<PutFileResult>.Fail(HttpStatusCode.BadRequest, errorMessage);
      }
      string file = Path.Combine(fullDirectory, cTiaExportHelpers.SanitizeFileName(tagTableName) + ".xml");
      if (!File.Exists(file)) {
        return ItemResult<PutFileResult>.Fail(HttpStatusCode.NotFound, $"Error: Tag table file not found: {file}.");
      }
      string kind = cTiaFileBatchHelpers.DetectKind(file);
      if (kind != cTiaFileBatchHelpers.KindTagTable) {
        return ItemResult<PutFileResult>.Fail(HttpStatusCode.BadRequest, $"Error: {file} is not a SimaticML export of a tag table" + (kind != null ? $" but of a {kind}." : "."));
      }

      var messages = new List<string>();
      PlcTagTableGroup targetGroup;
      if (existing != null) {
        targetGroup = GetTagTableGroup(existing) ?? plcTagTableGroup;
        if (requestPath != null && !PathEquals(existingPath, requestPath)) {
          messages.Add($"Tag table {existing.Name} already exists in folder '{string.Join("/", existingPath)}'; it was overwritten there and not moved to '{string.Join("/", requestPath)}'.");
        }
      } else {
        targetGroup = EnsureTagTableGroup(plcTagTableGroup, requestPath ?? new string[0]);
      }

      PlcTagTable imported;
      try {
        imported = targetGroup.TagTables.Import(new FileInfo(file), ImportOptions.Override)?.FirstOrDefault();
      } catch (Exception ex) {
        return ItemResult<PutFileResult>.Fail(HttpStatusCode.InternalServerError, $"Error: Import of tag table {tagTableName} failed: {ex.Message}");
      }
      if (imported == null) {
        imported = cTiaFindHelpers.FindTagTable(plcTagTableGroup, tagTableName);
      }
      if (imported == null) {
        return ItemResult<PutFileResult>.Fail(HttpStatusCode.InternalServerError, $"Error: Import finished, but tag table {tagTableName} was not found afterwards.");
      }
      if (!imported.Name.Equals(tagTableName, StringComparison.OrdinalIgnoreCase)) {
        messages.Add($"Imported tag table is named '{imported.Name}', not '{tagTableName}'.");
      }

      return ItemResult<PutFileResult>.Ok(new PutFileResult {
        DeviceName = deviceName,
        DeviceItemName = deviceItemName,
        PlcName = plcSoftware.Name,
        TagTableName = imported.Name,
        IsDefault = imported.IsDefault,
        Path = GetTagTablePath(imported),
        ModifiedDate = cTiaExportHelpers.TryGetUtcAttribute(imported, "ModifiedTimeStamp"),
        Created = existing == null,
        Format = FormatXml,
        Messages = messages.ToArray(),
        Directory = fullDirectory,
        Files = new[] { file }
      });
    }

    /// <summary>
    /// Liefert die Tagtabellen eines Ordners, optional mit allen Unterordnern.
    /// </summary>
    static private IEnumerable<PlcTagTable> EnumerateTagTables(PlcTagTableGroup group, bool recursive) {
      foreach (PlcTagTable table in group.TagTables) {
        yield return table;
      }
      if (recursive) {
        foreach (PlcTagTableUserGroup userGroup in group.Groups) {
          foreach (PlcTagTable table in EnumerateTagTables(userGroup, true)) {
            yield return table;
          }
        }
      }
    }

    /// <summary>
    /// Ermittelt den Ordner, in dem eine Tagtabelle liegt.
    /// </summary>
    static private PlcTagTableGroup GetTagTableGroup(PlcTagTable table) {
      IEngineeringObject current = table.Parent;
      while (current != null && !(current is PlcTagTableGroup)) {
        current = current.Parent;
      }
      return current as PlcTagTableGroup;
    }

    /// <summary>
    /// Durchsucht die Ordnerstruktur einer PLC-Tagtabelle-Gruppe rekursiv nach Tagtabellen und fügt die gefundenen Informationen in eine Liste ein.
    /// </summary>
    /// <param name="group">aktuelle Tagtabelle-Gruppe</param>
    /// <param name="list">Liste, in die die gefundenen Informationen hinzugefügt werden</param>
    /// <param name="path">Ordnerpfad der Gruppe</param>
    static private void List(PlcTagTableGroup group, List<Info> list, string[] path = null) {
      if (path == null) {
        path = new string[0];
      }
      foreach (PlcTagTable table in group.TagTables) {
        list.Add(new Info {
          TagTableName = table.Name,
          IsDefault = table.IsDefault,
          Path = path,
          ModifiedDate = cTiaExportHelpers.TryGetUtcAttribute(table, "ModifiedTimeStamp")
        });
      }
      foreach (PlcTagTableUserGroup userGroup in group.Groups) {
        List(userGroup, list, path.Concat(new[] { userGroup.Name }).ToArray());
      }
    }

    /// <summary>
    /// Ermittelt den Ordnerpfad einer Tagtabelle.
    /// </summary>
    /// <returns>Namen der Benutzerordner vom Wurzelordner bis zur Tabelle; leer, wenn die Tabelle im Wurzelordner liegt</returns>
    static private string[] GetTagTablePath(PlcTagTable table) {
      var path = new List<string>();
      IEngineeringObject current = table.Parent;
      while (current != null && !(current is PlcTagTableSystemGroup)) {
        if (current is PlcTagTableUserGroup userGroup) {
          path.Insert(0, userGroup.Name);
        }
        current = current.Parent;
      }
      return path.ToArray();
    }

    /// <summary>
    /// Liefert den Tagtabellen-Ordner zum angegebenen Pfad und legt fehlende Ordner an.
    /// </summary>
    static private PlcTagTableGroup EnsureTagTableGroup(PlcTagTableGroup root, string[] path) {
      PlcTagTableGroup group = root;
      foreach (string name in path) {
        group = (PlcTagTableGroup)group.Groups.Find(name) ?? group.Groups.Create(name);
      }
      return group;
    }

    /// <summary>
    /// Sucht Namen, die bereits als Variable oder Konstante in einer anderen Tagtabelle verwendet werden.
    /// </summary>
    /// <returns>Liste im Format "Name (Tabelle)"</returns>
    static private List<string> FindNamesInOtherTables(PlcTagTableGroup group, PlcTagTable excludedTable, HashSet<string> names) {
      var conflicts = new List<string>();
      foreach (PlcTagTable table in group.TagTables) {
        if (excludedTable != null && table.Equals(excludedTable)) {
          continue;
        }
        foreach (PlcTag tag in table.Tags) {
          if (names.Contains(tag.Name)) {
            conflicts.Add($"{tag.Name} ({table.Name})");
          }
        }
        foreach (PlcUserConstant constant in table.UserConstants) {
          if (names.Contains(constant.Name)) {
            conflicts.Add($"{constant.Name} ({table.Name})");
          }
        }
      }
      foreach (PlcTagTableUserGroup userGroup in group.Groups) {
        conflicts.AddRange(FindNamesInOtherTables(userGroup, excludedTable, names));
      }
      return conflicts;
    }

    /// <summary>
    /// Prüft die Einträge eines PUT- oder PATCH-Requests.
    /// </summary>
    /// <param name="complete">true bei PUT: DataTypeName, LogicalAddress bzw. Value sind Pflicht</param>
    /// <returns>Fehlermeldung oder null</returns>
    static private string ValidateEntries(List<TagInfo> tags, List<ConstantInfo> constants, bool complete) {
      if (tags.Any(t => t == null || string.IsNullOrWhiteSpace(t.Name)) || constants.Any(c => c == null || string.IsNullOrWhiteSpace(c.Name))) {
        return "Error: Every entry in Tags and UserConstants needs a Name.";
      }
      var duplicates = tags.Select(t => t.Name.Trim()).Concat(constants.Select(c => c.Name.Trim()))
        .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
        .Where(g => g.Count() > 1)
        .Select(g => g.Key)
        .ToList();
      if (duplicates.Count > 0) {
        return $"Error: Duplicate names (tags and user constants share one namespace): {string.Join(", ", duplicates)}.";
      }
      if (complete) {
        var incompleteTags = tags.Where(t => string.IsNullOrWhiteSpace(t.DataTypeName) || string.IsNullOrWhiteSpace(t.LogicalAddress)).Select(t => t.Name).ToList();
        if (incompleteTags.Count > 0) {
          return $"Error: Tags need DataTypeName and LogicalAddress: {string.Join(", ", incompleteTags)}.";
        }
        var incompleteConstants = constants.Where(c => string.IsNullOrWhiteSpace(c.DataTypeName) || string.IsNullOrWhiteSpace(c.Value)).Select(c => c.Name).ToList();
        if (incompleteConstants.Count > 0) {
          return $"Error: User constants need DataTypeName and Value: {string.Join(", ", incompleteConstants)}.";
        }
      } else {
        var emptyTags = tags.Where(t => t.DataTypeName == null && t.LogicalAddress == null && t.Comment == null).Select(t => t.Name).ToList();
        var emptyConstants = constants.Where(c => c.DataTypeName == null && c.Value == null && c.Comment == null).Select(c => c.Name).ToList();
        if (emptyTags.Count + emptyConstants.Count > 0) {
          return $"Error: Entries without any field to change: {string.Join(", ", emptyTags.Concat(emptyConstants))}.";
        }
        var blankFields = tags.Where(t => (t.DataTypeName != null && t.DataTypeName.Trim() == "") || (t.LogicalAddress != null && t.LogicalAddress.Trim() == "")).Select(t => t.Name)
          .Concat(constants.Where(c => (c.DataTypeName != null && c.DataTypeName.Trim() == "") || (c.Value != null && c.Value.Trim() == "")).Select(c => c.Name))
          .ToList();
        if (blankFields.Count > 0) {
          return $"Error: DataTypeName, LogicalAddress and Value must not be empty (use null to keep them): {string.Join(", ", blankFields)}.";
        }
      }
      return null;
    }

    static private TagInfo ToTagInfo(PlcTag tag, Language language) {
      return new TagInfo {
        Name = tag.Name,
        DataTypeName = tag.DataTypeName,
        LogicalAddress = tag.LogicalAddress,
        Comment = GetText(tag.Comment, language)
      };
    }

    static private ConstantInfo ToConstantInfo(PlcUserConstant constant, Language language) {
      return new ConstantInfo {
        Name = constant.Name,
        DataTypeName = constant.DataTypeName,
        Value = constant.Value,
        Comment = GetText(constant.Comment, language)
      };
    }

    /// <summary>
    /// Vergleicht logische Adressen ohne "%", Leerzeichen und Groß-/Kleinschreibung ("M10.0" = "%M10.0").
    /// </summary>
    static private bool AddressEquals(string a, string b) {
      string Normalize(string s) => new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).TrimStart('%');
      return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    static private Change Delete(string kind, string name, Action delete) {
      var change = new Change { Kind = kind, Name = name, Action = "Deleted" };
      try {
        delete();
      } catch (Exception ex) {
        change.Error = ex.Message;
      }
      return change;
    }

    static private Change CreateTag(PlcTagTable table, TagInfo info, Language language) {
      var change = new Change {
        Kind = KindTag,
        Name = info.Name,
        Action = "Created",
        Details = $"DataTypeName: {info.DataTypeName}; LogicalAddress: {info.LogicalAddress}"
      };
      try {
        PlcTag tag = table.Tags.Create(info.Name, info.DataTypeName, info.LogicalAddress);
        if (!string.IsNullOrEmpty(info.Comment)) {
          SetText(tag.Comment, language, info.Comment);
          change.Details += $"; Comment: {info.Comment}";
        }
      } catch (Exception ex) {
        change.Error = ex.Message;
      }
      return change;
    }

    static private Change CreateConstant(PlcTagTable table, ConstantInfo info, Language language) {
      var change = new Change {
        Kind = KindUserConstant,
        Name = info.Name,
        Action = "Created",
        Details = $"DataTypeName: {info.DataTypeName}; Value: {info.Value}"
      };
      try {
        PlcUserConstant constant = table.UserConstants.Create(info.Name, info.DataTypeName, info.Value);
        if (!string.IsNullOrEmpty(info.Comment)) {
          SetText(constant.Comment, language, info.Comment);
          change.Details += $"; Comment: {info.Comment}";
        }
      } catch (Exception ex) {
        change.Error = ex.Message;
      }
      return change;
    }

    /// <summary>
    /// Übernimmt die gesetzten Felder (nicht null) in eine bestehende Variable.
    /// </summary>
    static private Change UpdateTag(PlcTag tag, TagInfo info, Language language) {
      var change = new Change { Kind = KindTag, Name = tag.Name };
      var details = new List<string>();
      try {
        string oldType = tag.DataTypeName;
        string oldAddress = tag.LogicalAddress;
        bool typeChanged = info.DataTypeName != null && !string.Equals(oldType, info.DataTypeName.Trim(), StringComparison.OrdinalIgnoreCase);
        bool addressChanged = info.LogicalAddress != null && !AddressEquals(oldAddress, info.LogicalAddress);

        // Datentyp und Adresse hängen voneinander ab (z. B. Bool %M10.0 -> Int %MW10);
        // scheitert die eine Reihenfolge, wird die andere versucht.
        SetDependent(typeChanged, () => tag.DataTypeName = info.DataTypeName.Trim(),
                     addressChanged, () => tag.LogicalAddress = info.LogicalAddress.Trim());
        if (typeChanged) {
          details.Add($"DataTypeName: {oldType} -> {tag.DataTypeName}");
        }
        if (addressChanged) {
          details.Add($"LogicalAddress: {oldAddress} -> {tag.LogicalAddress}");
        }
        UpdateComment(tag.Comment, info.Comment, language, details);
      } catch (Exception ex) {
        change.Error = ex.Message;
      }
      change.Action = details.Count > 0 || change.Error != null ? "Updated" : "Unchanged";
      change.Details = details.Count > 0 ? string.Join("; ", details) : null;
      return change;
    }

    /// <summary>
    /// Übernimmt die gesetzten Felder (nicht null) in eine bestehende Anwenderkonstante.
    /// </summary>
    static private Change UpdateConstant(PlcUserConstant constant, ConstantInfo info, Language language) {
      var change = new Change { Kind = KindUserConstant, Name = constant.Name };
      var details = new List<string>();
      try {
        string oldType = constant.DataTypeName;
        string oldValue = constant.Value;
        bool typeChanged = info.DataTypeName != null && !string.Equals(oldType, info.DataTypeName.Trim(), StringComparison.OrdinalIgnoreCase);
        bool valueChanged = info.Value != null && !string.Equals(oldValue, info.Value.Trim(), StringComparison.Ordinal);

        SetDependent(typeChanged, () => constant.DataTypeName = info.DataTypeName.Trim(),
                     valueChanged, () => constant.Value = info.Value.Trim());
        if (typeChanged) {
          details.Add($"DataTypeName: {oldType} -> {constant.DataTypeName}");
        }
        if (valueChanged) {
          details.Add($"Value: {oldValue} -> {constant.Value}");
        }
        UpdateComment(constant.Comment, info.Comment, language, details);
      } catch (Exception ex) {
        change.Error = ex.Message;
      }
      change.Action = details.Count > 0 || change.Error != null ? "Updated" : "Unchanged";
      change.Details = details.Count > 0 ? string.Join("; ", details) : null;
      return change;
    }

    /// <summary>
    /// Setzt zwei voneinander abhängige Eigenschaften; scheitert die Reihenfolge erst/zweit, wird zweit/erst versucht.
    /// </summary>
    static private void SetDependent(bool setFirst, Action first, bool setSecond, Action second) {
      if (!setFirst || !setSecond) {
        if (setFirst) {
          first();
        }
        if (setSecond) {
          second();
        }
        return;
      }
      try {
        first();
        second();
      } catch {
        second();
        first();
      }
    }

    static private void UpdateComment(MultilingualText comment, string newComment, Language language, List<string> details) {
      if (newComment == null) {
        return;
      }
      string oldComment = GetText(comment, language) ?? "";
      if (string.Equals(oldComment, newComment, StringComparison.Ordinal)) {
        return;
      }
      SetText(comment, language, newComment);
      details.Add($"Comment: \"{oldComment}\" -> \"{newComment}\"");
    }

    #endregion
  }
}
