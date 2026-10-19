using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Multiuser;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.Project;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Das angegebene Projekt ist in der TIA Portal-Instanz nicht geöffnet.
  /// </summary>
  internal sealed class TiaProjectNotFoundException : InvalidOperationException {
    public TiaProjectNotFoundException(string message) : base(message) { }
  }

  /// <summary>
  /// Aufgelöstes offenes Projekt: einzelnes <see cref="Project"/> oder Project-Server-<see cref="LocalSession"/> (<see cref="MultiuserProject"/>).
  /// </summary>
  internal sealed class ResolvedProject {
    public ProjectBase Project { get; set; }
    public LocalSession LocalSession { get; set; }
    /// <summary>ProjectRoutes.KindProject | KindLocalSession</summary>
    public string Kind { get; set; }

    public void Save() {
      if (LocalSession != null) {
        LocalSession.Save();
      } else {
        ((Project)Project).Save();
      }
    }

    public void Close() {
      if (LocalSession != null) {
        LocalSession.Close();
      } else {
        ((Project)Project).Close();
      }
    }
  }

  /// <summary>
  /// Verwaltet die Verbindung zu einem TIA Portal-Prozess und stellt Methoden für den Zugriff auf Projekte bereit.
  /// Je Anfrage wird eine eigene Openness-Sitzung geöffnet und danach freigegeben: TIA Portal zählt alle an eine Sitzung
  /// übergebenen Objekte und lehnt ab 500000 Instanzen weitere Zugriffe ab, bis die Sitzung geschlossen wird.
  /// </summary>
  internal sealed class TiaConnectionManager : IDisposable {
    private static readonly Lazy<TiaConnectionManager> _instance = new Lazy<TiaConnectionManager>(() => new TiaConnectionManager());
    public static TiaConnectionManager Instance => _instance.Value;

    private readonly object _lock = new object();
    private TiaPortal _tia;

    private TiaConnectionManager() { }

    /// <summary>
    /// Führt eine Aktion mit einem Projekt bzw. einer Project-Server-LocalSession in einer eigenen Openness-Sitzung aus.
    /// Meldet TIA Portal das Instanzlimit der Sitzung, wird die Aktion einmal mit einer neuen Sitzung wiederholt.
    /// </summary>
    /// <typeparam name="T">Rückgabewert der Aktion</typeparam>
    /// <param name="processIdStr">Prozess-ID</param>
    /// <param name="projectName">Name des Projekts bzw. der LocalSession-Projektdatei</param>
    /// <param name="action">Aktion, die mit dem Projekt ausgeführt wird; Openness-Objekte dürfen sie nicht überleben</param>
    /// <returns>Rückgabewert der Aktion</returns>
    public T ExecuteWithProject<T>(string processIdStr, string projectName, Func<ProjectBase, T> action, string kind = null) {
      return ExecuteWithResolvedProject(processIdStr, projectName, resolved => action(resolved.Project), kind);
    }

    /// <summary>
    /// Wie <see cref="ExecuteWithProject{T}(string,string,Func{ProjectBase,T},string)"/>, liest optional <c>kind</c> aus der Query.
    /// </summary>
    public T ExecuteWithProject<T>(HttpListenerContext context, string processIdStr, string projectName, Func<ProjectBase, T> action) {
      return ExecuteWithProject(processIdStr, projectName, action, context.Request.QueryString["kind"]);
    }

    /// <summary>
    /// Wie ExecuteWithProject, liefert zusätzlich die LocalSession für Speichern/Schließen von Project-Server-Sessions.
    /// </summary>
    /// <param name="kind">Optional: Project / LocalSession zur Disambiguierung</param>
    public T ExecuteWithResolvedProject<T>(string processIdStr, string projectName, Func<ResolvedProject, T> action, string kind = null) {
      if (string.IsNullOrEmpty(processIdStr) || string.IsNullOrEmpty(projectName)) {
        throw new InvalidOperationException("Error: Process ID and project name must be provided.");
      }
      if (!int.TryParse(processIdStr, out int processId)) {
        throw new InvalidOperationException($"Error: Invalid process ID {processIdStr}.");
      }

      return ExecuteWithPortal(processId, tia => {
        ResolvedProject resolved = ResolveOpenProject(tia, projectName, kind);
        if (resolved == null) {
          throw new TiaProjectNotFoundException(
            $"Project {projectName} nicht gefunden (weder unter Projects noch unter LocalSessions"
            + (string.IsNullOrEmpty(kind) ? "" : $", kind={kind}") + ").");
        }
        return action(resolved);
      });
    }

    /// <summary>
    /// Sucht ein geöffnetes Einzelprojekt oder eine Project-Server-LocalSession.
    /// </summary>
    static internal ResolvedProject ResolveOpenProject(TiaPortal tia, string projectName, string kind = null) {
      string wantKind = NormalizeKind(kind);
      var candidates = new List<ResolvedProject>();

      if (wantKind == null || wantKind == ProjectRoutes.KindProject) {
        foreach (Project project in tia.Projects) {
          if (MatchesProject(project, projectName)) {
            candidates.Add(new ResolvedProject { Project = project, Kind = ProjectRoutes.KindProject });
          }
        }
      }

      if (wantKind == null || wantKind == ProjectRoutes.KindLocalSession) {
        foreach (LocalSession session in tia.LocalSessions) {
          MultiuserProject multiuser;
          try {
            multiuser = session.Project;
          } catch {
            continue;
          }
          if (multiuser == null || !MatchesProject(multiuser, projectName)) {
            continue;
          }
          candidates.Add(new ResolvedProject {
            Project = multiuser,
            LocalSession = session,
            Kind = ProjectRoutes.KindLocalSession
          });
        }
      }

      if (candidates.Count == 0) {
        return null;
      }
      if (candidates.Count == 1 || wantKind != null) {
        return candidates[0];
      }
      // Bei Mehrdeutigkeit: LocalSession vor Einzelprojekt.
      return candidates.FirstOrDefault(c => c.Kind == ProjectRoutes.KindLocalSession)
        ?? candidates[0];
    }

    static string NormalizeKind(string kind) {
      if (string.IsNullOrWhiteSpace(kind)) {
        return null;
      }
      if (string.Equals(kind, ProjectRoutes.KindProject, StringComparison.OrdinalIgnoreCase)) {
        return ProjectRoutes.KindProject;
      }
      if (string.Equals(kind, ProjectRoutes.KindLocalSession, StringComparison.OrdinalIgnoreCase)) {
        return ProjectRoutes.KindLocalSession;
      }
      return kind.Trim();
    }

    static bool MatchesProject(ProjectBase project, string projectName) {
      if (string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
      if (project.Path == null) {
        return false;
      }
      return string.Equals(Path.GetFileNameWithoutExtension(project.Path.Name), projectName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(project.Path.FullName, projectName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Führt eine Aktion mit einem TIA Portal-Prozess in einer eigenen Openness-Sitzung aus (wie ExecuteWithProject).
    /// </summary>
    /// <param name="processId">Prozess-ID</param>
    /// <param name="action">Aktion, die mit der Sitzung ausgeführt wird; Openness-Objekte dürfen sie nicht überleben</param>
    /// <returns>Rückgabewert der Aktion</returns>
    public T ExecuteWithPortal<T>(int processId, Func<TiaPortal, T> action) {
      lock (_lock) {
        try {
          return ExecuteInSession(processId, action);
        } catch (Exception ex) when (IsInstanceLimit(ex)) {
          Console.Error.WriteLine($"TIA Openness instance limit reached, retrying with a new session: {ex.Message}");
          return ExecuteInSession(processId, action);
        }
      }
    }

    private T ExecuteInSession<T>(int processId, Func<TiaPortal, T> action) {
      var process = TiaPortal.GetProcesses().FirstOrDefault(p => p.Id == processId);
      if (process == null) {
        throw new InvalidOperationException($"Error: TIA Process with ID {processId} not found.");
      }
      try {
        try {
          _tia = process.Attach();
        } catch (Exception ex) {
          throw new InvalidOperationException(
            $"TIA Attach failed for process {processId}: {ex.GetType().Name}: {ex.Message}", ex);
        }
        return action(_tia);
      } finally {
        TryDisposePortal();
      }
    }

    private static bool IsInstanceLimit(Exception ex) {
      for (Exception e = ex; e != null; e = e.InnerException) {
        if (e.Message != null && e.Message.IndexOf("maximum number", StringComparison.OrdinalIgnoreCase) >= 0
            && e.Message.IndexOf("instances", StringComparison.OrdinalIgnoreCase) >= 0) {
          return true;
        }
      }
      return false;
    }

    private void TryDisposePortal() {
      try {
        _tia?.Dispose();
      } catch { /* ignore */ }
      _tia = null;
    }

    /// <summary>
    /// Gibt eine offene Sitzung beim Beenden frei; wartet höchstens kurz auf eine laufende Anfrage.
    /// </summary>
    public void Dispose() {
      bool locked = System.Threading.Monitor.TryEnter(_lock, TimeSpan.FromSeconds(2));
      try {
        TryDisposePortal();
      } finally {
        if (locked) {
          System.Threading.Monitor.Exit(_lock);
        }
      }
    }
  }

  /// <summary>
  /// Stellt Methoden für den Zugriff auf Projekte bereit.
  /// </summary>
  static internal class cTiaProject {
    /// <summary>
    /// Gibt eine Liste aller offenen TIA-Projekte und Project-Server-LocalSessions zurück.
    /// Bei .amc per Attach (LocalSessions); sonst ProjectPath (vermeidet langsame Attach-Aufrufe).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <returns>JSON-String mit der Liste oder Fehlermeldung</returns>
    [RestRoute(ProjectRoutes.List)]
    static public string List(HttpListenerContext context) {
      var processes = TiaPortal.GetProcesses();
      var projectInfos = new List<Info>();
      var warnings = new List<string>();

      foreach (var process in processes) {
        FileInfo projectFile = null;
        try {
          projectFile = process.ProjectPath;
        } catch (Exception ex) {
          warnings.Add($"Process {process.Id}: ProjectPath unavailable ({ex.GetType().Name}: {ex.Message}).");
          continue;
        }
        if (projectFile == null) {
          continue;
        }

        bool multiuserFile = (projectFile.Extension ?? "").StartsWith(".amc", StringComparison.OrdinalIgnoreCase);
        if (multiuserFile) {
          try {
            using (var tia = process.Attach()) {
              int before = projectInfos.Count;
              int index = 0;
              foreach (LocalSession session in tia.LocalSessions) {
                MultiuserProject multiuser;
                try {
                  multiuser = session.Project;
                } catch (Exception ex) {
                  warnings.Add($"Process {process.Id}: LocalSession[{index}].Project unavailable ({ex.Message}).");
                  index++;
                  continue;
                }
                index++;
                if (multiuser == null) {
                  continue;
                }
                projectInfos.Add(new Info {
                  ProcessId = process.Id,
                  Name = multiuser.Name,
                  Path = multiuser.Path != null ? multiuser.Path.FullName : null,
                  Kind = ProjectRoutes.KindLocalSession
                });
              }
              if (projectInfos.Count > before) {
                continue;
              }
            }
          } catch (Exception ex) {
            warnings.Add($"Process {process.Id}: Attach failed ({ex.GetType().Name}: {ex.Message}).");
            Console.Error.WriteLine($"TIA Attach failed for process {process.Id}: {ex.GetType().Name}: {ex.Message}");
          }
        }

        projectInfos.Add(new Info {
          ProcessId = process.Id,
          Name = Path.GetFileNameWithoutExtension(projectFile.Name),
          Path = projectFile.FullName,
          Kind = multiuserFile ? ProjectRoutes.KindLocalSession : ProjectRoutes.KindProject
        });
      }

      foreach (string warning in warnings) {
        Console.Error.WriteLine(warning);
      }

      if (projectInfos.Count == 0) {
        string errorMessage = warnings.Count > 0
          ? "Error: No open projects found. " + string.Join(" ", warnings)
          : "Error: No TIA Portal instances with open projects found.";
        Console.Error.WriteLine(errorMessage);
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        context.Response.ContentType = "text/plain";
        return errorMessage;
      }

      context.Response.ContentType = "application/json";
      return JsonConvert.SerializeObject(projectInfos, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }

    /// <summary>
    /// Öffnet ein Projekt (.ap&lt;Version&gt;) oder dearchiviert ein Projektarchiv (.zap&lt;Version&gt;) in TIA Portal.
    /// Ohne processId wird eine TIA Portal-Instanz ohne geöffnetes Projekt verwendet; gibt es keine, wird TIA Portal
    /// (mit Oberfläche, Version aus appsettings.json) gestartet. Ist das Projekt bereits geöffnet, wird diese Instanz gemeldet.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="path">Absoluter Dateisystem-Pfad zur TIA-Projektdatei (.ap…) oder zum Projektarchiv (.zap…) auf dem Rechner mit TIA Portal</query>
    /// <query name="processId">Optional: Prozess-ID der TIA Portal-Instanz; sie darf kein Projekt geöffnet haben</query>
    /// <query name="upgrade">Optional: true öffnet bzw. dearchiviert ein Projekt einer älteren TIA-Version mit Hochrüstung</query>
    /// <query name="targetDirectory">Nur für Archive: absoluter Dateisystem-Zielordner, in den das Projekt dearchiviert wird (nicht TIA-intern)</query>
    /// <query name="startTimeoutSeconds">Optional: maximale Wartezeit auf den Start von TIA Portal (Standard 300)</query>
    /// <returns>JSON-String mit OpenResult oder Fehlermeldung</returns>
    [RestRoute(ProjectRoutes.Open, "POST")]
    static public string Open(HttpListenerContext context) {
      string pathStr = context.Request.QueryString["path"];
      string processIdStr = context.Request.QueryString["processId"];
      bool upgrade = string.Equals(context.Request.QueryString["upgrade"], "true", StringComparison.OrdinalIgnoreCase);
      string targetDirectoryStr = context.Request.QueryString["targetDirectory"];
      string timeoutStr = context.Request.QueryString["startTimeoutSeconds"];

      if (string.IsNullOrWhiteSpace(pathStr) || !Path.IsPathRooted(pathStr)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: path must be an absolute path of a project file or project archive.");
      }
      var file = new FileInfo(pathStr.Trim());
      if (!file.Exists) {
        return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: File not found: {file.FullName}");
      }
      bool archive = file.Extension.StartsWith(".zap", StringComparison.OrdinalIgnoreCase);
      DirectoryInfo targetDirectory = null;
      if (archive) {
        if (string.IsNullOrWhiteSpace(targetDirectoryStr) || !Path.IsPathRooted(targetDirectoryStr)) {
          return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: targetDirectory (absolute path) is required to retrieve a project archive.");
        }
        targetDirectory = new DirectoryInfo(targetDirectoryStr.Trim());
      } else if (!file.Extension.StartsWith(".ap", StringComparison.OrdinalIgnoreCase)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Unsupported file type {file.Extension}; expected a project file (.ap<version>) or archive (.zap<version>).");
      }
      int processId = -1;
      if (!string.IsNullOrWhiteSpace(processIdStr) && !int.TryParse(processIdStr, out processId)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid process ID {processIdStr}.");
      }
      int startTimeoutSeconds = 300;
      if (!string.IsNullOrWhiteSpace(timeoutStr) && (!int.TryParse(timeoutStr, out startTimeoutSeconds) || startTimeoutSeconds <= 0)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Invalid startTimeoutSeconds {timeoutStr}.");
      }

      try {
        IList<TiaPortalProcess> processes = TiaPortal.GetProcesses();
        if (!archive) {
          TiaPortalProcess opened = processes.FirstOrDefault(p => SamePath(p.ProjectPath, file));
          if (opened != null) {
            if (processId >= 0 && opened.Id != processId) {
              return ErrorResponse(context, HttpStatusCode.Conflict, $"Error: Project {file.FullName} is already open in TIA Portal process {opened.Id}.");
            }
            return OpenResponse(context, new OpenResult { ProcessId = opened.Id, Name = Path.GetFileNameWithoutExtension(file.Name), Path = file.FullName, Mode = "AlreadyOpen" });
          }
        }

        bool started = false;
        if (processId >= 0) {
          TiaPortalProcess process = processes.FirstOrDefault(p => p.Id == processId);
          if (process == null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: TIA Process with ID {processId} not found.");
          }
          if (process.ProjectPath != null) {
            return ErrorResponse(context, HttpStatusCode.Conflict, $"Error: TIA Portal process {processId} already has project {process.ProjectPath.FullName} open. Close it first (tia_projects_close).");
          }
        } else {
          TiaPortalProcess idle = processes
            .Where(p => p.ProjectPath == null)
            .OrderByDescending(p => p.Mode == TiaPortalMode.WithUserInterface)
            .FirstOrDefault();
          if (idle != null) {
            processId = idle.Id;
          } else {
            string startError = StartTiaPortal(processes.Select(p => p.Id), startTimeoutSeconds, out processId);
            if (startError != null) {
              return ErrorResponse(context, HttpStatusCode.ServiceUnavailable, startError);
            }
            started = true;
          }
        }

        OpenResult result = TiaConnectionManager.Instance.ExecuteWithPortal(processId, tia => {
          Project project;
          string mode;
          if (archive) {
            targetDirectory.Create();
            project = upgrade ? tia.Projects.RetrieveWithUpgrade(file, targetDirectory) : tia.Projects.Retrieve(file, targetDirectory);
            mode = upgrade ? "RetrieveWithUpgrade" : "Retrieve";
          } else {
            project = upgrade ? tia.Projects.OpenWithUpgrade(file) : tia.Projects.Open(file);
            mode = upgrade ? "OpenWithUpgrade" : "Open";
          }
          return new OpenResult { ProcessId = processId, Name = project.Name, Path = project.Path.FullName, Mode = mode };
        });
        result.StartedTiaPortal = started;
        return OpenResponse(context, result);
      } catch (Exception ex) {
        string hint = upgrade ? "" : " If the project was created with an older TIA Portal version, retry with upgrade=true.";
        return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error opening project {file.FullName}: {ex.Message}{hint}");
      }
    }

    /// <summary>
    /// Speichert ein geöffnetes Projekt.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="kind">Optional: Project oder LocalSession zur Disambiguierung</query>
    /// <returns>JSON-String mit SaveResult oder Fehlermeldung</returns>
    [RestRoute(ProjectRoutes.Save, "POST")]
    static public string Save(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      try {
        SaveResult result = TiaConnectionManager.Instance.ExecuteWithResolvedProject(processIdStr, projectName, resolved => {
          ProjectBase project = resolved.Project;
          bool modified = project.IsModified;
          resolved.Save();
          return new SaveResult {
            ProcessId = int.Parse(processIdStr),
            Name = project.Name,
            Path = project.Path != null ? project.Path.FullName : null,
            WasModified = modified,
            Kind = resolved.Kind
          };
        }, context.Request.QueryString["kind"]);
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(result);
      } catch (TiaProjectNotFoundException ex) {
        return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: {ex.Message}");
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error saving project {projectName}: {ex.Message}");
      }
    }

    /// <summary>
    /// Schließt ein geöffnetes Projekt; TIA Portal bleibt geöffnet. Hat das Projekt ungespeicherte Änderungen,
    /// muss save=true (speichern) oder discardChanges=true (verwerfen) angegeben werden, sonst wird mit 409 abgebrochen.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="kind">Optional: Project oder LocalSession zur Disambiguierung</query>
    /// <query name="save">Optional: true speichert das Projekt vor dem Schließen</query>
    /// <query name="discardChanges">Optional: true schließt auch bei ungespeicherten Änderungen ohne zu speichern</query>
    /// <returns>JSON-String mit CloseResult oder Fehlermeldung</returns>
    [RestRoute(ProjectRoutes.Close, "POST")]
    static public string Close(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      bool save = string.Equals(context.Request.QueryString["save"], "true", StringComparison.OrdinalIgnoreCase);
      bool discardChanges = string.Equals(context.Request.QueryString["discardChanges"], "true", StringComparison.OrdinalIgnoreCase);
      if (save && discardChanges) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: save and discardChanges cannot both be true.");
      }
      try {
        CloseResult result = TiaConnectionManager.Instance.ExecuteWithResolvedProject(processIdStr, projectName, resolved => {
          ProjectBase project = resolved.Project;
          bool modified = project.IsModified;
          var closeResult = new CloseResult {
            ProcessId = int.Parse(processIdStr),
            Name = project.Name,
            Path = project.Path != null ? project.Path.FullName : null,
            Kind = resolved.Kind
          };
          if (modified && !save && !discardChanges) {
            return null;
          }
          if (save) {
            resolved.Save();
            closeResult.Saved = true;
          }
          closeResult.DiscardedChanges = modified && !save;
          resolved.Close();
          return closeResult;
        }, context.Request.QueryString["kind"]);
        if (result == null) {
          return ErrorResponse(context, HttpStatusCode.Conflict,
            $"Error: Project {projectName} has unsaved changes. Pass save=true to save them or discardChanges=true to discard them.");
        }
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(result);
      } catch (TiaProjectNotFoundException ex) {
        return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: {ex.Message}");
      } catch (Exception ex) {
        return ErrorResponse(context, HttpStatusCode.InternalServerError, $"Error closing project {projectName}: {ex.Message}");
      }
    }

    static string OpenResponse(HttpListenerContext context, OpenResult result) {
      context.Response.ContentType = "application/json";
      return JsonConvert.SerializeObject(result);
    }

    static bool SamePath(FileInfo a, FileInfo b) {
      return a != null && b != null && string.Equals(Path.GetFullPath(a.FullName), Path.GetFullPath(b.FullName), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Startet TIA Portal mit Oberfläche und wartet, bis die neue Instanz Openness-Verbindungen annimmt.
    /// </summary>
    /// <param name="knownProcessIds">Bereits laufende TIA Portal-Prozesse</param>
    /// <param name="timeoutSeconds">Maximale Wartezeit</param>
    /// <param name="processId">Prozess-ID der neuen Instanz</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static string StartTiaPortal(IEnumerable<int> knownProcessIds, int timeoutSeconds, out int processId) {
      processId = -1;
      string version = RestSettings.Current.TiaVersion;
      string exe = Path.Combine(RestSettings.Current.SiemensPath, $"Portal V{version}", "Bin", "Siemens.Automation.Portal.exe");
      if (!File.Exists(exe)) {
        return $"Error: TIA Portal not found: {exe}";
      }
      var known = new HashSet<int>(knownProcessIds);
      int startedId;
      try {
        using (var started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) {
          UseShellExecute = true,
          WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal
        })) {
          startedId = started?.Id ?? -1;
        }
      } catch (Exception ex) {
        return $"Error: TIA Portal could not be started: {ex.Message}";
      }
      Console.WriteLine($"TIA Portal V{version} started (process {startedId}), waiting for Openness ...");

      DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
      string lastError = "process not visible to Openness";
      while (DateTime.UtcNow < deadline) {
        System.Threading.Thread.Sleep(2000);
        TiaPortalProcess process = TiaPortal.GetProcesses().FirstOrDefault(p => p.Id == startedId)
          ?? TiaPortal.GetProcesses().FirstOrDefault(p => !known.Contains(p.Id));
        if (process == null) {
          continue;
        }
        try {
          int id = process.Id;
          TiaConnectionManager.Instance.ExecuteWithPortal(id, tia => tia.Projects.Count);
          processId = id;
          return null;
        } catch (Exception ex) {
          lastError = ex.Message;
        }
      }
      return $"Error: TIA Portal was started but did not accept an Openness connection within {timeoutSeconds} s ({lastError}).";
    }



    #region Hilfsmethoden für den Projekt zugriff

    /// <summary>
    /// Gibt die PLC-Software für ein Gerät in einem Projekt zurück.
    /// </summary>
    /// <param name="project">Projekt</param>
    /// <param name="deviceName">Name des Geräts</param>
    /// <param name="deviceItemName">Name des Geräteelements</param>
    /// <param name="plcSoftware">Out-Parameter für die PLC-Software</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static public string GetPlcSoftware(ProjectBase project, string deviceName, string deviceItemName, out PlcSoftware plcSoftware) {
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


    static public string GetPlcSystemBlockGroup(ProjectBase project, string deviceName, string deviceItemName, out PlcSoftware plcSoftware, out PlcBlockGroup plcBlockGroup) {
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

    static public string GetPlcSystemTypeGroup(ProjectBase project, string deviceName, string deviceItemName, out PlcSoftware plcSoftware, out PlcTypeGroup plcTypeGroup) {
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

    static public string GetPlcTagTableGroup(ProjectBase project, string deviceName, string deviceItemName, out PlcSoftware plcSoftware, out PlcTagTableGroup plcTagTableGroup) {
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
