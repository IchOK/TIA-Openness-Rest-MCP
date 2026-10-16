using System;

namespace Tophinke.TiaOpenness.Tool.Consts
{
  /// <summary>
  /// Protokoll-Konstanten der REST-API. Ports, API-Key und Versionen stehen in der appsettings.json der jeweiligen App.
  /// </summary>
  public static class Network
  {
    public const string TiaRestApiKeyHeader = "X-API-Key";
    public const string RouteHealth = "/api/health";
    public const string RouteHelp = "/help";
  }

  // Routen-Schema: /api/<bereich>/<funktion>; der MCP-Toolname ist derselbe Pfad mit "tia" statt "/api"
  // und "_" statt "/" bzw. "-" (z. B. /api/plc/blocks/get-file = tia_plc_blocks_get_file).

  /// <summary>
  /// Konstanten für die Projekt-Routen.
  /// </summary>
  public static class ProjectRoutes
  {
    public const string List = "/api/projects/list";
    public const string Open = "/api/projects/open";
    public const string Save = "/api/projects/save";
    public const string Close = "/api/projects/close";
    public const string GraphicsList = "/api/project/graphics/list";
    public const string GraphicsCleanupDuplicates = "/api/project/graphics/cleanup-duplicates";
  }

  /// <summary>
  /// Konstanten für die PLC-Geräte-Routen.
  /// </summary>
  public static class PlcRoutes {
    public const string DevicesList = "/api/plc/devices/list";
  }

  /// <summary>
  /// Konstanten für die Block-Routen (DB, FB, FC, OB, …).
  /// </summary>
  public static class BlockRoutes {
    public const string List = "/api/plc/blocks/list";
    public const string Get = "/api/plc/blocks/get";
    public const string GetFile = "/api/plc/blocks/get-file";
    public const string Put = "/api/plc/blocks/put";
    public const string PutFile = "/api/plc/blocks/put-file";
    public const string GetDir = "/api/plc/blocks/get-dir";
    public const string PutDir = "/api/plc/blocks/put-dir";
    public const string GetFileList = "/api/plc/blocks/get-file-list";
    public const string PutFileList = "/api/plc/blocks/put-file-list";
    public const string Patch = "/api/plc/blocks/patch";
    public const string DiscardTypeVersion = "/api/plc/blocks/discard-type-version";
    public const string ListExportCapabilities = "/api/plc/blocks/list-export-capabilities";
  }

  /// <summary>
  /// Konstanten für die UDT-Routen.
  /// </summary>
  public static class UDTRoutes {
    public const string List = "/api/plc/udts/list";
    public const string Get = "/api/plc/udts/get";
    public const string Put = "/api/plc/udts/put";
    public const string GetFile = "/api/plc/udts/get-file";
    public const string PutFile = "/api/plc/udts/put-file";
    public const string GetDir = "/api/plc/udts/get-dir";
    public const string PutDir = "/api/plc/udts/put-dir";
    public const string GetFileList = "/api/plc/udts/get-file-list";
    public const string PutFileList = "/api/plc/udts/put-file-list";
    public const string DiscardTypeVersion = "/api/plc/udts/discard-type-version";
  }

  /// <summary>
  /// Konstanten für die Übersetzen-Routen.
  /// </summary>
  public static class CompileRoutes {
    public const string Item = "/api/plc/compile/item";
    public const string Plc = "/api/plc/compile/plc";
  }

  /// <summary>
  /// Konstanten für die Querverweis-Routen.
  /// </summary>
  public static class XRefRoutes {
    public const string Get = "/api/plc/xrefs/get";
  }

  /// <summary>
  /// Konstanten für die Variablentabellen-Routen.
  /// </summary>
  public static class TagTableRoutes {
    public const string List = "/api/plc/tagtables/list";
    public const string Get = "/api/plc/tagtables/get";
    public const string Put = "/api/plc/tagtables/put";
    public const string Patch = "/api/plc/tagtables/patch";
    public const string GetFile = "/api/plc/tagtables/get-file";
    public const string PutFile = "/api/plc/tagtables/put-file";
    public const string GetDir = "/api/plc/tagtables/get-dir";
    public const string PutDir = "/api/plc/tagtables/put-dir";
    public const string GetFileList = "/api/plc/tagtables/get-file-list";
    public const string PutFileList = "/api/plc/tagtables/put-file-list";
  }

  /// <summary>
  /// Konstanten für die WinCC Unified-HMI-Routen.
  /// </summary>
  public static class HmiRoutes {
    public const string DevicesList = "/api/hmi/devices/list";
    public const string TagsList = "/api/hmi/tags/list";
    public const string TagsPut = "/api/hmi/tags/put";
    public const string TagsDelete = "/api/hmi/tags/delete";
    public const string TagsGetFile = "/api/hmi/tags/get-file";
    public const string TagsPutFile = "/api/hmi/tags/put-file";
    public const string AlarmsList = "/api/hmi/alarms/list";
    public const string AlarmsPut = "/api/hmi/alarms/put";
    public const string AlarmsDelete = "/api/hmi/alarms/delete";
    public const string AlarmClassesList = "/api/hmi/alarmclasses/list";
    public const string AlarmClassesPut = "/api/hmi/alarmclasses/put";
    public const string AlarmClassesDelete = "/api/hmi/alarmclasses/delete";
    public const string LogsList = "/api/hmi/logs/list";
    public const string LogsPut = "/api/hmi/logs/put";
    public const string LogsDelete = "/api/hmi/logs/delete";
  }

  /// <summary>
  /// Konstanten für die Melde-/Message-Routen.
  /// </summary>
  public static class MessageRoutes {
    public const string List = "/api/topctrl/plc/messages/list";
    public const string GetFile = "/api/topctrl/plc/messages/get-file";
    public const string HmiSync = "/api/topctrl/hmi/messages/sync";
  }
}
