using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.PLC.Device;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC {
  /// <summary>
  /// Zugriff auf PLC-Geräte (Steuerungen) im Projekt.
  /// </summary>
  static internal class cTiaPlcDevices {
    /// <summary>
    /// Listet alle Geräte mit PLC-Software inkl. Geräteordnern und Unterelementen (z. B. CPU im Rack).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="kind">Optional: Project oder LocalSession zur Disambiguierung</query>
    /// <returns>JSON-String mit einer Liste von DeviceInfo oder Fehlermeldung</returns>
    [RestRoute(PlcRoutes.DevicesList)]
    static public string DevicesList(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      try {
        var list = TiaConnectionManager.Instance.ExecuteWithProject(context, processIdStr, projectName, project => {
          var result = new List<DeviceInfo>();
          foreach (PlcTarget target in FindAll(project)) {
            PlcSoftware plc = target.Software;
            result.Add(new DeviceInfo {
              DeviceName = target.Device.Name,
              DeviceItemName = target.DeviceItem.Name,
              PlcName = plc.Name,
              TypeIdentifier = Safe(() => target.DeviceItem.TypeIdentifier)
                ?? Safe(() => target.Device.TypeIdentifier)
                ?? target.Device.DeviceItems.Select(i => Safe(() => i.TypeIdentifier)).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
              BlockCount = CountBlocks(plc.BlockGroup),
              UdtCount = CountTypes(plc.TypeGroup),
              TagTableCount = CountTagTables(plc.TagTableGroup)
            });
          }
          return result;
        });
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(list);
      } catch (TiaProjectNotFoundException ex) {
        return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: {ex.Message}");
      } catch (EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        return ErrorResponse(context, HttpStatusCode.ServiceUnavailable, "Error: TIA session unavailable. Please re-open TIA Portal.");
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error accessing TIA project: {ex.Message}");
      }
    }

    #region Hilfsmethoden

    sealed class PlcTarget {
      public Device Device;
      public DeviceItem DeviceItem;
      public PlcSoftware Software;
    }

    static T Safe<T>(Func<T> getter) {
      try {
        return getter();
      } catch {
        return default(T);
      }
    }

    static int CountBlocks(PlcBlockGroup group) {
      int count = group.Blocks.Count;
      foreach (PlcBlockGroup child in group.Groups) {
        count += CountBlocks(child);
      }
      return count;
    }

    static int CountTypes(PlcTypeGroup group) {
      int count = group.Types.Count;
      foreach (PlcTypeGroup child in group.Groups) {
        count += CountTypes(child);
      }
      return count;
    }

    static int CountTagTables(PlcTagTableGroup group) {
      int count = group.TagTables.Count;
      foreach (PlcTagTableGroup child in group.Groups) {
        count += CountTagTables(child);
      }
      return count;
    }

    /// <summary>
    /// Alle Geräte mit PLC-Software, auch in Geräteordnern und als Unterelement (z. B. CPU im Rack).
    /// </summary>
    static List<PlcTarget> FindAll(ProjectBase project) {
      var targets = new List<PlcTarget>();
      foreach (Device device in AllDevices(project)) {
        foreach (DeviceItem item in device.DeviceItems) {
          Collect(device, item, targets);
        }
      }
      return targets;
    }

    static void Collect(Device device, DeviceItem item, List<PlcTarget> targets) {
      SoftwareContainer container = item.GetService<SoftwareContainer>();
      if (container?.Software is PlcSoftware plc) {
        targets.Add(new PlcTarget { Device = device, DeviceItem = item, Software = plc });
        return;
      }
      foreach (DeviceItem child in item.DeviceItems) {
        Collect(device, child, targets);
      }
    }

    static IEnumerable<Device> AllDevices(ProjectBase project) {
      foreach (Device device in project.Devices) {
        yield return device;
      }
      var groups = new Stack<DeviceUserGroup>(project.DeviceGroups);
      while (groups.Count > 0) {
        DeviceUserGroup group = groups.Pop();
        foreach (Device device in group.Devices) {
          yield return device;
        }
        foreach (DeviceUserGroup child in group.Groups) {
          groups.Push(child);
        }
      }
    }

    #endregion
  }
}
