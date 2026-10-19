using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiLogging;
using Siemens.Engineering.HmiUnified.HmiLogging.HmiLoggingCommon;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.HMI;
using static Tophinke.TiaOpenness.Tool.TiaREST.HMI.cTiaHmi;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.HMI {
  /// <summary>
  /// Daten- und Meldearchive von WinCC Unified-Geräten.
  /// </summary>
  static internal class cTiaHmiLogs {
    const string Data = "Data";
    const string Alarm = "Alarm";

    /// <summary>
    /// Listet die Daten- und Meldearchive eines Unified-Geräts.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional, wenn nur eines vorhanden ist</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <returns>JSON-String mit ListResult von LogInfo oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.LogsList)]
    static public string List(HttpListenerContext context) {
      return ExecuteOnHmi(context, (project, target) => {
        var items = target.Software.DataLogs.Select(l => ToInfo(l, Data))
          .Concat(target.Software.AlarmLogs.Select(l => ToInfo(l, Alarm)))
          .ToList();
        return ListResponse(context, target, items);
      });
    }

    /// <summary>
    /// Legt Daten- oder Meldearchive an oder ändert sie (Felder mit null bleiben unverändert).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>LogPutRequest</body>
    /// <returns>JSON-String mit ChangeResult oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.LogsPut, "PUT")]
    static public string Put(HttpListenerContext context) {
      LogPutRequest request = ReadRequest<LogPutRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.Logs == null || request.Logs.Count == 0 || request.Logs.Any(l => string.IsNullOrWhiteSpace(l?.Name))) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Logs must contain at least one log and every log needs a Name.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        var changes = request.Logs.Select(info => Upsert(target.Software, info)).ToList();
        return ChangeResponse(context, target, changes);
      });
    }

    /// <summary>
    /// Löscht Daten- oder Meldearchive über ihren Namen.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts (DeviceName aus /api/hmi/devices/list); optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements (DeviceItemName aus /api/hmi/devices/list); optional</query>
    /// <body>DeleteRequest</body>
    /// <returns>JSON-String mit ChangeResult oder Fehlermeldung</returns>
    [RestRoute(HmiRoutes.LogsDelete, "POST")]
    static public string Delete(HttpListenerContext context) {
      DeleteRequest request = ReadRequest<DeleteRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.Names == null || request.Names.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Names must contain at least one name.");
      }
      return ExecuteOnHmi(context, (project, target) => {
        HmiSoftware hmi = target.Software;
        var changes = request.Names.Select(name => hmi.AlarmLogs.Find(name) != null
          ? DeleteObject("AlarmLog", name, () => hmi.AlarmLogs.Find(name), l => l.Delete())
          : DeleteObject("DataLog", name, () => hmi.DataLogs.Find(name), l => l.Delete())).ToList();
        return ChangeResponse(context, target, changes);
      });
    }

    static LogInfo ToInfo(LoggingBase log, string kind) {
      return new LogInfo {
        Kind = kind,
        Name = log.Name,
        StorageDevice = Safe(() => log.Settings.StorageDevice.ToString()),
        StorageFolder = NullIfEmpty(Safe(() => log.Settings.StorageFolder)),
        LogMaxSize = Safe(() => (uint?)log.Settings.LogMaxSize),
        LogTimePeriod = Safe(() => DurationText(log.Settings.LogTimePeriod.Days, log.Settings.LogTimePeriod.Hours, log.Settings.LogTimePeriod.Minutes, log.Settings.LogTimePeriod.Seconds)),
        SegmentMaxSize = Safe(() => (uint?)log.Segment.SegmentMaxSize),
        SegmentTimePeriod = Safe(() => DurationText(log.Segment.SegmentTimePeriod.Days, log.Segment.SegmentTimePeriod.Hours, log.Segment.SegmentTimePeriod.Minutes, log.Segment.SegmentTimePeriod.Seconds)),
        BackupMode = Safe(() => log.Backup.BackupMode.ToString()),
        BackupPath = NullIfEmpty(Safe(() => log.Backup.PrimaryPath))
      };
    }

    static Change Upsert(HmiSoftware hmi, LogInfo info) {
      LoggingBase log = (LoggingBase)hmi.DataLogs.Find(info.Name) ?? hmi.AlarmLogs.Find(info.Name);
      string kind = log is HmiAlarmLog ? Alarm : log != null ? Data : string.IsNullOrEmpty(info.Kind) ? Data : info.Kind;
      bool isAlarm = string.Equals(kind, Alarm, StringComparison.OrdinalIgnoreCase);
      string changeKind = isAlarm ? "AlarmLog" : "DataLog";
      if (!isAlarm && !string.Equals(kind, Data, StringComparison.OrdinalIgnoreCase)) {
        return new Change { Kind = "Log", Name = info.Name, Action = "Created", Error = "Kind must be 'Data' or 'Alarm'." };
      }
      if (log != null && !string.IsNullOrEmpty(info.Kind) && !string.Equals(info.Kind, kind, StringComparison.OrdinalIgnoreCase)) {
        return new Change { Kind = changeKind, Name = info.Name, Action = "Updated", Error = $"Log {info.Name} already exists as {kind} log." };
      }
      bool created = log == null;
      if (created) {
        try {
          log = isAlarm ? (LoggingBase)hmi.AlarmLogs.Create(info.Name) : hmi.DataLogs.Create(info.Name);
        } catch (Exception ex) {
          return new Change { Kind = changeKind, Name = info.Name, Action = "Created", Error = ex.Message };
        }
      }
      return Apply(changeKind, info.Name, created, tracker => {
        LogSettings settings = log.Settings;
        LogSegment segment = log.Segment;
        LogBackup backup = log.Backup;
        tracker.Set("StorageDevice", info.StorageDevice, () => settings.StorageDevice.ToString(), v => settings.StorageDevice = ParseEnum<DeviceNode>(v, "StorageDevice"));
        tracker.Set("StorageFolder", info.StorageFolder, () => settings.StorageFolder ?? "", v => settings.StorageFolder = v);
        tracker.Set("LogMaxSize", info.LogMaxSize?.ToString(), () => settings.LogMaxSize.ToString(), v => settings.LogMaxSize = uint.Parse(v));
        Func<string> logPeriod = () => DurationText(settings.LogTimePeriod.Days, settings.LogTimePeriod.Hours, settings.LogTimePeriod.Minutes, settings.LogTimePeriod.Seconds);
        tracker.Set("LogTimePeriod", NormalizeDuration(info.LogTimePeriod), logPeriod, v => {
          TimeSpan d = ParseDuration(v);
          CheckDuration(settings.LogTimePeriod.SetLogDuration((uint)d.Days, (uint)d.Hours, (uint)d.Minutes, (uint)d.Seconds, 0), logPeriod(), v);
        });
        tracker.Set("SegmentMaxSize", info.SegmentMaxSize?.ToString(), () => segment.SegmentMaxSize.ToString(), v => segment.SegmentMaxSize = uint.Parse(v));
        Func<string> segmentPeriod = () => DurationText(segment.SegmentTimePeriod.Days, segment.SegmentTimePeriod.Hours, segment.SegmentTimePeriod.Minutes, segment.SegmentTimePeriod.Seconds);
        tracker.Set("SegmentTimePeriod", NormalizeDuration(info.SegmentTimePeriod), segmentPeriod, v => {
          TimeSpan d = ParseDuration(v);
          CheckDuration(segment.SegmentTimePeriod.SetSegmentDuration((uint)d.Days, (uint)d.Hours, (uint)d.Minutes, (uint)d.Seconds, 0), segmentPeriod(), v);
        });
        tracker.Set("BackupMode", info.BackupMode, () => backup.BackupMode.ToString(), v => backup.BackupMode = ParseEnum<HmiBackupMode>(v, "BackupMode"));
        tracker.Set("BackupPath", info.BackupPath, () => backup.PrimaryPath ?? "", v => backup.PrimaryPath = v);
      });
    }

    static string DurationText(uint days, uint hours, uint minutes, uint seconds) {
      return new TimeSpan((int)days, (int)hours, (int)minutes, (int)seconds).ToString("c", CultureInfo.InvariantCulture);
    }

    static TimeSpan ParseDuration(string text) {
      if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan duration) || duration < TimeSpan.Zero) {
        throw new ArgumentException($"Invalid duration '{text}', expected d.hh:mm:ss.");
      }
      return duration;
    }

    static string NormalizeDuration(string text) {
      return text == null ? null : ParseDuration(text).ToString("c", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// SetLogDuration/SetSegmentDuration melden Fehler als Rückgabetext statt als Ausnahme; geprüft wird daher der gelesene Wert.
    /// </summary>
    static void CheckDuration(string result, string actual, string expected) {
      if (actual != expected) {
        throw new InvalidOperationException(string.IsNullOrEmpty(result) ? $"Duration {expected} was not accepted." : result);
      }
    }
  }
}
