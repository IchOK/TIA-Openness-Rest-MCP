using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Tophinke.TiaOpenness.Tool.Consts;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Kennzeichnet eine Methode string Handler(HttpListenerContext) als REST-Endpunkt.
  /// Routing und /help werden beim Start per Reflection aus allen so markierten Methoden aufgebaut.
  /// </summary>
  [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
  internal sealed class RestRouteAttribute : Attribute {
    public RestRouteAttribute(string path, string method = "GET") {
      Path = path;
      Method = method.ToUpperInvariant();
    }

    public string Path { get; }
    public string Method { get; }

    /// <summary>
    /// true = ohne API-Key erreichbar.
    /// </summary>
    public bool Anonymous { get; set; }
  }

  /// <summary>
  /// Ein registrierter REST-Endpunkt.
  /// </summary>
  internal sealed class RestRoute {
    public string Path { get; set; }
    public string Method { get; set; }
    public bool Anonymous { get; set; }
    public MethodInfo Handler { get; set; }
    public Func<HttpListenerContext, string> Invoke { get; set; }
  }

  /// <summary>
  /// Routing-Tabelle der REST-API, aufgebaut aus den [RestRoute]-Attributen.
  /// </summary>
  static internal class RestRouter {
    static private Dictionary<string, RestRoute> _routes;

    static public IReadOnlyCollection<RestRoute> Routes => _routes.Values;

    /// <summary>
    /// Sucht alle [RestRoute]-Methoden der Assembly und baut die Routing-Tabelle.
    /// </summary>
    /// <exception cref="InvalidOperationException">Bei falscher Handler-Signatur oder doppelt vergebenem Pfad</exception>
    static public void Build() {
      var routes = new Dictionary<string, RestRoute>(StringComparer.OrdinalIgnoreCase);
      const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
      foreach (Type type in Assembly.GetExecutingAssembly().GetTypes()) {
        foreach (MethodInfo method in type.GetMethods(flags)) {
          var attribute = method.GetCustomAttribute<RestRouteAttribute>();
          if (attribute == null) {
            continue;
          }
          ParameterInfo[] parameters = method.GetParameters();
          if (method.ReturnType != typeof(string) || parameters.Length != 1 || parameters[0].ParameterType != typeof(HttpListenerContext)) {
            throw new InvalidOperationException($"Route {attribute.Path}: handler {type.Name}.{method.Name} must have the signature static string (HttpListenerContext).");
          }
          if (routes.TryGetValue(attribute.Path, out RestRoute existing)) {
            throw new InvalidOperationException($"Route {attribute.Path} is defined twice: {existing.Handler.DeclaringType.Name}.{existing.Handler.Name} and {type.Name}.{method.Name}.");
          }
          routes[attribute.Path] = new RestRoute {
            Path = attribute.Path,
            Method = attribute.Method,
            Anonymous = attribute.Anonymous,
            Handler = method,
            Invoke = (Func<HttpListenerContext, string>)Delegate.CreateDelegate(typeof(Func<HttpListenerContext, string>), method)
          };
        }
      }
      _routes = routes;
    }

    /// <summary>
    /// Bearbeitet eine Anfrage: API-Key prüfen, Route suchen, HTTP-Methode prüfen, Handler aufrufen.
    /// </summary>
    /// <returns>Antworttext; Statuscode und ContentType sind gesetzt</returns>
    static public string Dispatch(HttpListenerContext context, string apiKey) {
      string path = context.Request.Url.LocalPath;
      _routes.TryGetValue(path, out RestRoute route);

      if (route == null || !route.Anonymous) {
        if (context.Request.Headers[Network.TiaRestApiKeyHeader] != apiKey) {
          return ErrorResponse(context, HttpStatusCode.Unauthorized, $"Error: Unauthorized access. Invalid or missing {Network.TiaRestApiKeyHeader}.");
        }
      }
      if (route == null) {
        return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Unknown route: {path}. See {Network.RouteHelp} for all endpoints.");
      }
      if (!string.Equals(context.Request.HttpMethod, route.Method, StringComparison.OrdinalIgnoreCase)) {
        context.Response.AddHeader("Allow", route.Method);
        return ErrorResponse(context, HttpStatusCode.MethodNotAllowed, $"Error: Route {route.Path} requires HTTP {route.Method}.");
      }
      return route.Invoke(context);
    }
  }
}
