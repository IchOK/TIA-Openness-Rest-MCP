using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.Block;
using static Tophinke.TiaOpenness.Tool.TiaREST.cTiaRequestHelpers;

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
    /// <query name="filterTypes">Optionaler kommagetrennter Filter für die Bausteintypen (z. B. "FC,GlobalDB")</query>
    /// <query name="filterLanguages">Optionaler kommagetrennter Filter für die Programmiersprachen (z. B. "SCL,LAD")</query>
    /// <query name="path">Optionaler Ordnerfilter, ein Parameter je Ordnername (path=A&amp;path=B); liefert die Bausteine dieses Ordners inkl. Unterordnern</query>
    /// <returns>JSON-String mit der Liste der PLC-Bausteine je PLC oder Fehlermeldung</returns>
    static public string GetAll(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string[] filterTypes = ReadListFilter(context, "filterTypes");
      string[] filterLanguages = ReadListFilter(context, "filterLanguages");
      string[] filterPath = ReadPathFilter(context);

      if (filterLanguages != null) {
        string[] knownLanguages = Enum.GetNames(typeof(ProgrammingLanguage));
        var unknown = filterLanguages.Where(l => !knownLanguages.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0) {
          return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Unknown programming language(s): {string.Join(", ", unknown)}. Allowed: {string.Join(", ", knownLanguages)}.");
        }
      }

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var list = new List<PlcList>();
          foreach (Device device in project.Devices) {
            foreach (DeviceItem deviceItem in device.DeviceItems) {
              SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
              if (container != null && container.Software is PlcSoftware plcSoftware) {
                PlcBlockGroup group = ResolveGroup<PlcBlockGroup>(plcSoftware.BlockGroup, filterPath, g => g.Groups, g => g.Name, out string[] groupPath);
                if (group == null) {
                  continue;
                }
                list.Add(new PlcList {
                  DeviceName = device.Name,
                  DeviceItemName = deviceItem.Name,
                  PlcName = plcSoftware.Name,
                  Blocks = GetAll(group, filterTypes, filterLanguages, groupPath)
                });
              }
            }
          }
          return list;
        });

        if (filterPath != null && retValue.Count == 0) {
          return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Block folder '{string.Join("/", filterPath)}' not found in any PLC.");
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
            Path = GetBlockPath(block),
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
    /// Legt einen PLC-Baustein neu an oder überschreibt einen bestehenden Baustein (HTTP PUT).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="blockName">Name des PLC-Bausteins</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PutRequest mit Content (SD oder XML), optional Format, MultiLingualText und Path</body>
    /// <returns>JSON-String mit PutResult oder Fehlermeldung</returns>
    static public string Put(HttpListenerContext context) {
      string methodError = RequireMethod(context, "PUT", BlockRoutes.Put);
      if (methodError != null) {
        return methodError;
      }

      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string blockName = context.Request.QueryString["blockName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      if (string.IsNullOrWhiteSpace(blockName)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: blockName must be provided.");
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
          var errorMessage = cTiaProject.GetPlcSystemBlockGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcBlockGroup plcBlockGroup);
          if (errorMessage != null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, errorMessage);
          }

          PlcBlock existing = cTiaFindHelpers.FindBlock(plcBlockGroup, blockName);
          string[] existingPath = existing != null ? GetBlockPath(existing) : null;
          string[] targetPath = request.Path ?? existingPath ?? new string[0];

          if (existing != null && existing.IsKnowHowProtected) {
            return ErrorResponse(context, HttpStatusCode.Conflict, $"Error: Block {blockName} is know-how-protected and cannot be overwritten.");
          }

          // Der Import (v. a. aus SD-Dokumenten ohne Nummer) vergibt die Bausteinnummer neu;
          // die bisherige Nummer wird deshalb vorher gemerkt und danach wiederhergestellt.
          int? targetNumber = request.BlockNumber ?? existing?.Number;
          bool? existingAutoNumber = existing?.AutoNumber;

          PlcBlockGroup targetGroup = EnsureBlockGroup(plcBlockGroup, targetPath);

          // Liegt der Baustein in einem anderen Ordner als dem Ziel, wird er gesichert und gelöscht,
          // damit der Import ihn im Zielordner neu anlegt; bei einem Importfehler wird die Sicherung zurückgespielt.
          string backupXml = null;
          if (existing != null && !PathEquals(existingPath, targetPath)) {
            errorMessage = ExportAsXml(existing, "_backup_" + blockName, out backupXml);
            if (errorMessage != null) {
              return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error: Could not back up block {blockName} before moving it: {errorMessage}");
            }
            existing.Delete();
          }

          errorMessage = Import(targetGroup, blockName, format, request.Content, request.MultiLingualText, out PlcBlock imported, out string[] messages);
          if (errorMessage != null) {
            if (backupXml != null) {
              string restoreError = Import(EnsureBlockGroup(plcBlockGroup, existingPath), "_backup_" + blockName, FormatXml, backupXml, null, out _, out _);
              errorMessage += restoreError == null
                ? " The original block was restored."
                : $" Restoring the original block failed: {restoreError}";
            }
            return ErrorResponse(context, HttpStatusCode.InternalServerError, errorMessage);
          }
          if (imported == null) {
            return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error: Import finished, but block {blockName} was not found afterwards.");
          }

          if (!imported.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase)) {
            messages = messages.Concat(new[] { $"Imported block is named '{imported.Name}', not '{blockName}'." }).ToArray();
          }

          if (targetNumber.HasValue && imported.Number != targetNumber.Value) {
            string numberError = SetBlockNumber(imported, targetNumber.Value, existingAutoNumber ?? false);
            if (numberError != null) {
              messages = messages.Concat(new[] { $"Block number could not be set to {targetNumber.Value} (now {imported.Number}): {numberError}" }).ToArray();
            }
          }

          var result = new PutResult {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            BlockName = imported.Name,
            BlockNumber = imported.Number,
            BlockType = imported.GetType().Name,
            ProgrammingLanguage = imported.ProgrammingLanguage.ToString(),
            Path = GetBlockPath(imported),
            Created = existing == null,
            Format = format,
            Messages = messages
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

    /// <summary>
    /// Passt die Startwerte von Variablen eines Datenbausteins an (HTTP PATCH).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="blockName">Name des Datenbausteins</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PatchRequest mit der Liste der neuen Startwerte</body>
    /// <returns>JSON-String mit PatchResult oder Fehlermeldung</returns>
    static public string Patch(HttpListenerContext context) {
      string methodError = RequireMethod(context, "PATCH", BlockRoutes.Patch);
      if (methodError != null) {
        return methodError;
      }

      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string blockName = context.Request.QueryString["blockName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      if (string.IsNullOrWhiteSpace(blockName)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: blockName must be provided.");
      }

      PatchRequest request;
      try {
        request = ReadBody<PatchRequest>(context);
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid request body: {ex.Message}");
      }
      if (request == null || request.StartValues == null || request.StartValues.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Request body must contain at least one entry in StartValues.");
      }
      if (request.StartValues.Any(sv => sv == null || string.IsNullOrWhiteSpace(sv.Member) || sv.Value == null)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Every StartValues entry needs Member and Value.");
      }

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcSystemBlockGroup(project, deviceName, deviceItemName, out PlcSoftware plcSoftware, out PlcBlockGroup plcBlockGroup);
          if (errorMessage != null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, errorMessage);
          }

          PlcBlock block = cTiaFindHelpers.FindBlock(plcBlockGroup, blockName);
          if (block == null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Block with name {blockName} not found in PLC software {plcSoftware.Name}.");
          }
          if (!(block is DataBlock dataBlock)) {
            return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Block {blockName} is a {block.GetType().Name}; start values can only be changed in data blocks.");
          }
          if (block.IsKnowHowProtected) {
            return ErrorResponse(context, HttpStatusCode.Conflict, $"Error: Block {blockName} is know-how-protected.");
          }

          MemberComposition members = dataBlock.Interface?.Members;
          var resolved = new List<KeyValuePair<StartValueUpdate, Member>>();
          var missing = new List<string>();
          foreach (StartValueUpdate update in request.StartValues) {
            Member member = FindMember(members, update.Member, out string notFoundReason);
            if (member == null) {
              missing.Add($"{update.Member} ({notFoundReason})");
            } else {
              resolved.Add(new KeyValuePair<StartValueUpdate, Member>(update, member));
            }
          }
          if (missing.Count > 0) {
            return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Members not found in block {blockName}: {string.Join(", ", missing)}. No start values were changed.");
          }

          var changes = new List<StartValueChange>();
          foreach (var pair in resolved) {
            var change = new StartValueChange {
              Member = pair.Key.Member,
              NewValue = pair.Key.Value
            };
            try {
              change.OldValue = pair.Value.GetAttribute("StartValue")?.ToString();
              pair.Value.SetAttribute("StartValue", pair.Key.Value);
            } catch (Exception ex) {
              change.Error = ex.Message;
            }
            changes.Add(change);
          }

          var result = new PatchResult {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            BlockName = block.Name,
            BlockNumber = block.Number,
            BlockType = block.GetType().Name,
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
    static private List<Info> GetAll(PlcBlockGroup group, string[] filterTypes, string[] filterLanguages, string[] path) {
      var blocks = new List<Info>();
      foreach (var block in group.Blocks) {
        string blockType = block.GetType().Name;
        if (filterTypes != null && !filterTypes.Contains(blockType, StringComparer.OrdinalIgnoreCase)) {
          continue;
        }
        string language = block.ProgrammingLanguage.ToString();
        if (filterLanguages != null && !filterLanguages.Contains(language, StringComparer.OrdinalIgnoreCase)) {
          continue;
        }
        blocks.Add(new Info {
          BlockName = block.Name,
          BlockNumber = block.Number,
          BlockType = blockType,
          ProgrammingLanguage = language,
          Path = path
        });
      }
      foreach (var userGroup in group.Groups) {
        string[] newPath = path.Concat(new string[] { userGroup.Name }).ToArray();
        blocks.AddRange(GetAll(userGroup, filterTypes, filterLanguages, newPath));
      }
      return blocks;
    }

    /// <summary>
    /// Ermittelt den Ordnerpfad eines PLC-Bausteins innerhalb der Bausteinordner.
    /// </summary>
    /// <param name="block">PLC-Baustein</param>
    /// <returns>Namen der Benutzerordner vom Wurzelordner bis zum Baustein; leer, wenn der Baustein im Wurzelordner liegt</returns>
    static private string[] GetBlockPath(PlcBlock block) {
      var path = new List<string>();
      IEngineeringObject current = block.Parent;
      while (current != null && !(current is PlcBlockSystemGroup)) {
        if (current is PlcBlockUserGroup userGroup) {
          path.Insert(0, userGroup.Name);
        }
        current = current.Parent;
      }
      return path.ToArray();
    }

    /// <summary>
    /// Setzt die Nummer eines PLC-Bausteins. Dafür wird die automatische Nummernvergabe kurz abgeschaltet.
    /// </summary>
    /// <param name="block">PLC-Baustein</param>
    /// <param name="number">Gewünschte Bausteinnummer</param>
    /// <param name="autoNumber">Gewünschter Zustand der automatischen Nummernvergabe danach</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static private string SetBlockNumber(PlcBlock block, int number, bool autoNumber) {
      try {
        block.AutoNumber = false;
        block.Number = number;
        if (autoNumber) {
          block.AutoNumber = true;
        }
        return null;
      } catch (Exception ex) {
        return ex.Message;
      }
    }

    /// <summary>
    /// Liefert den Bausteinordner zum angegebenen Pfad und legt fehlende Ordner an.
    /// </summary>
    /// <param name="root">Wurzelordner der PLC-Bausteine</param>
    /// <param name="path">Namen der Benutzerordner vom Wurzelordner aus</param>
    /// <returns>Zielordner</returns>
    static private PlcBlockGroup EnsureBlockGroup(PlcBlockGroup root, string[] path) {
      PlcBlockGroup group = root;
      foreach (string name in path) {
        group = (PlcBlockGroup)group.Groups.Find(name) ?? group.Groups.Create(name);
      }
      return group;
    }

    /// <summary>
    /// Importiert einen PLC-Baustein als SD-Dokument oder XML in einen Bausteinordner.
    /// Bestehende Bausteine gleichen Namens im Ordner werden überschrieben.
    /// </summary>
    /// <param name="group">Zielordner</param>
    /// <param name="fileName">Basisname der temporären Importdatei</param>
    /// <param name="format">FormatSd oder FormatXml</param>
    /// <param name="content">Inhalt des .s7dcl- bzw. XML-Dokuments</param>
    /// <param name="multiLingualText">Optionaler Inhalt der .s7res-Datei (nur SD)</param>
    /// <param name="block">Out-Parameter für den importierten Baustein</param>
    /// <param name="messages">Out-Parameter für die Meldungen des Imports</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static private string Import(PlcBlockGroup group, string fileName, string format, string content, string multiLingualText, out PlcBlock block, out string[] messages) {
      block = null;
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
          DocumentImportResultForBlocks result = group.Blocks.ImportFromDocuments(importDir, safeName, ImportDocumentOptions.Override);
          if (result == null || result.State == DocumentResultState.Failure) {
            return "ImportFromDocuments failed: " + cTiaExportHelpers.FormatImportMessages(result);
          }
          messages = cTiaExportHelpers.GetMessages(result.Messages);
          block = result.ImportedPlcBlocks?.FirstOrDefault();
        } else {
          FileInfo xmlFile = new FileInfo(Path.Combine(importDir.FullName, safeName + ".xml"));
          File.WriteAllText(xmlFile.FullName, content, encoding);
          IList<PlcBlock> imported = group.Blocks.Import(xmlFile, ImportOptions.Override);
          block = imported?.FirstOrDefault();
        }
        return null;
      } catch (Exception ex) {
        return "Import failed: " + ex.Message;
      } finally {
        cTiaExportHelpers.TryDeleteExportedDocument(importDir.FullName, safeName);
      }
    }

    /// <summary>
    /// Sucht eine Variable im Interface eines Datenbausteins anhand ihres Pfads.
    /// Unterstützt Strukturen ("Motor.Scale.Min"), Arrays ("Motor.Diag[2]", mehrdimensional "Matrix[1,2]")
    /// und beliebige Verschachtelungen ("Motor.Params[1].Scale.Mins[1].Value").
    /// Openness führt alle Variablen flach in der obersten Member-Ebene; Array-Elemente stehen dort
    /// unter ihrem Array mit wiederholtem Namen ("Motor.Diag.Diag[2]").
    /// </summary>
    /// <param name="members">Wurzel-Member des Datenbausteins</param>
    /// <param name="memberPath">Pfad der Variable</param>
    /// <param name="notFoundReason">Out-Parameter mit der Stelle, an der die Suche gescheitert ist, und den dort vorhandenen Namen</param>
    /// <returns>Gefundene Variable oder null</returns>
    static private Member FindMember(MemberComposition members, string memberPath, out string notFoundReason) {
      notFoundReason = null;
      if (members == null) {
        notFoundReason = "data block has no interface members";
        return null;
      }
      if (string.IsNullOrWhiteSpace(memberPath)) {
        notFoundReason = "empty path";
        return null;
      }
      string path = memberPath.Replace("\"", "").Trim();

      List<string> segments = SplitMemberPath(path);
      if (segments == null) {
        notFoundReason = "invalid path syntax";
        return null;
      }

      Member member = FindMemberByName(members, ToOpennessMemberPath(segments)) ?? TryFindMember(members, path);
      if (member == null) {
        notFoundReason = DescribeMissingMember(members, segments);
      }
      return member;
    }

    /// <summary>
    /// Wandelt Pfadsegmente in die Openness-Schreibweise um ("Params[1].Value" → "Params.Params[1].Value").
    /// </summary>
    static private string ToOpennessMemberPath(IEnumerable<string> segments) {
      var parts = new List<string>();
      foreach (string segment in segments) {
        int bracket = segment.IndexOf('[');
        if (bracket > 0) {
          parts.Add(segment.Substring(0, bracket));
        }
        parts.Add(segment);
      }
      return string.Join(".", parts);
    }

    /// <summary>
    /// Beschreibt, an welchem Pfadsegment die Suche gescheitert ist, inklusive der dort vorhandenen Namen.
    /// </summary>
    static private string DescribeMissingMember(MemberComposition members, List<string> segments) {
      var names = new List<string>();
      try {
        foreach (Member member in members) {
          names.Add(member.Name);
        }
      } catch {
      }
      var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

      int found = 0;
      for (int i = segments.Count - 1; i > 0; i--) {
        if (nameSet.Contains(ToOpennessMemberPath(segments.Take(i)))) {
          found = i;
          break;
        }
      }

      string missing = segments[found];
      string prefix = found == 0 ? "" : ToOpennessMemberPath(segments.Take(found)) + ".";
      string location = found == 0 ? "data block root" : $"'{string.Join(".", segments.Take(found))}'";

      int bracket = missing.IndexOf('[');
      if (bracket > 0 && nameSet.Contains(prefix + missing.Substring(0, bracket))) {
        string arrayName = missing.Substring(0, bracket);
        prefix += arrayName + ".";
        location = $"array '{string.Join(".", segments.Take(found).Concat(new[] { arrayName }))}'";
      }

      var children = names
        .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.IndexOf('.', prefix.Length) < 0)
        .Select(n => n.Substring(prefix.Length))
        .ToList();
      return $"'{missing}' not found in {location}; available: {FormatNameList(children)}";
    }

    /// <summary>
    /// Formatiert eine Namensliste (gekürzt) für Fehlermeldungen.
    /// </summary>
    static private string FormatNameList(List<string> names, int maxCount = 15) {
      if (names.Count == 0) {
        return "(none)";
      }
      string list = string.Join(", ", names.Take(maxCount));
      return names.Count > maxCount ? $"{list}, … ({names.Count} total)" : list;
    }

    /// <summary>
    /// Zerlegt einen Variablenpfad an den Punkten außerhalb von Array-Indizes
    /// und normalisiert die Indizes ("Mins[1][2]" bzw. "Mins[ 1, 2 ]" → "Mins[1,2]").
    /// </summary>
    /// <returns>Liste der Pfadsegmente oder null bei ungültigem Pfad</returns>
    static private List<string> SplitMemberPath(string path) {
      var segments = new List<string>();
      var name = new StringBuilder();
      var indices = new List<string>();
      var index = new StringBuilder();
      bool inIndex = false;

      foreach (char c in path) {
        if (inIndex) {
          if (c == ']') {
            if (index.Length == 0) {
              return null;
            }
            indices.Add(index.ToString());
            index.Clear();
            inIndex = false;
          } else if (c == '[') {
            return null;
          } else if (!char.IsWhiteSpace(c)) {
            index.Append(c);
          }
        } else if (c == '[') {
          if (name.Length == 0) {
            return null;
          }
          inIndex = true;
        } else if (c == '.') {
          if (name.Length == 0) {
            return null;
          }
          segments.Add(BuildSegment(name.ToString().Trim(), indices));
          name.Clear();
          indices.Clear();
        } else if (c == ']') {
          return null;
        } else {
          if (indices.Count > 0) {
            return null;
          }
          name.Append(c);
        }
      }
      if (inIndex || name.Length == 0) {
        return null;
      }
      segments.Add(BuildSegment(name.ToString().Trim(), indices));
      return segments;
    }

    static private string BuildSegment(string name, List<string> indices) {
      return indices.Count == 0 ? name : name + "[" + string.Join(",", indices) + "]";
    }

    /// <summary>
    /// Sucht einen Member über Find und danach ohne Berücksichtigung der Groß-/Kleinschreibung.
    /// </summary>
    static private Member FindMemberByName(MemberComposition members, string name) {
      Member member = TryFindMember(members, name);
      if (member != null) {
        return member;
      }
      try {
        foreach (Member candidate in members) {
          if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) {
            return candidate;
          }
        }
      } catch {
      }
      return null;
    }

    static private Member TryFindMember(MemberComposition members, string name) {
      try {
        return members.Find(name);
      } catch {
        return null;
      }
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
        format = FormatSd;
        return null;
      }

      string xmlError = ExportAsXml(block, blockName, out content);
      if (xmlError != null) {
        return xmlError;
      }
      format = FormatXml;
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
