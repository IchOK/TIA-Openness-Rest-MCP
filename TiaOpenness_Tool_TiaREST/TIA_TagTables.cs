using Newtonsoft.Json;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using System;
using System.Collections.Generic;
using System.Net;
using Tophinke.TiaOpenness.Tool.Types.TagTable;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  static internal class cTiaTagTables {
    /// <summary>
    /// Gibt die Liste der Tagtabellen in einem TIA-Projekt zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <returns>JSON-String mit der Liste der Tagtabellen oder Fehlermeldung</returns>
    static public string List(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var list = new List<Info>();
          foreach (Device device in project.Devices) {
            foreach (DeviceItem deviceItem in device.DeviceItems) {
              SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
              if (container != null && container.Software is PlcSoftware plcSoftware) {
                List(plcSoftware.TagTableGroup, list, device.Name, deviceItem.Name, plcSoftware.Name);
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

          var tags = new List<TagInfo>();
          foreach (PlcTag tag in tagTable.Tags) {
            tags.Add(new TagInfo {
              Name = tag.Name,
              DataTypeName = tag.DataTypeName,
              LogicalAddress = tag.LogicalAddress
            });
          }

          var userConstants = new List<ConstantInfo>();
          foreach (PlcUserConstant constant in tagTable.UserConstants) {
            userConstants.Add(new ConstantInfo {
              Name = constant.Name,
              DataTypeName = constant.DataTypeName,
              Value = constant.Value
            });
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

    #region Hilfsmethoden für TIA Openness Objektstruktur

    /// <summary>
    /// Durchsucht die Ordnerstruktur einer PLC-Tagtabelle-Gruppe rekursiv nach Tagtabellen und fügt die gefundenen Informationen in eine Liste ein.
    /// </summary>
    /// <param name="group">aktuelle Tagtabelle-Gruppe</param>
    /// <param name="list">Liste, in die die gefundenen Informationen hinzugefügt werden</param>
    /// <param name="deviceName">Name des Geräts</param>
    /// <param name="deviceItemName">Name des Gerätelements</param>
    /// <param name="plcName">Name des PLCs</param>
    static private void List(PlcTagTableGroup group, List<Info> list, string deviceName, string deviceItemName, string plcName) {
      foreach (PlcTagTable table in group.TagTables) {
        list.Add(new Info {
          DeviceName = deviceName,
          DeviceItemName = deviceItemName,
          PlcName = plcName,
          TagTableName = table.Name,
          IsDefault = table.IsDefault
        });
      }
      foreach (PlcTagTableUserGroup userGroup in group.Groups) {
        List(userGroup, list, deviceName, deviceItemName, plcName);
      }
    }

    #endregion

  }
}
