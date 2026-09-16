using System;

namespace Tophinke.TiaOpenness.Tool.Consts
{
  /// <summary>
  /// Konstanten für Netzwerk- und API-Konfigurationen.
  /// </summary>
  public static class Network
  {
    /// <summary>
    /// Port auf den die TIA REST API lauscht. Standardmäßig 6280.
    /// </summary>
    public const int TiaRestPort = 6280;
    public const int TiaMcpPort = 6281;
    public const string TiaRestApiKeyHeader = "X-API-Key";
    public const string TiaRestApiKeyDefault = "TiaOpennessDefaultToken";
    public const string TiaVersionDefault = "20";
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
  /// Konstanten für die DB-Routen.
  /// </summary>
  public static class DBRoutes
  {
    public const string List = "/api/dbs";
    public const string Get = "/api/dbs/get";
  }

  /// <summary>
  /// Konstanten für die UDT-Routen.
  /// </summary>
  public static class UDTRoutes {
    public const string List = "/api/udts";
    public const string Get = "/api/udts/get";
  }

  /// <summary>
  /// Konstanten für die FC-Routen.
  /// </summary>
  public static class FCRoutes {
    public const string List = "/api/fcs";
    public const string Get = "/api/fcs/get";
  }

  /// <summary>
  /// Konstanten für die FB-Routen.
  /// </summary>
  public static class FBRoutes {
    public const string List = "/api/fbs";
    public const string Get = "/api/fbs/get";
  }

  /// <summary>
  /// Konstanten für die OB-Routen.
  /// </summary>
  public static class OBRoutes {
    public const string List = "/api/obs";
    public const string Get = "/api/obs/get";
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
  }
}
