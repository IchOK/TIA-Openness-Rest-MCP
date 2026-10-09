using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper;
using Tophinke.TiaOpenness.Tool.Types.PLC.Compile;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC {
  /// <summary>
  /// Übersetzen von Bausteinen, Datentypen und der kompletten PLC-Software.
  /// Gemeldet werden die Fehler und Warnungen des Übersetzens; Informationsmeldungen werden ausgelassen.
  /// </summary>
  static internal class cTiaCompile {
    public const string TargetBlock = "Block";
    public const string TargetUdt = "UDT";
    public const string TargetPlc = "PLC";

    /// <summary>
    /// Übersetzt einen einzelnen PLC-Baustein oder Datentyp (HTTP POST).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="name">Name des Bausteins oder Datentyps</query>
    /// <query name="itemType">Optional "Block" oder "UDT"; ohne Angabe wird zuerst ein Baustein, dann ein Datentyp gesucht</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <returns>JSON-String mit CompileResult oder Fehlermeldung</returns>
    static public string Item(HttpListenerContext context) {
      string methodError = RequireMethod(context, "POST", CompileRoutes.Item);
      if (methodError != null) {
        return methodError;
      }

      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string name = context.Request.QueryString["name"];
      string itemType = context.Request.QueryString["itemType"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      if (string.IsNullOrWhiteSpace(name)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: name is required.");
      }
      bool searchBlock = string.IsNullOrWhiteSpace(itemType) || itemType.Equals(TargetBlock, StringComparison.OrdinalIgnoreCase);
      bool searchType = string.IsNullOrWhiteSpace(itemType) || itemType.Equals(TargetUdt, StringComparison.OrdinalIgnoreCase);
      if (!searchBlock && !searchType) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: Unknown itemType '{itemType}'. Allowed: {TargetBlock}, {TargetUdt} or empty.");
      }

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcSoftware(project, deviceName, deviceItemName, out PlcSoftware plcSoftware);
          if (errorMessage != null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, errorMessage);
          }

          PlcBlock block = searchBlock ? cTiaFindHelpers.FindBlock(plcSoftware.BlockGroup, name) : null;
          PlcType type = searchType ? cTiaFindHelpers.FindType(plcSoftware.TypeGroup, name) : null;
          if (block == null && type == null) {
            string kind = searchBlock && searchType ? "block or UDT" : (searchBlock ? "block" : "UDT");
            return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: No {kind} named {name} found in PLC software {plcSoftware.Name}.");
          }
          if (block != null && type != null) {
            return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: {name} is both a block and a UDT in PLC software {plcSoftware.Name}; pass itemType {TargetBlock} or {TargetUdt}.");
          }

          IEngineeringServiceProvider target = (IEngineeringServiceProvider)block ?? type;
          ICompilable compilable = target.GetService<ICompilable>();
          if (compilable == null) {
            return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: {name} cannot be compiled.");
          }

          CompilerResult compilerResult = compilable.Compile();
          var messages = new List<CompileMessage>();
          CollectMessages(compilerResult, messages);

          var result = new CompileResult {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            Target = block != null ? TargetBlock : TargetUdt,
            Name = block?.Name ?? type.Name,
            Path = block != null ? cTiaBlocks.GetBlockPath(block) : cTiaUDTs.GetTypePath(type),
            State = compilerResult.State.ToString(),
            ErrorCount = compilerResult.ErrorCount,
            WarningCount = compilerResult.WarningCount,
            Messages = messages
          };

          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
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
    /// Übersetzt die PLC-Software (HTTP POST); entspricht "Software (nur Änderungen)".
    /// Openness bietet kein "Software (alle Bausteine neu übersetzen)", und ein konsistenter Baustein wird auch einzeln nicht neu übersetzt.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processIdStr">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des Geräts</query>
    /// <query name="deviceItemName">Name des Geräteelements</query>
    /// <returns>JSON-String mit CompileResult oder Fehlermeldung</returns>
    static public string Plc(HttpListenerContext context) {
      string methodError = RequireMethod(context, "POST", CompileRoutes.Plc);
      if (methodError != null) {
        return methodError;
      }

      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var errorMessage = cTiaProject.GetPlcSoftware(project, deviceName, deviceItemName, out PlcSoftware plcSoftware);
          if (errorMessage != null) {
            return ErrorResponse(context, HttpStatusCode.NotFound, errorMessage);
          }
          ICompilable softwareCompilable = plcSoftware.GetService<ICompilable>();
          if (softwareCompilable == null) {
            return ErrorResponse(context, HttpStatusCode.BadRequest, $"Error: PLC software {plcSoftware.Name} cannot be compiled.");
          }

          CompilerResult compilerResult = softwareCompilable.Compile();
          var messages = new List<CompileMessage>();
          CollectMessages(compilerResult, messages);

          var result = new CompileResult {
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcSoftware.Name,
            Target = TargetPlc,
            Name = plcSoftware.Name,
            State = compilerResult.State.ToString(),
            ErrorCount = compilerResult.ErrorCount,
            WarningCount = compilerResult.WarningCount,
            Messages = messages
          };

          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
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
    /// Sammelt die Fehler und Warnungen aus dem Meldungsbaum. Gemeldet werden nur die Blätter;
    /// Location enthält die Pfade aller übergeordneten Meldungen.
    /// </summary>
    static private void CollectMessages(CompilerResult compilerResult, List<CompileMessage> messages) {
      foreach (CompilerResultMessage message in compilerResult.Messages) {
        CollectMessages(message, new List<string>(), messages);
      }
    }

    static private void CollectMessages(CompilerResultMessage message, List<string> parents, List<CompileMessage> messages) {
      var location = new List<string>(parents);
      if (!string.IsNullOrWhiteSpace(message.Path)) {
        location.Add(message.Path);
      }

      if (message.Messages.Count > 0) {
        foreach (CompilerResultMessage child in message.Messages) {
          CollectMessages(child, location, messages);
        }
        return;
      }

      // Ohne Location ist es die Zusammenfassung "Compiling finished (errors: …)", die schon in ErrorCount/WarningCount steckt.
      if (location.Count == 0 || (message.State != CompilerResultState.Error && message.State != CompilerResultState.Warning)) {
        return;
      }
      messages.Add(new CompileMessage {
        Location = location.ToArray(),
        BlockName = location.Select(l => BlockLocation.Match(l)).Where(m => m.Success).Select(m => m.Groups["name"].Value).LastOrDefault(),
        State = message.State.ToString(),
        Description = message.Description
      });
    }

    private static readonly Regex BlockLocation = new Regex(@"^(?<name>.+) \((?:OB|FB|FC|DB|SFB|SFC)\d+\)$");
  }
}
