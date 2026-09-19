using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using Tophinke.TiaOpenness.Tool.Types.Project;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Verwaltet die Verbindung zu einem TIA Portal-Prozess und stellt Methoden für den Zugriff auf Projekte bereit.
  /// </summary>
  internal sealed class TiaConnectionManager : IDisposable {
    private static readonly Lazy<TiaConnectionManager> _instance = new Lazy<TiaConnectionManager>(() => new TiaConnectionManager());
    public static TiaConnectionManager Instance => _instance.Value;

    private readonly object _lock = new object();
    private TiaPortal _tia;
    private int _attachedProcessId = -1;

    private TiaConnectionManager() { }

    /// <summary>
    /// Stellt sicher, dass eine Verbindung zu einem TIA Portal-Prozess hergestellt ist.
    /// </summary>
    /// <param name="processId">Prozess-ID</param>
    /// <exception cref="InvalidOperationException">Wird geworfen, wenn die Verbindung nicht hergestellt werden kann</exception>
    public void EnsureAttached(int processId) {
      lock (_lock) {
        if (_tia != null && _attachedProcessId == processId) {
          // kurze Validierung: Zugriff auf eine harmlose Eigenschaft
          try {
            var _ = _tia.Projects.Count; // wirft bei disposed
            return;
          } catch {
            TryDisposePortal();
          }
        }

        // neues Attach
        var process = TiaPortal.GetProcesses().FirstOrDefault(p => p.Id == processId);
        if (process == null) throw new InvalidOperationException($"TIA Process {processId} nicht gefunden.");

        try {
          _tia = process.Attach(); // NICHT disposen, solange wir es verwenden
          _attachedProcessId = processId;
        } catch (Exception ex) {
          TryDisposePortal();
          throw new InvalidOperationException(
            $"TIA Attach failed for process {processId}: {ex.GetType().Name}: {ex.Message}", ex);
        }
      }
    }

    /// <summary>
    /// Führt eine Aktion mit einem Projekt aus und stellt sicher, dass die Verbindung zum TIA Portal-Prozess hergestellt ist.
    /// </summary>
    /// <typeparam name="T">Rückgabewert der Aktion</typeparam>
    /// <param name="processIdStr">Prozess-ID</param>
    /// <param name="projectName">Name des Projekts</param>
    /// <param name="action">Aktion, die mit dem Projekt ausgeführt wird</param>
    /// <returns>Rückgabewert der Aktion</returns>
    public T ExecuteWithProject<T>(string processIdStr, string projectName, Func<Project, T> action) {
      if (string.IsNullOrEmpty(processIdStr) || string.IsNullOrEmpty(projectName)) {
        throw new InvalidOperationException("Error: Process ID and project name must be provided.");
      }
      int processId = int.Parse(processIdStr);
      var process = TiaPortal.GetProcesses().FirstOrDefault(p => p.Id == processId);
      if (process == null) {
        throw new InvalidOperationException($"Error: TIA Process with ID {processId} not found.");
      }

      EnsureAttached(processId);
      lock (_lock) {
        var project = _tia.Projects.FirstOrDefault(p => p.Name == projectName);
        if (project == null) throw new InvalidOperationException($"Project {projectName} nicht gefunden.");
        return action(project);
      }
    }

    private void TryDisposePortal() {
      try {
        _tia?.Dispose();
      } catch { /* ignore */ }
      _tia = null;
      _attachedProcessId = -1;
    }

    public void Dispose() {
      lock (_lock) {
        TryDisposePortal();
      }
    }
  }

  /// <summary>
  /// Stellt Methoden für den Zugriff auf Projekte bereit.
  /// </summary>
  static internal class cTiaProject {
    /// <summary>
    /// Gibt eine Liste aller offenen TIA Portal Instanzen und deren Projekte zurück.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <returns>JSON-String mit der Liste der offenen TIA Portal Instanzen und deren Projekte oder Fehlermeldung</returns>
    static public string List(HttpListenerContext context) {
      var processes = TiaPortal.GetProcesses();
      var projectInfos = new List<Info>();
      string errorMessage = string.Empty;

      foreach (var process in processes) {
        try {
          using (var tia = process.Attach()) {
            foreach (var proj in tia.Projects) {
              projectInfos.Add(new Info {
                ProcessId = process.Id,
                Name = proj.Name,
                Path = proj.Path.ToString()
              });
            }
          }
        } catch (Exception ex) {
          Console.Error.WriteLine($"TIA Attach failed for process {process.Id}: {ex.GetType().Name}: {ex.Message}");
          errorMessage = $"Error: TIA Attach denied/failed for process {process.Id}: {ex.Message}";
        }
      }

      if (projectInfos.Count == 0) {
        if (string.IsNullOrEmpty(errorMessage)) {
          errorMessage = "Error: No TIA Portal instances with open projects found.";
        }
        Console.Error.WriteLine(errorMessage);
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        context.Response.ContentType = "text/plain";
        return errorMessage;
      } else {
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(projectInfos);
      }
    }



    #region Hilfsmethoden für den Projekt zugriff

    /// <summary>
    /// Gibt das Projekt anhand der Prozess-ID und des Projektnamens zurück.
    /// </summary>
    /// <param name="processIdStr">Prozess-ID</param>
    /// <param name="projectName">Projektname</param>
    /// <param name="project">Das zurückgegebene Projekt</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static public string GetProjectByProcessAndName(string processIdStr, string projectName, out Project project) {
      project = null;
      if (string.IsNullOrEmpty(processIdStr) || string.IsNullOrEmpty(projectName)) {
        return "Error: Process ID and project name must be provided.";
      }
      int processId = int.Parse(processIdStr);
      var process = TiaPortal.GetProcesses().FirstOrDefault(p => p.Id == processId);
      if (process == null) {
        return $"Error: TIA Process with ID {processId} not found.";
      }
      using (var tia = process.Attach()) {
        project = tia.Projects.FirstOrDefault(p => p.Name == projectName);
        if (project == null) {
          return $"Error: Project with name {projectName} not found.";
        } else {
          return null;
        }
      }
    }

    /// <summary>
    /// Gibt die PLC-Software für ein Gerät in einem Projekt zurück.
    /// </summary>
    /// <param name="project">Projekt</param>
    /// <param name="deviceName">Name des Geräts</param>
    /// <param name="deviceItemName">Name des Geräteelements</param>
    /// <param name="plcSoftware">Out-Parameter für die PLC-Software</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static public string GetPlcSoftware(Project project, string deviceName, string deviceItemName, out PlcSoftware plcSoftware) {
      plcSoftware = null;
      if (project == null) {
        return "Error: Project must be provided.";
      }
      if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(deviceItemName)) {
        // Suche in allen Geräten nach einer PLC (Steuerung)
        foreach (Device device in project.Devices) {
          foreach (DeviceItem deviceItem in device.DeviceItems) {
            // Prüfen ob das Gerät Software (also Bausteine) enthält
            SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
            if (container != null && container.Software is PlcSoftware) {
              plcSoftware = (PlcSoftware)container.Software;
              return null;
            }
          }
        }
        return "Error: No PLC software found.";
      } else {
        var device = project.Devices.FirstOrDefault(d => d.Name == deviceName);
        if (device == null) {
          return $"Error: Device with name {deviceName} not found in project {project.Name}.";
        }
        var deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name == deviceItemName);
        if (deviceItem == null) {
          return $"Error: Device item with name {deviceItemName} not found in device {deviceName}.";
        }
        SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
        if (container != null && container.Software is PlcSoftware) {
          plcSoftware = (PlcSoftware)container.Software;
          return null;
        } else {
          return $"Error: No PLC software found in device {deviceName}/item {deviceItemName} within project {project.Name}.";
        }
      }
    }


    static public string GetPlcSystemBlockGroup(Project project, string deviceName, string deviceItemName, out PlcSoftware plcSoftware, out PlcBlockGroup plcBlockGroup) {
      plcBlockGroup = null;
      plcSoftware = null;
      if (project == null) {
        return "Error: Project must be provided.";
      }
      if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(deviceItemName)) {
        // Suche in allen Geräten nach einer PLC (Steuerung)
        foreach (Device device in project.Devices) {
          foreach (DeviceItem deviceItem in device.DeviceItems) {
            // Prüfen ob das Gerät Software (also Bausteine) enthält
            SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
            if (container != null && container.Software is PlcSoftware) {
              plcSoftware = (PlcSoftware)container.Software;
              plcBlockGroup = plcSoftware.BlockGroup;
              return null;
            }
          }
        }
        return "Error: No PLC software found.";
      } else {
        var device = project.Devices.FirstOrDefault(d => d.Name == deviceName);
        if (device == null) {
          return $"Error: Device with name {deviceName} not found in project {project.Name}.";
        }
        var deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name == deviceItemName);
        if (deviceItem == null) {
          return $"Error: Device item with name {deviceItemName} not found in device {deviceName}.";
        }
        SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
        if (container != null && container.Software is PlcSoftware) {
          plcSoftware = (PlcSoftware)container.Software;
          plcBlockGroup = plcSoftware.BlockGroup;
          return null;
        } else {
          return $"Error: No PLC software found in device {deviceName}/item {deviceItemName} within project {project.Name}.";
        }
      }
    }

    static public string GetPlcSystemTypeGroup(Project project, string deviceName, string deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup) {
      plcTypeGroup = null;
      plcSoftware = null;
      if (project == null) {
        return "Error: Project must be provided.";
      }
      if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(deviceItemName)) {
        // Suche in allen Geräten nach einer PLC (Steuerung)
        foreach (Device device in project.Devices) {
          foreach (DeviceItem deviceItem in device.DeviceItems) {
            // Prüfen ob das Gerät Software (also Bausteine) enthält
            SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
            if (container != null && container.Software is PlcSoftware) {
              plcSoftware = (PlcSoftware)container.Software;
              plcTypeGroup = plcSoftware.TypeGroup;
              return null;
            }
          }
        }
        return "Error: No PLC software found.";
      } else {
        var device = project.Devices.FirstOrDefault(d => d.Name == deviceName);
        if (device == null) {
          return $"Error: Device with name {deviceName} not found in project {project.Name}.";
        }
        var deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name == deviceItemName);
        if (deviceItem == null) {
          return $"Error: Device item with name {deviceItemName} not found in device {deviceName}.";
        }
        SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
        if (container != null && container.Software is PlcSoftware) {
          plcSoftware = (PlcSoftware)container.Software;
          plcTypeGroup = plcSoftware.TypeGroup;
          return null;
        } else {
          return $"Error: No PLC software found in device {deviceName}/item {deviceItemName} within project {project.Name}.";
        }
      }
    }

    static public string GetPlcTagTableGroup(Project project, string deviceName, string deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup) {
      plcTagTableGroup = null;
      plcSoftware = null;
      if (project == null) {
        return "Error: Project must be provided.";
      }
      if (string.IsNullOrEmpty(deviceName) || string.IsNullOrEmpty(deviceItemName)) {
        foreach (Device device in project.Devices) {
          foreach (DeviceItem deviceItem in device.DeviceItems) {
            SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
            if (container != null && container.Software is PlcSoftware) {
              plcSoftware = (PlcSoftware)container.Software;
              plcTagTableGroup = plcSoftware.TagTableGroup;
              return null;
            }
          }
        }
        return "Error: No PLC software found.";
      } else {
        var device = project.Devices.FirstOrDefault(d => d.Name == deviceName);
        if (device == null) {
          return $"Error: Device with name {deviceName} not found in project {project.Name}.";
        }
        var deviceItem = device.DeviceItems.FirstOrDefault(di => di.Name == deviceItemName);
        if (deviceItem == null) {
          return $"Error: Device item with name {deviceItemName} not found in device {deviceName}.";
        }
        SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
        if (container != null && container.Software is PlcSoftware) {
          plcSoftware = (PlcSoftware)container.Software;
          plcTagTableGroup = plcSoftware.TagTableGroup;
          return null;
        } else {
          return $"Error: No PLC software found in device {deviceName}/item {deviceItemName} within project {project.Name}.";
        }
      }
    }
    #endregion
  }
}
