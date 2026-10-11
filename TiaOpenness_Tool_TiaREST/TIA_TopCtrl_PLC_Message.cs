using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaREST.PLC;
using Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper;
using Tophinke.TiaOpenness.Tool.Types.TopCtrl.PLC.Message;

namespace Tophinke.TiaOpenness.Tool.TiaREST.TopCtrl.PLC {
  /// <summary>
  /// Erstellt die Meldungsliste der TopCtrl-Meldetreiber.
  /// </summary>
  static internal class cTiaMessages {
    /// <summary>
    /// Listet die Meldungen aller HMI-Meldetreiber (Variablen vom Typ tbMsg_Drv16..._T). Jedes Bit eines Treibers wird über den
    /// Treiberaufruf (tbMsg_Drv16...), den Meldebus (tbMsg_Bus16_T bzw. Array of tbMsg_BusElement_T) und den Melde-Handler
    /// (tbMsg_Handler_...) bis zur Meldevariablen (tbMsg_Element_T) zurückverfolgt. Meldetext ist der Kommentar der Meldevariablen,
    /// die Konfiguration stammt aus den Startwerten von stConfig.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Optional: Name des Geräts; ohne Angabe alle PLCs</query>
    /// <query name="deviceItemName">Optional: Name des Geräteelements; ohne Angabe alle PLCs</query>
    /// <query name="includeUnselected">Optional: true liefert auch Meldungen mit stConfig.xSelect = FALSE</query>
    /// <returns>JSON-String mit ListResult (Messages, Warnings) oder Fehlermeldung</returns>
    [RestRoute(MessageRoutes.List)]
    static public string List(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];
      bool includeUnselected = string.Equals(context.Request.QueryString["includeUnselected"], "true", StringComparison.OrdinalIgnoreCase);

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          ListResult result = Collect(project, deviceName, deviceItemName, includeUnselected);
          if (result == null) {
            return NoPlcResponse(context, deviceName, deviceItemName);
          }
          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(result, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        });
        return retValue;
      } catch (EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.ContentType = "text/plain";
        return "Error: TIA session unavailable. Please re-open TIA Portal.";
      } catch (Exception ex) {
        Console.Error.WriteLine($"Error collecting messages: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "text/plain";
        return $"Error collecting messages: {ex.Message}";
      }
    }

    /// <summary>
    /// Exportiert die Meldungsliste als CSV-Datei nach Root\Projekt\&lt;fileName&gt; (UTF-8 mit BOM, eine Zeile je Meldung,
    /// je Sprache eine Textspalte Text_&lt;Sprache&gt;). Eine vorhandene Datei wird überschrieben.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="rootDirectory">Absoluter Pfad des Wurzelverzeichnisses</query>
    /// <query name="fileName">Optional: Dateiname ohne Pfad; Standard Messages.csv, .csv wird ergänzt</query>
    /// <query name="separator">Optional: Trennzeichen ";" (Standard), "," oder "tab"</query>
    /// <query name="deviceName">Optional: Name des Geräts; ohne Angabe alle PLCs</query>
    /// <query name="deviceItemName">Optional: Name des Geräteelements; ohne Angabe alle PLCs</query>
    /// <query name="includeUnselected">Optional: true exportiert auch Meldungen mit stConfig.xSelect = FALSE</query>
    /// <returns>JSON-String mit FileData oder Fehlermeldung</returns>
    [RestRoute(MessageRoutes.GetFile)]
    static public string GetFile(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];
      bool includeUnselected = string.Equals(context.Request.QueryString["includeUnselected"], "true", StringComparison.OrdinalIgnoreCase);

      string rootError = cTiaExportHelpers.ResolveRootDirectory(context.Request.QueryString["rootDirectory"], out string fullRoot);
      if (rootError != null) {
        return TextResponse(context, HttpStatusCode.BadRequest, rootError);
      }
      string fileError = ResolveFileName(context.Request.QueryString["fileName"], out string fileName);
      if (fileError != null) {
        return TextResponse(context, HttpStatusCode.BadRequest, fileError);
      }
      string separatorError = ResolveSeparator(context.Request.QueryString["separator"], out char separator);
      if (separatorError != null) {
        return TextResponse(context, HttpStatusCode.BadRequest, separatorError);
      }

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          ListResult result = Collect(project, deviceName, deviceItemName, includeUnselected);
          if (result == null) {
            return NoPlcResponse(context, deviceName, deviceItemName);
          }
          string directoryError = cTiaExportHelpers.GetFileDirectory(fullRoot, project.Name, null, out string directory);
          if (directoryError != null) {
            return TextResponse(context, HttpStatusCode.BadRequest, directoryError);
          }
          Directory.CreateDirectory(directory);
          string filePath = Path.Combine(directory, fileName);
          File.WriteAllText(filePath, BuildCsv(result.Messages, separator), new UTF8Encoding(true));

          var fileData = new FileData {
            Format = "CSV",
            Directory = directory,
            Files = new[] { filePath },
            MessageCount = result.Messages.Count,
            Warnings = result.Warnings
          };
          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(fileData);
        });
        return retValue;
      } catch (EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        return TextResponse(context, HttpStatusCode.ServiceUnavailable, "Error: TIA session unavailable. Please re-open TIA Portal.");
      } catch (Exception ex) {
        Console.Error.WriteLine($"Error exporting messages: {ex.Message}");
        return TextResponse(context, HttpStatusCode.InternalServerError, $"Error exporting messages: {ex.Message}");
      }
    }

    /// <summary>
    /// Sammelt die Meldungen aller passenden PLCs; null, wenn keine PLC gefunden wurde.
    /// </summary>
    static internal ListResult Collect(Project project, string deviceName, string deviceItemName, bool includeUnselected) {
      var result = new ListResult();
      int plcCount = 0;
      foreach (Device device in project.Devices) {
        if (!string.IsNullOrEmpty(deviceName) && !device.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
        foreach (DeviceItem deviceItem in device.DeviceItems) {
          if (!string.IsNullOrEmpty(deviceItemName) && !deviceItem.Name.Equals(deviceItemName, StringComparison.OrdinalIgnoreCase)) {
            continue;
          }
          SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
          if (container == null || !(container.Software is PlcSoftware plcSoftware)) {
            continue;
          }
          plcCount++;
          new cMessageTracer(plcSoftware, device.Name, deviceItem.Name, includeUnselected, result).Run();
        }
      }
      return plcCount == 0 ? null : result;
    }

    static string NoPlcResponse(HttpListenerContext context, string deviceName, string deviceItemName) {
      return TextResponse(context, HttpStatusCode.NotFound,
        "Error: No PLC software found" + (string.IsNullOrEmpty(deviceName) ? "." : $" for device {deviceName}/{deviceItemName}."));
    }

    static string TextResponse(HttpListenerContext context, HttpStatusCode statusCode, string message) {
      context.Response.StatusCode = (int)statusCode;
      context.Response.ContentType = "text/plain";
      return message;
    }

    static string ResolveFileName(string requested, out string fileName) {
      fileName = string.IsNullOrWhiteSpace(requested) ? "Messages.csv" : requested.Trim();
      if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName == "." || fileName == "..") {
        string invalid = fileName;
        fileName = null;
        return $"Error: fileName must be a plain file name without path: {invalid}";
      }
      if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) {
        fileName += ".csv";
      }
      return null;
    }

    static string ResolveSeparator(string requested, out char separator) {
      separator = ';';
      if (string.IsNullOrEmpty(requested) || requested == ";") {
        return null;
      }
      if (requested == ",") {
        separator = ',';
        return null;
      }
      if (requested.Equals("tab", StringComparison.OrdinalIgnoreCase) || requested == "\t") {
        separator = '\t';
        return null;
      }
      return $"Error: Unknown separator '{requested}'. Use ';', ',' or 'tab'.";
    }

    /// <summary>
    /// Erstellt den CSV-Inhalt: Kopfzeile mit den Feldnamen, je Sprache eine Spalte Text_&lt;Sprache&gt;.
    /// </summary>
    static string BuildCsv(List<Info> messages, char separator) {
      List<string> languages = messages
        .Where(m => m.Texts != null)
        .SelectMany(m => m.Texts.Keys)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
        .ToList();

      var columns = new List<KeyValuePair<string, Func<Info, string>>> {
        Column("PlcName", m => m.PlcName),
        Column("Driver", m => m.Driver),
        Column("HmiVariable", m => m.HmiVariable),
        Column("HmiBit", m => m.HmiBit.ToString(CultureInfo.InvariantCulture)),
        Column("Element", m => m.Element)
      };
      if (languages.Count == 0) {
        columns.Add(Column("Text", m => m.Text));
      }
      foreach (string language in languages) {
        string lang = language;
        columns.Add(Column("Text_" + lang, m => m.Texts != null && m.Texts.TryGetValue(lang, out string text) ? text : null));
      }
      columns.AddRange(new[] {
        Column("Type", m => m.Type?.ToString(CultureInfo.InvariantCulture)),
        Column("TypeConstant", m => m.TypeConstant),
        Column("Select", m => CsvBool(m.Select)),
        Column("AutoQuit", m => CsvBool(m.AutoQuit)),
        Column("Sync", m => CsvBool(m.Sync)),
        Column("DoReset", m => CsvBool(m.DoReset)),
        Column("IsFault", m => CsvBool(m.IsFault)),
        Column("ConfigSource", m => m.ConfigSource),
        Column("ElementBlock", m => m.ElementBlock),
        Column("ElementFb", m => m.ElementFb),
        Column("BusElement", m => m.BusElement),
        Column("Handler", m => m.Handler),
        Column("HandlerBlock", m => m.HandlerBlock),
        Column("DriverFunction", m => m.DriverFunction),
        Column("DriverBlock", m => m.DriverBlock),
        Column("DeviceName", m => m.DeviceName),
        Column("DeviceItemName", m => m.DeviceItemName)
      });

      string separatorText = separator.ToString();
      var sb = new StringBuilder();
      sb.Append(string.Join(separatorText, columns.Select(c => CsvField(c.Key, separator)))).Append("\r\n");
      foreach (Info message in messages) {
        sb.Append(string.Join(separatorText, columns.Select(c => CsvField(c.Value(message), separator)))).Append("\r\n");
      }
      return sb.ToString();
    }

    static KeyValuePair<string, Func<Info, string>> Column(string name, Func<Info, string> value) {
      return new KeyValuePair<string, Func<Info, string>>(name, value);
    }

    static string CsvBool(bool value) {
      return value ? "TRUE" : "FALSE";
    }

    static string CsvField(string value, char separator) {
      if (string.IsNullOrEmpty(value)) {
        return "";
      }
      if (value.IndexOf(separator) < 0 && value.IndexOfAny(new[] { '"', '\r', '\n' }) < 0) {
        return value;
      }
      return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
  }

  /// <summary>
  /// Verfolgt die Meldungen einer PLC von den HMI-Treibervariablen bis zu den Meldevariablen zurück.
  /// Bausteine werden exportiert (SD, sonst SimaticML) und nur die Aufrufe ausgewertet; Verwendungsstellen liefern die Querverweise.
  /// </summary>
  internal class cMessageTracer {
    const string DriverPrefix = "tbMsg_Drv16";
    const string MultiDriverPrefix = "tbMsg_Drv16x";
    const string HandlerPrefix = "tbMsg_Handler_";
    const string BusArray = "aMsg";
    const string ConfigMember = "stConfig";
    const int BitsPerDriver = 16;
    const int MaxDepth = 12;

    class BlockEntry {
      public PlcBlock Block;
      public string Name;
      public string Kind;
      public string InstanceOf;
    }

    class DriverCall {
      public string Function;
      public string Caller;
      public SourceOperand Bus;
      public SourceOperand Hmi;
    }

    class Link {
      public SourceOperand Element;
      public string Handler;
      public string HandlerBlock;
      public Regex Pattern;
      public List<string> Variables;
    }

    readonly PlcSoftware _plc;
    readonly string _deviceName;
    readonly string _deviceItemName;
    readonly bool _includeUnselected;
    readonly ListResult _result;

    readonly Dictionary<string, BlockEntry> _blocks = new Dictionary<string, BlockEntry>(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, PlcType> _types;
    Dictionary<string, string> _constants;
    readonly Dictionary<string, SourceModel> _models = new Dictionary<string, SourceModel>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, HashSet<string>> _users = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
    readonly List<SourceOperand> _trackedBuses = new List<SourceOperand>();
    readonly Dictionary<string, Link> _links = new Dictionary<string, Link>();
    readonly List<Link> _patternLinks = new List<Link>();
    readonly List<Link> _configCopies = new List<Link>();
    readonly HashSet<string> _analyzedTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _visited = new HashSet<string>();
    readonly HashSet<string> _warnings = new HashSet<string>();

    public cMessageTracer(PlcSoftware plc, string deviceName, string deviceItemName, bool includeUnselected, ListResult result) {
      _plc = plc;
      _deviceName = deviceName;
      _deviceItemName = deviceItemName;
      _includeUnselected = includeUnselected;
      _result = result;
    }

    public void Run() {
      IndexBlocks(_plc.BlockGroup);
      List<DriverCall> driverCalls = FindDriverCalls();
      foreach (DriverCall driverCall in driverCalls) {
        if (!_trackedBuses.Any(b => b.Key == driverCall.Bus.Key)) {
          _trackedBuses.Add(driverCall.Bus);
        }
      }
      foreach (string busBlock in _trackedBuses.Select(b => b.Root).Distinct(StringComparer.OrdinalIgnoreCase)) {
        TraceBusBlock(busBlock);
      }
      foreach (DriverCall driverCall in driverCalls) {
        BuildMessages(driverCall);
      }
    }

    #region Treiber

    /// <summary>
    /// Sucht alle Aufrufe der Treiber-FCs tbMsg_Drv16... mit globaler Bus- und HMI-Variablen.
    /// </summary>
    List<DriverCall> FindDriverCalls() {
      var calls = new List<DriverCall>();
      var seen = new HashSet<string>();
      List<BlockEntry> drivers = _blocks.Values
        .Where(b => b.Kind == "FC" && b.Name.StartsWith(DriverPrefix, StringComparison.OrdinalIgnoreCase))
        .ToList();
      foreach (BlockEntry driver in drivers) {
        foreach (string caller in GetUsers(driver).Where(u => !u.StartsWith(DriverPrefix, StringComparison.OrdinalIgnoreCase))) {
          SourceModel model = GetBlockModel(caller);
          if (model == null) {
            continue;
          }
          foreach (SourceCall call in model.Calls.Where(c => IsCallOf(c, driver.Name))) {
            SourceOperand bus = call.Arg("IQstBus", "IQastBus");
            SourceOperand hmi = call.Arg("IQstHmi", "IQastHmi");
            if (bus == null || hmi == null || !bus.Global || !hmi.Global) {
              Warn($"{caller}: Aufruf von {driver.Name} ohne globale Bus- und HMI-Variable wird nicht ausgewertet.");
              continue;
            }
            if (seen.Add(driver.Name + "|" + bus.Key + "|" + hmi.Key)) {
              calls.Add(new DriverCall { Function = driver.Name, Caller = caller, Bus = bus, Hmi = hmi });
            }
          }
        }
      }
      return calls;
    }

    static bool IsCallOf(SourceCall call, string functionName) {
      if (string.Equals(call.CalleeTypeName, functionName, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
      return call.Callee.Global && call.Callee.Segments.Count == 1
        && string.Equals(call.Callee.Root, functionName, StringComparison.OrdinalIgnoreCase);
    }

    static string DriverKind(string function) {
      string kind = function.Substring(DriverPrefix.Length);
      return function.StartsWith(MultiDriverPrefix, StringComparison.OrdinalIgnoreCase) ? kind.Substring(1) : kind;
    }

    #endregion

    #region Busverfolgung

    /// <summary>
    /// Wertet alle Bausteine aus, die einen Datenbaustein mit einem verfolgten Meldebus verwenden.
    /// </summary>
    void TraceBusBlock(string blockName) {
      if (!_blocks.TryGetValue(blockName, out BlockEntry entry)) {
        Warn($"Baustein {blockName} des Meldebusses nicht gefunden.");
        return;
      }
      foreach (string user in GetUsers(entry)) {
        if (!user.StartsWith(DriverPrefix, StringComparison.OrdinalIgnoreCase)) {
          AnalyzeTop(user);
        }
      }
    }

    void AnalyzeTop(string blockName) {
      if (!_analyzedTop.Add(blockName) || !_blocks.TryGetValue(blockName, out BlockEntry entry)) {
        return;
      }
      if (entry.Kind != "FC" && entry.Kind != "FB" && entry.Kind != "OB") {
        return;
      }
      SourceModel model = GetBlockModel(blockName);
      if (model == null) {
        return;
      }
      var noBindings = new Dictionary<string, SourceOperand>(StringComparer.OrdinalIgnoreCase);
      if (entry.Kind == "FB") {
        List<BlockEntry> instances = _blocks.Values.Where(b => b.Kind == "InstanceDB" && string.Equals(b.InstanceOf, blockName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (instances.Count > 0) {
          foreach (BlockEntry instance in instances) {
            Analyze(model, SourceOperand.ForBlock(instance.Name), noBindings, 0);
          }
          return;
        }
      }
      Analyze(model, null, noBindings, 0);
    }

    /// <summary>
    /// Wertet die Aufrufe eines Bausteins aus. Lokale Operanden werden über die Parameterversorgung (bindings)
    /// bzw. den Instanzpfad (instancePath) in globale Operanden umgesetzt.
    /// </summary>
    void Analyze(SourceModel model, SourceOperand instancePath, Dictionary<string, SourceOperand> bindings, int depth) {
      string key = model.Name + "|" + instancePath?.Key + "|"
        + string.Join(";", bindings.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase).Select(b => b.Key.ToUpperInvariant() + "=" + b.Value.Key));
      if (!_visited.Add(key)) {
        return;
      }

      foreach (var assignment in model.Assignments) {
        SourceOperand target = assignment.Key;
        if (target.Segments.Count < 2 || !string.Equals(target.Segments[target.Segments.Count - 1].Name, ConfigMember, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
        SourceOperand targetGlobal = Substitute(target, instancePath, bindings);
        SourceOperand sourceGlobal = Substitute(assignment.Value, instancePath, bindings);
        if (targetGlobal != null && sourceGlobal != null && targetGlobal.Key != sourceGlobal.Key) {
          var copy = new Link { Element = sourceGlobal, HandlerBlock = model.Name, Variables = new List<string>() };
          copy.Pattern = BuildPattern(targetGlobal, copy.Variables);
          _configCopies.Add(copy);
        }
      }

      foreach (SourceCall call in model.Calls) {
        if (!ResolveCallee(model, call, instancePath, bindings, out string calleeName, out string calleeKind, out SourceOperand calleeInstance)) {
          continue;
        }
        if (calleeName.StartsWith(DriverPrefix, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        var args = call.Args
          .Where(a => a.Key.Length > 0)
          .Select(a => new KeyValuePair<string, SourceOperand>(a.Key, Substitute(a.Value, instancePath, bindings)))
          .ToList();

        if (calleeKind == "FC" && (calleeName.StartsWith(HandlerPrefix, StringComparison.OrdinalIgnoreCase)
            || (call.Arg("IQstMsg") != null && call.Arg("IQstBus") != null))) {
          RecordHandler(calleeName, model.Name, ArgOf(args, "IQstMsg"), ArgOf(args, "IQstBus"));
          continue;
        }

        if (depth >= MaxDepth || !args.Any(a => a.Value != null && IsTracked(a.Value))) {
          continue;
        }
        SourceModel calleeModel = GetBlockModel(calleeName);
        if (calleeModel == null) {
          continue;
        }
        var calleeBindings = new Dictionary<string, SourceOperand>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args.Where(a => a.Value != null)) {
          if (!calleeBindings.ContainsKey(arg.Key)) {
            calleeBindings[arg.Key] = arg.Value;
          }
        }
        Analyze(calleeModel, calleeKind == "FB" ? calleeInstance : null, calleeBindings, depth + 1);
      }
    }

    static SourceOperand ArgOf(List<KeyValuePair<string, SourceOperand>> args, string name) {
      foreach (var arg in args) {
        if (string.Equals(arg.Key, name, StringComparison.OrdinalIgnoreCase)) {
          return arg.Value;
        }
      }
      return null;
    }

    /// <summary>
    /// Ermittelt den aufgerufenen Baustein: FC, FB über einen Instanz-DB oder FB einer Multiinstanz.
    /// </summary>
    bool ResolveCallee(SourceModel model, SourceCall call, SourceOperand instancePath, Dictionary<string, SourceOperand> bindings,
        out string calleeName, out string calleeKind, out SourceOperand calleeInstance) {
      calleeName = null;
      calleeKind = null;
      calleeInstance = null;
      SourceOperand callee = call.Callee;

      if (callee.Global && callee.Segments.Count == 1 && callee.Segments[0].Indices.Count == 0) {
        if (_blocks.TryGetValue(callee.Root, out BlockEntry entry)) {
          if (entry.Kind == "FC") {
            calleeName = entry.Name;
            calleeKind = "FC";
            return true;
          }
          if (entry.Kind == "InstanceDB") {
            calleeName = entry.InstanceOf ?? call.CalleeTypeName;
            calleeKind = "FB";
            calleeInstance = callee;
            return calleeName != null;
          }
        }
        if (call.CalleeTypeName != null && _blocks.TryGetValue(call.CalleeTypeName, out BlockEntry typeEntry)
            && (typeEntry.Kind == "FC" || typeEntry.Kind == "FB")) {
          calleeName = typeEntry.Name;
          calleeKind = typeEntry.Kind;
          calleeInstance = typeEntry.Kind == "FB" ? callee : null;
          return true;
        }
        return false;
      }

      if (callee.Local) {
        string typeName = call.CalleeTypeName ?? model.Decl(callee.Root)?.TypeName;
        if (typeName == null || !_blocks.TryGetValue(typeName, out BlockEntry fb) || fb.Kind != "FB") {
          return false;
        }
        calleeName = fb.Name;
        calleeKind = "FB";
        calleeInstance = Substitute(callee, instancePath, bindings);
        return true;
      }
      return false;
    }

    /// <summary>
    /// Setzt einen lokalen Operanden in einen globalen um: Parameter über die Versorgung des Aufrufers, statische Variablen über den Instanzpfad.
    /// </summary>
    static SourceOperand Substitute(SourceOperand op, SourceOperand instancePath, Dictionary<string, SourceOperand> bindings) {
      if (op == null) {
        return null;
      }
      if (op.Global) {
        return op;
      }
      if (!op.Local) {
        return null;
      }
      SourceSegment first = op.Segments[0];
      if (bindings.TryGetValue(first.Name, out SourceOperand bound)) {
        SourceOperand result = bound;
        foreach (string index in first.Indices) {
          result = result.AppendIndex(index);
        }
        return result.Append(op.Segments.Skip(1));
      }
      if (instancePath != null) {
        return instancePath.Append(op.Segments);
      }
      return null;
    }

    /// <summary>
    /// Prüft, ob ein Operand einen verfolgten Meldebus enthält oder Teil davon ist.
    /// </summary>
    bool IsTracked(SourceOperand op) {
      string key = op.Key;
      foreach (SourceOperand bus in _trackedBuses) {
        string busKey = bus.Key;
        if (IsPrefix(busKey, key) || IsPrefix(key, busKey)) {
          return true;
        }
      }
      return false;
    }

    static bool IsPrefix(string prefix, string key) {
      return key == prefix || key.StartsWith(prefix + ".") || key.StartsWith(prefix + "[");
    }

    #endregion

    #region Melde-Handler

    /// <summary>
    /// Merkt sich die Zuordnung Buselement → Meldevariable eines Handler-Aufrufs.
    /// </summary>
    void RecordHandler(string handler, string handlerBlock, SourceOperand element, SourceOperand bus) {
      if (bus == null || !IsTracked(bus)) {
        return;
      }
      if (element == null || !element.Global) {
        Warn($"{handlerBlock}: Meldevariable des Handlers {handler} für Bus {bus} ist nicht auflösbar.");
        return;
      }

      bool wholeBus = bus.Segments[bus.Segments.Count - 1].Indices.Count == 0 && TypeHasMember(FindDecl(bus)?.TypeName, BusArray);
      if (wholeBus) {
        // 16er-Handler: Meldestruktur mit einem tbMsg_Element_T je Buselement in Deklarationsreihenfolge
        SourceDecl structDecl = FindDecl(element);
        List<SourceDecl> members = structDecl?.Children.Where(c => c.TypeName != null).ToList();
        if (members == null || members.Count == 0) {
          Warn($"{handlerBlock}: Struktur der Meldevariablen {element} für Handler {handler} nicht lesbar.");
          return;
        }
        int low = ArrayLow(bus.Append(BusArray), 1);
        for (int i = 0; i < members.Count; i++) {
          AddLink(bus.Append(BusArray, (low + i).ToString(CultureInfo.InvariantCulture)), element.Append(members[i].Name), handler, handlerBlock);
        }
        return;
      }
      AddLink(bus, element, handler, handlerBlock);
    }

    void AddLink(SourceOperand bus, SourceOperand element, string handler, string handlerBlock) {
      var link = new Link { Element = element, Handler = handler, HandlerBlock = handlerBlock };
      if (bus.Segments.Any(s => s.Indices.Any(i => !IsInteger(i)))) {
        link.Variables = new List<string>();
        link.Pattern = BuildPattern(bus, link.Variables);
        _patternLinks.Add(link);
        return;
      }
      if (_links.TryGetValue(bus.Key, out Link existing)) {
        if (existing.Element.Key != element.Key) {
          Warn($"Buselement {bus} wird von {existing.Element} und {element} beschrieben; verwendet wird {existing.Element}.");
        }
        return;
      }
      _links[bus.Key] = link;
    }

    /// <summary>
    /// Regulärer Ausdruck für ein Buselement mit variablem Index (z. B. Schleife über #TiIndex).
    /// </summary>
    static Regex BuildPattern(SourceOperand bus, List<string> variables) {
      var sb = new StringBuilder("^");
      for (int s = 0; s < bus.Segments.Count; s++) {
        SourceSegment segment = bus.Segments[s];
        string name = segment.Name.ToUpperInvariant();
        if (s == 0) {
          sb.Append(Regex.Escape(bus.Global ? "\"" + name + "\"" : bus.Local ? "#" + name : name));
        } else {
          sb.Append(Regex.Escape("." + name));
        }
        if (segment.Indices.Count > 0) {
          sb.Append(Regex.Escape("["));
          for (int i = 0; i < segment.Indices.Count; i++) {
            if (i > 0) {
              sb.Append(',');
            }
            string index = segment.Indices[i];
            if (IsInteger(index)) {
              sb.Append(Regex.Escape(index));
            } else {
              variables.Add(index);
              sb.Append(@"(-?\d+)");
            }
          }
          sb.Append(Regex.Escape("]"));
        }
      }
      sb.Append('$');
      return new Regex(sb.ToString());
    }

    /// <summary>
    /// Sucht die Meldevariable zu einem Buselement.
    /// </summary>
    bool TryLookup(SourceOperand bus, out Link link, out SourceOperand element) {
      if (_links.TryGetValue(bus.Key, out link)) {
        element = link.Element;
        return true;
      }
      foreach (Link pattern in _patternLinks) {
        element = MatchPattern(pattern, bus);
        if (element != null) {
          link = pattern;
          return true;
        }
      }
      link = null;
      element = null;
      return false;
    }

    /// <summary>
    /// Wendet ein Muster an und ersetzt die Indexvariablen im Ziel durch die gefundenen Indizes.
    /// </summary>
    static SourceOperand MatchPattern(Link pattern, SourceOperand op) {
      Match match = pattern.Pattern.Match(op.Key);
      if (!match.Success) {
        return null;
      }
      SourceOperand result = pattern.Element.Clone();
      for (int v = 0; v < pattern.Variables.Count; v++) {
        string value = match.Groups[v + 1].Value;
        foreach (SourceSegment segment in result.Segments) {
          for (int i = 0; i < segment.Indices.Count; i++) {
            if (string.Equals(segment.Indices[i], pattern.Variables[v], StringComparison.OrdinalIgnoreCase)) {
              segment.Indices[i] = value;
            }
          }
        }
      }
      return result;
    }

    /// <summary>
    /// Konfiguration einer Meldevariablen; wird stConfig im Code von einer anderen Meldevariablen kopiert, gilt deren Konfiguration.
    /// </summary>
    SourceOperand ResolveConfig(SourceOperand element) {
      SourceOperand config = element.Append(ConfigMember);
      for (int guard = 0; guard < 5; guard++) {
        SourceOperand source = null;
        foreach (Link copy in _configCopies) {
          source = MatchPattern(copy, config);
          if (source != null) {
            break;
          }
        }
        if (source == null || source.Key == config.Key) {
          break;
        }
        config = source;
      }
      return config;
    }

    #endregion

    #region Meldungen

    void BuildMessages(DriverCall driverCall) {
      bool multi = driverCall.Function.StartsWith(MultiDriverPrefix, StringComparison.OrdinalIgnoreCase);
      int found = 0;
      if (!multi) {
        int low = ArrayLow(driverCall.Bus.Append(BusArray), 1);
        for (int bit = 0; bit < BitsPerDriver; bit++) {
          found += Emit(driverCall, driverCall.Hmi, bit, driverCall.Bus.Append(BusArray, (low + bit).ToString(CultureInfo.InvariantCulture)));
        }
      } else {
        int[] hmiBounds = FindDecl(driverCall.Hmi)?.Bounds?.FirstOrDefault();
        int[] busBounds = FindDecl(driverCall.Bus)?.Bounds?.FirstOrDefault();
        if (hmiBounds == null || busBounds == null || hmiBounds.Contains(int.MinValue) || busBounds.Contains(int.MinValue)) {
          Warn($"{driverCall.Caller}: Array-Grenzen von {driverCall.Hmi} oder {driverCall.Bus} für {driverCall.Function} nicht lesbar.");
          return;
        }
        int hmiCount = hmiBounds[1] - hmiBounds[0] + 1;
        int blockCount = (busBounds[1] - busBounds[0] + 1) / BitsPerDriver;
        if (hmiCount < blockCount) {
          Warn($"{driverCall.Caller}: {driverCall.Hmi} hat weniger Elemente ({hmiCount}) als {driverCall.Bus} 16er-Blöcke ({blockCount}); {driverCall.Function} bearbeitet keine Meldungen.");
          return;
        }
        for (int block = 0; block < blockCount; block++) {
          SourceOperand hmi = driverCall.Hmi.AppendIndex((hmiBounds[0] + block).ToString(CultureInfo.InvariantCulture));
          for (int bit = 0; bit < BitsPerDriver; bit++) {
            int index = busBounds[0] + block * BitsPerDriver + bit;
            found += Emit(driverCall, hmi, bit, driverCall.Bus.AppendIndex(index.ToString(CultureInfo.InvariantCulture)));
          }
        }
      }
      if (found == 0) {
        Warn($"{driverCall.Caller}: Für {driverCall.Hmi} ({driverCall.Function}, Bus {driverCall.Bus}) wurde keine Meldevariable gefunden.");
      }
    }

    int Emit(DriverCall driverCall, SourceOperand hmi, int bit, SourceOperand bus) {
      if (!TryLookup(bus, out Link link, out SourceOperand element)) {
        return 0;
      }
      SourceOperand config = ResolveConfig(element);
      bool select = ParseBool(ResolveValue(config.Append("xSelect")));
      if (!select && !_includeUnselected) {
        return 1;
      }

      string typeValue = ResolveValue(config.Append("iType"));
      int? type = typeValue == null
        ? (FindDecl(config.Append("iType")) != null ? 0 : (int?)null)
        : ResolveInt(typeValue, config);
      Dictionary<string, string> texts = GetComments(element);
      _blocks.TryGetValue(element.Root, out BlockEntry elementBlock);

      _result.Messages.Add(new Info {
        DeviceName = _deviceName,
        DeviceItemName = _deviceItemName,
        PlcName = _plc.Name,
        Driver = DriverKind(driverCall.Function),
        DriverFunction = driverCall.Function,
        DriverBlock = driverCall.Caller,
        HmiVariable = hmi.ToString(),
        HmiBit = bit,
        BusElement = bus.ToString(),
        Element = element.ToString(),
        ElementBlock = element.Root,
        ElementFb = elementBlock?.Kind == "InstanceDB" ? elementBlock.InstanceOf : null,
        Handler = link.Handler,
        HandlerBlock = link.HandlerBlock,
        ConfigSource = config.Key == element.Append(ConfigMember).Key ? null : config.ToString(),
        Text = PickText(texts),
        Texts = texts,
        Select = select,
        Type = type,
        TypeConstant = typeValue != null && !IsInteger(typeValue) && type.HasValue ? typeValue : null,
        AutoQuit = ParseBool(ResolveValue(config.Append("xAutoQuit"))),
        Sync = ParseBool(ResolveValue(config.Append("xSync"))),
        DoReset = ParseBool(ResolveValue(config.Append("xDoReset"))),
        IsFault = ParseBool(ResolveValue(config.Append("xIsFault")))
      });
      return 1;
    }

    static string PickText(Dictionary<string, string> texts) {
      if (texts == null || texts.Count == 0) {
        return null;
      }
      if (texts.TryGetValue("de-DE", out string german) && !string.IsNullOrWhiteSpace(german)) {
        return german;
      }
      return texts.Values.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
    }

    #endregion

    #region Deklarationen und Startwerte

    /// <summary>
    /// Liefert die Deklaration eines globalen Operanden (Member eines DBs, Instanz-DBs, FBs oder UDTs).
    /// </summary>
    SourceDecl FindDecl(SourceOperand op) {
      if (op == null || !op.Global || op.Segments.Count < 2) {
        return null;
      }
      SourceModel model = GetBlockModel(op.Root);
      return model == null ? null : FindIn(model.Decls, model.InstanceOf, op.Segments, 1);
    }

    SourceDecl FindIn(List<SourceDecl> decls, string ownerType, List<SourceSegment> segments, int i) {
      SourceDecl decl = Member(decls, segments[i].Name);
      bool last = i == segments.Count - 1;
      if (decl != null) {
        if (last && decl.TypeName != null) {
          return decl;
        }
        if (!last) {
          SourceDecl inner = FindIn(decl.Children, decl.TypeName, segments, i + 1);
          if (inner != null) {
            return inner;
          }
        }
      }
      List<SourceDecl> typeDecls = TypeDecls(ownerType);
      if (typeDecls != null && !ReferenceEquals(typeDecls, decls)) {
        SourceDecl fromType = FindIn(typeDecls, null, segments, i);
        if (fromType != null) {
          return fromType;
        }
      }
      return last ? decl : null;
    }

    /// <summary>
    /// Kommentare der Meldevariablen; bei Instanz-DBs ohne eigene Kommentare aus dem FB.
    /// </summary>
    Dictionary<string, string> GetComments(SourceOperand op) {
      SourceDecl decl = FindDecl(op);
      if (decl?.Comments != null && decl.Comments.Count > 0) {
        return decl.Comments;
      }
      SourceModel model = GetBlockModel(op.Root);
      List<SourceDecl> typeDecls = TypeDecls(model?.InstanceOf);
      if (typeDecls != null) {
        decl = FindIn(typeDecls, null, op.Segments, 1);
        if (decl?.Comments != null && decl.Comments.Count > 0) {
          return decl.Comments;
        }
      }
      return null;
    }

    /// <summary>
    /// Startwert eines globalen Operanden: Wert im DB bzw. Instanz-DB, sonst Vorbelegung im FB bzw. Struktur, sonst Startwert im UDT.
    /// </summary>
    string ResolveValue(SourceOperand op) {
      if (op == null || !op.Global || op.Segments.Count < 2) {
        return null;
      }
      SourceModel model = GetBlockModel(op.Root);
      if (model == null) {
        return null;
      }
      if (model.Values.TryGetValue(op.MemberPath, out string value)) {
        return value;
      }
      return ResolveIn(model.Decls, model.InstanceOf, op.Segments, 1);
    }

    string ResolveIn(List<SourceDecl> decls, string ownerType, List<SourceSegment> segments, int i) {
      SourceDecl decl = Member(decls, segments[i].Name);
      bool last = i == segments.Count - 1;
      if (decl != null) {
        if (last) {
          if (decl.StartValue != null) {
            return decl.StartValue;
          }
        } else {
          string inner = ResolveIn(decl.Children, decl.TypeName, segments, i + 1);
          if (inner != null) {
            return inner;
          }
        }
      }
      List<SourceDecl> typeDecls = TypeDecls(ownerType);
      if (typeDecls != null && !ReferenceEquals(typeDecls, decls)) {
        return ResolveIn(typeDecls, null, segments, i);
      }
      return null;
    }

    static SourceDecl Member(List<SourceDecl> decls, string name) {
      return decls?.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Deklarationen eines Datentyps: Schnittstelle eines FBs (Multiinstanz) oder Member eines UDTs.
    /// </summary>
    List<SourceDecl> TypeDecls(string typeName) {
      if (string.IsNullOrEmpty(typeName) || typeName.Equals("Struct", StringComparison.OrdinalIgnoreCase)) {
        return null;
      }
      if (_blocks.TryGetValue(typeName, out BlockEntry entry)) {
        return entry.Kind == "FB" ? GetBlockModel(typeName)?.Decls : null;
      }
      return GetTypeModel(typeName)?.Decls;
    }

    bool TypeHasMember(string typeName, string memberName) {
      return Member(TypeDecls(typeName), memberName) != null;
    }

    int ArrayLow(SourceOperand arrayOperand, int fallback) {
      int[] bounds = FindDecl(arrayOperand)?.Bounds?.FirstOrDefault();
      return bounds != null && bounds[0] != int.MinValue ? bounds[0] : fallback;
    }

    /// <summary>
    /// Wandelt einen Startwert in eine Zahl; Konstanten werden über die FB-Konstanten und die PLC-Anwenderkonstanten aufgelöst.
    /// </summary>
    int? ResolveInt(string value, SourceOperand element) {
      for (int guard = 0; guard < 5 && value != null; guard++) {
        if (TryParseInt(value, out int number)) {
          return number;
        }
        string constantValue = null;
        SourceModel model = GetBlockModel(element.Root);
        List<SourceDecl> fbDecls = TypeDecls(model?.InstanceOf);
        SourceDecl localConstant = fbDecls?.FirstOrDefault(d => d.Section == "Constant" && string.Equals(d.Name, value, StringComparison.OrdinalIgnoreCase));
        if (localConstant != null) {
          constantValue = localConstant.StartValue;
        } else {
          LoadConstants();
          _constants.TryGetValue(value, out constantValue);
        }
        if (constantValue == null) {
          Warn($"Konstante {value} nicht gefunden.");
          return null;
        }
        value = cSourceParser.NormalizeValue(constantValue);
      }
      return null;
    }

    static bool TryParseInt(string value, out int number) {
      string text = value.Trim();
      int hash = text.IndexOf('#');
      if (hash >= 0) {
        string prefix = text.Substring(0, hash).ToUpperInvariant();
        string digits = text.Substring(hash + 1).Replace("_", "");
        int radix = prefix.EndsWith("16") ? 16 : prefix.EndsWith("8") ? 8 : prefix.EndsWith("2") ? 2 : 10;
        if (radix == 10 && digits.Contains("#")) {
          return TryParseInt(digits, out number);
        }
        try {
          number = Convert.ToInt32(digits, radix);
          return true;
        } catch {
          number = 0;
          return false;
        }
      }
      return int.TryParse(text.Replace("_", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    static bool IsInteger(string value) {
      return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    static bool ParseBool(string value) {
      if (string.IsNullOrWhiteSpace(value)) {
        return false;
      }
      string text = value.Trim();
      int hash = text.IndexOf('#');
      if (hash >= 0) {
        text = text.Substring(hash + 1);
      }
      return text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || text == "1";
    }

    #endregion

    #region Openness-Zugriffe

    void IndexBlocks(PlcBlockGroup group) {
      foreach (PlcBlock block in group.Blocks) {
        var entry = new BlockEntry { Block = block, Name = block.Name, Kind = block.GetType().Name };
        if (block is InstanceDB instanceDb) {
          entry.InstanceOf = instanceDb.InstanceOfName;
        }
        _blocks[block.Name] = entry;
      }
      foreach (PlcBlockUserGroup userGroup in group.Groups) {
        IndexBlocks(userGroup);
      }
    }

    void IndexTypes(PlcTypeGroup group) {
      foreach (PlcType type in group.Types) {
        _types[type.Name] = type;
      }
      foreach (PlcTypeUserGroup userGroup in group.Groups) {
        IndexTypes(userGroup);
      }
    }

    void LoadConstants() {
      if (_constants != null) {
        return;
      }
      _constants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      LoadConstants(_plc.TagTableGroup);
    }

    void LoadConstants(PlcTagTableGroup group) {
      foreach (PlcTagTable table in group.TagTables) {
        foreach (PlcUserConstant constant in table.UserConstants) {
          _constants[constant.Name] = constant.Value;
        }
      }
      foreach (PlcTagTableUserGroup userGroup in group.Groups) {
        LoadConstants(userGroup);
      }
    }

    /// <summary>
    /// Namen der Bausteine, die den Baustein verwenden (Querverweise "UsedBy").
    /// </summary>
    HashSet<string> GetUsers(BlockEntry entry) {
      if (_users.TryGetValue(entry.Name, out HashSet<string> users)) {
        return users;
      }
      users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      try {
        CrossReferenceService service = entry.Block.GetService<CrossReferenceService>();
        CrossReferenceResult result = service?.GetCrossReferences(CrossReferenceFilter.AllObjects);
        if (result?.Sources != null) {
          foreach (SourceObject source in result.Sources) {
            CollectUsers(source, users);
          }
        }
      } catch (Exception ex) {
        Warn($"Querverweise von {entry.Name} nicht lesbar: {ex.Message}");
      }
      users.Remove(entry.Name);
      users.RemoveWhere(u => !_blocks.ContainsKey(u));
      _users[entry.Name] = users;
      return users;
    }

    static void CollectUsers(SourceObject source, HashSet<string> users) {
      if (source.References != null) {
        foreach (ReferenceObject reference in source.References) {
          if (reference.Locations != null && reference.Locations.Any(l => l.ReferenceType == ReferenceType.UsedBy)) {
            users.Add(reference.Name.Trim('"'));
          }
        }
      }
      if (source.Children != null) {
        foreach (SourceObject child in source.Children) {
          CollectUsers(child, users);
        }
      }
    }

    SourceModel GetBlockModel(string name) {
      if (string.IsNullOrEmpty(name)) {
        return null;
      }
      string key = "B:" + name;
      if (_models.TryGetValue(key, out SourceModel model)) {
        return model;
      }
      model = null;
      if (_blocks.TryGetValue(name, out BlockEntry entry)) {
        string error = cTiaBlocks.Export(entry.Block, entry.Name, out string content, out string multiLingualText, out string format);
        if (error != null) {
          Warn($"Baustein {name}: {error}");
        } else {
          model = ParseModel(name, content, multiLingualText, format == cTiaRequestHelpers.FormatXml);
        }
      }
      _models[key] = model;
      return model;
    }

    SourceModel GetTypeModel(string name) {
      string key = "T:" + name;
      if (_models.TryGetValue(key, out SourceModel model)) {
        return model;
      }
      model = null;
      if (_types == null) {
        _types = new Dictionary<string, PlcType>(StringComparer.OrdinalIgnoreCase);
        IndexTypes(_plc.TypeGroup);
      }
      if (_types.TryGetValue(name, out PlcType type)) {
        string error = cTiaUDTs.ExportAsDocuments(type, name, out string content, out string multiLingualText);
        if (error == null) {
          model = ParseModel(name, content, multiLingualText, false);
        } else {
          error = ExportTypeAsXml(type, name, out content);
          if (error == null) {
            model = ParseModel(name, content, null, true);
          } else {
            Warn($"Datentyp {name}: {error}");
          }
        }
      }
      _models[key] = model;
      return model;
    }

    SourceModel ParseModel(string name, string content, string multiLingualText, bool xml) {
      try {
        SourceModel model = xml ? cSourceParser.ParseXml(content) : cSourceParser.ParseSd(content, multiLingualText);
        if (string.IsNullOrEmpty(model.Name)) {
          model.Name = name;
        }
        return model;
      } catch (Exception ex) {
        Warn($"{name}: Export nicht lesbar: {ex.Message}");
        return null;
      }
    }

    static string ExportTypeAsXml(PlcType type, string typeName, out string content) {
      content = null;
      string safeName = cTiaExportHelpers.SanitizeFileName(typeName);
      DirectoryInfo exportDir = cTiaExportHelpers.EnsureExportDirectory();
      try {
        cTiaExportHelpers.TryDeleteExportedDocument(exportDir.FullName, safeName);
        var xmlFile = new FileInfo(Path.Combine(exportDir.FullName, safeName + ".xml"));
        type.Export(xmlFile, ExportOptions.WithDefaults);
        xmlFile.Refresh();
        if (!xmlFile.Exists) {
          return "XML export file not found.";
        }
        content = File.ReadAllText(xmlFile.FullName);
        return null;
      } catch (Exception ex) {
        return ex.Message;
      }
    }

    void Warn(string message) {
      string text = $"{_plc.Name}: {message}";
      if (_warnings.Add(text)) {
        _result.Warnings.Add(text);
      }
    }

    #endregion
  }
}
