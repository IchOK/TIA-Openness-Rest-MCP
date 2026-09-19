using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.Net;
using Tophinke.TiaOpenness.Tool.Types.XRef;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  static internal class cTiaXRefs {
    /// <summary>
    /// Gibt die Cross-References für ein Objekt in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <query name="objectName">Name des Objekts</query>
    /// <query name="objectKind">Art des Objekts</query>
    /// <query name="filterStr">Filter für die Cross-References</query>
    /// <returns>JSON-String mit den Cross-References oder Fehlermeldung</returns>
    static public string Get(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];
      string objectName = context.Request.QueryString["objectName"];
      string objectKind = context.Request.QueryString["objectKind"];
      string filterStr = context.Request.QueryString["filter"];

      if (string.IsNullOrEmpty(objectName) || string.IsNullOrEmpty(objectKind)) {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "text/plain";
        return "Error: objectName and objectKind must be provided.";
      }

      CrossReferenceFilter filter = CrossReferenceFilter.AllObjects;
      if (!string.IsNullOrEmpty(filterStr)) {
        if (!Enum.TryParse(filterStr, true, out filter)) {
          context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
          context.Response.ContentType = "text/plain";
          return $"Error: Invalid filter '{filterStr}'. Valid values: AllObjects, ObjectsWithReferences, ObjectsWithoutReferences, UnusedObjects.";
        }
      }

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          string errorMessage = cTiaProject.GetPlcSoftware(project, deviceName, deviceItemName, out PlcSoftware plcSoftware);
          if (errorMessage != null) {
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          IEngineeringServiceProvider serviceProvider = ResolveObject(plcSoftware, objectKind, objectName, out errorMessage);
          if (serviceProvider == null) {
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          CrossReferenceService xrefService = serviceProvider.GetService<CrossReferenceService>();
          if (xrefService == null) {
            errorMessage = $"Error: Cross-reference service is not available for {objectKind} '{objectName}'.";
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }

          CrossReferenceResult result = xrefService.GetCrossReferences(filter);
          var sources = new List<SourceInfo>();
          if (result != null && result.Sources != null) {
            foreach (SourceObject source in result.Sources) {
              sources.Add(MapSource(source));
            }
          }

          var data = new Data {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            ObjectName = objectName,
            ObjectKind = objectKind,
            Filter = filter.ToString(),
            Sources = sources
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
    /// Ermittelt das Objekt anhand des Objekt-Typs und -Namens.
    /// </summary>
    /// <param name="plcSoftware">PLC-Software</param>
    /// <param name="objectKind">Art des Objekts</param>
    /// <param name="objectName">Name des Objekts</param>
    /// <param name="errorMessage">Fehlermeldung, falls das Objekt nicht gefunden wurde</param>
    /// <returns>Gefundenes Objekt oder null</returns>
    static private IEngineeringServiceProvider ResolveObject(PlcSoftware plcSoftware, string objectKind, string objectName, out string errorMessage) {
      errorMessage = null;
      switch (objectKind.Trim().ToLowerInvariant()) {
        case "block": {
          PlcBlock block = cTiaFindHelpers.FindBlock(plcSoftware.BlockGroup, objectName);
          if (block == null) {
            errorMessage = $"Error: Block with name {objectName} not found in PLC software {plcSoftware.Name}.";
            return null;
          }
          return block;
        }
        case "tag": {
          PlcTag tag = cTiaFindHelpers.FindTag(plcSoftware.TagTableGroup, objectName);
          if (tag == null) {
            errorMessage = $"Error: Tag with name {objectName} not found in PLC software {plcSoftware.Name}.";
            return null;
          }
          return tag;
        }
        case "udt": {
          PlcType type = cTiaFindHelpers.FindType(plcSoftware.TypeGroup, objectName);
          if (type == null) {
            errorMessage = $"Error: UDT with name {objectName} not found in PLC software {plcSoftware.Name}.";
            return null;
          }
          return type;
        }
        case "systemconstant": {
          PlcSystemConstant constant = cTiaFindHelpers.FindSystemConstant(plcSoftware.TagTableGroup, objectName);
          if (constant == null) {
            errorMessage = $"Error: System constant with name {objectName} not found in PLC software {plcSoftware.Name}.";
            return null;
          }
          return constant;
        }
        default:
          errorMessage = $"Error: Unsupported objectKind '{objectKind}'. Valid values: Block, Tag, Udt, SystemConstant.";
          return null;
      }
    }

    /// <summary>
    /// Mappt ein SourceObject auf ein SourceInfo.
    /// </summary>
    /// <param name="source">SourceObject</param>
    /// <returns>Mapped SourceInfo</returns>
    static private SourceInfo MapSource(SourceObject source) {
      var references = new List<ReferenceInfo>();
      if (source.References != null) {
        foreach (ReferenceObject reference in source.References) {
          references.Add(MapReference(reference));
        }
      }

      var children = new List<SourceInfo>();
      if (source.Children != null) {
        foreach (SourceObject child in source.Children) {
          children.Add(MapSource(child));
        }
      }

      return new SourceInfo {
        Name = source.Name,
        Path = source.Path,
        Address = source.Address,
        Device = source.Device,
        TypeName = source.TypeName,
        References = references,
        Children = children
      };
    }

    /// <summary>
    /// Mappt ein ReferenceObject auf ein ReferenceInfo.
    /// </summary>
    /// <param name="reference">ReferenceObject</param>
    /// <returns>Mapped ReferenceInfo</returns>
    static private ReferenceInfo MapReference(ReferenceObject reference) {
      var locations = new List<LocationInfo>();
      if (reference.Locations != null) {
        foreach (Location location in reference.Locations) {
          locations.Add(new LocationInfo {
            Name = location.Name,
            Address = location.Address,
            TypeName = location.TypeName,
            Access = location.Access.ToString(),
            ReferenceType = location.ReferenceType.ToString(),
            ReferenceLocation = location.ReferenceLocation,
            ReferencedAsName = location.ReferencedAsName
          });
        }
      }

      return new ReferenceInfo {
        Name = reference.Name,
        Path = reference.Path,
        Address = reference.Address,
        Device = reference.Device,
        TypeName = reference.TypeName,
        Locations = locations
      };
    }
    #endregion
  }
}
