using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper;
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
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="path">Optionaler Ordnerfilter, ein Parameter je Ordnername (path=A&amp;path=B); liefert die Tagtabellen dieses Ordners inkl. Unterordnern</query>
    /// <returns>JSON-String mit der Liste der Tagtabellen je PLC oder Fehlermeldung</returns>
    static public string List(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string[] filterPath = ReadPathFilter(context);

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
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
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="tagTableName">Name der Tagtabelle</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <returns>JSON-String mit der Tagtabelle oder Fehlermeldung</returns>
    static public string Get(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string tagTableName = context.Request.QueryString["tagTableName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
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
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="tagTableName">Name der Tagtabelle</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PutRequest mit Tags, UserConstants und optional Path</body>
    /// <returns>JSON-String mit PutResult oder Fehlermeldung</returns>
    static public string Put(HttpListenerContext context) {
      string methodError = RequireMethod(context, "PUT", TagTableRoutes.Put);
      if (methodError != null) {
        return methodError;
      }

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
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
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
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="tagTableName">Name der Tagtabelle</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <body>PatchRequest mit den zu ändernden Tags und UserConstants</body>
    /// <returns>JSON-String mit PatchResult oder Fehlermeldung</returns>
    static public string Patch(HttpListenerContext context) {
      string methodError = RequireMethod(context, "PATCH", TagTableRoutes.Patch);
      if (methodError != null) {
        return methodError;
      }

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
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
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

    #region Hilfsmethoden für TIA Openness Objektstruktur

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
          Path = path
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
