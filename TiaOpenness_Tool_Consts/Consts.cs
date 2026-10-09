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
  }

  /// <summary>
  /// Konstanten für die Projekt-Routen.
  /// </summary>
  public static class ProjectRoutes
  {
    public const string List = "/api/projects";
  }

  /// <summary>
  /// Konstanten für die Block-Routen (DB, FB, FC, OB, …).
  /// </summary>
  public static class BlockRoutes {
    public const string List = "/api/blocks";
    public const string Get = "/api/blocks/get";
    public const string GetFile = "/api/blocks/getfile";
    public const string Put = "/api/blocks/put";
    public const string PutFile = "/api/blocks/putfile";
    public const string Patch = "/api/blocks/patch";
    public const string DiscardTypeVersion = "/api/blocks/discard-type-version";
    public const string ExportCapabilities = "/api/blocks/export-capabilities";
  }

  /// <summary>
  /// Konstanten für die UDT-Routen.
  /// </summary>
  public static class UDTRoutes {
    public const string List = "/api/udts";
    public const string Get = "/api/udts/get";
    public const string Put = "/api/udts/put";
  }

  /// <summary>
  /// Konstanten für die Übersetzen-Routen.
  /// </summary>
  public static class CompileRoutes {
    public const string Item = "/api/compile/item";
    public const string Plc = "/api/compile/plc";
  }

  /// <summary>
  /// Konstanten für die Querverweis-Routen.
  /// </summary>
  public static class XRefRoutes {
    public const string Get = "/api/xrefs";
  }

  /// <summary>
  /// Konstanten für die Variablentabellen-Routen.
  /// </summary>
  public static class TagTableRoutes {
    public const string List = "/api/tagtables";
    public const string Get = "/api/tagtables/get";
    public const string Put = "/api/tagtables/put";
    public const string Patch = "/api/tagtables/patch";
  }

  /// <summary>
  /// Konstanten für die Melde-/Message-Routen.
  /// </summary>
  public static class MessageRoutes {
    public const string List = "/api/messages";
  }
}
