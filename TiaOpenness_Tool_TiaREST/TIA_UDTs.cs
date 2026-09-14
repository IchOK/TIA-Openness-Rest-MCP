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
using Tophinke.TiaOpenness.Tool.Types.UDT;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  static internal class cTiaUDTs {
    static public string List(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];

      try {
        // TIA-Zugriff via Connection Manager, der Attach-Lifetime handhabt
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var list = new List<Info>();
          // Suche in allen Geräten nach einer PLC (Steuerung)
          foreach (Device device in project.Devices) {
            foreach (DeviceItem deviceItem in device.DeviceItems) {
              // Prüfen ob das Gerät Software (also Bausteine) enthält
              SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
              if (container != null && container.Software is PlcSoftware plcSoftware) {
                List(plcSoftware.TypeGroup, list, device.Name, deviceItem.Name, plcSoftware.Name);
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
          PlcType type = Find(plcTypeGroup, typeName);
          if (type == null) {
            errorMessage = $"Error: Type with name {typeName} not found in PLC software {plcSoftware.Name}.";
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }
          // UDT Exportieren und Daten zurückgeben (Files werden im Unterverzeichnis export der App gespeichert)

          string tempFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export");
          FileInfo tempFileInfo = new FileInfo(Path.Combine(tempFilePath, $"{typeName}.s7dcl"));
          try {
            if (!Directory.Exists(tempFilePath)) {
              Directory.CreateDirectory(tempFilePath);
            }
            if (tempFileInfo.Exists) {
              File.Delete(tempFileInfo.FullName);
            }
            DocumentExportResult exportResult = type.ExportAsDocuments(tempFileInfo.Directory, typeName);
            if (exportResult == null || exportResult.State != DocumentResultState.Success) {
              errorMessage = $"Error exporting block {typeName}.";
              Console.Error.WriteLine(errorMessage);
              context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
              context.Response.ContentType = "text/plain";
              return errorMessage;
            }
            string fileContent = File.ReadAllText(tempFileInfo.FullName);

            var data = new Data {
              DeviceName = deviceName,
              DeviceItemName = deviceItemName,
              PlcName = plcSoftware.Name,
              UdtName = type.Name,
              Format = "SimaticData/SD",
              Content = fileContent
            };

            context.Response.ContentType = "application/json";
            return JsonConvert.SerializeObject(data);

          } catch (Exception ex) {
            errorMessage = $"Error exporting block {typeName}: {ex.Message}";
            Console.Error.WriteLine(errorMessage);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "text/plain";
            return errorMessage;
          }
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
    /// Sucht rekursiv nach einem PLC-Datentype mit dem angegebenen Namen innerhalb einer PLC--Gruppe.
    /// </summary>
    /// <param name="group">aktuelle Block-Gruppe</param>
    /// <param name="blockName">Name des zu suchenden Datenbausteins</param>
    /// <returns></returns>
    static private PlcType Find(PlcTypeGroup group, string blockName) {
      foreach (var type in group.Types) {
        if (type.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase)) {
          return type;
        }
      }
      foreach (var userGroup in group.Groups) {
        var foundType = Find(userGroup, blockName);
        if (foundType != null) {
          return foundType;
        }
      }
      return null;
    }

    /// <summary>
    /// Durchsucht die Ordnerstruktur eines PLC-Block-Groups rekursiv nach Datenbausteinen (DBs) und fügt die gefundenen Informationen in eine Liste ein.
    /// </summary>
    /// <param name="group">aktuell zu durchsuchender Block-Group</param>
    /// <param name="list">Liste, in die die gefundenen Informationen hinzugefügt werden</param>
    /// <param name="deviceName">Name des Geräts</param>
    /// <param name="deviceItemName">Name des Gerätelements</param>
    /// <param name="plcName">Name des PLCs</param>
    static private void List(PlcTypeGroup group, List<Info> list, string deviceName, string deviceItemName, string plcName) {
      foreach (PlcType type in group.Types) {
        // In Openness sind DBs spezifische Klassen
        list.Add(new Info {
          DeviceName = deviceName,
          DeviceItemName = deviceItemName,
          PlcName = plcName,
          UdtName = type.Name
        });
      }

      // Rekursiv in Unterordnern suchen
      foreach (PlcTypeUserGroup userGroup in group.Groups) {
        List(userGroup, list, deviceName, deviceItemName, plcName);
      }
    }

    #endregion
  }
}
