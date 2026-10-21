using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Beschreibung der REST-API (Ergebnis von /help).
  /// </summary>
  internal sealed class HelpDocument {
    public string Service { get; set; }
    public string TiaVersion { get; set; }
    public string ApiKeyHeader { get; set; }
    public string Note { get; set; }
    public List<HelpEndpoint> Endpoints { get; set; }
  }

  internal sealed class HelpEndpoint {
    public string Group { get; set; }
    public string Method { get; set; }
    public string Path { get; set; }
    public bool ApiKeyRequired { get; set; }
    public string Summary { get; set; }
    public List<HelpParameter> Query { get; set; }
    /// <summary>Strukturierte Body-Felder aus &lt;body name="…"&gt; (wie Query).</summary>
    public List<HelpParameter> BodyParams { get; set; }
    /// <summary>Freier Body-Hinweis aus &lt;body&gt; ohne name (Fallback, wenn keine BodyParams).</summary>
    public string Body { get; set; }
    public string Returns { get; set; }
  }

  internal sealed class HelpParameter {
    public string Name { get; set; }
    public string Description { get; set; }
    /// <summary>Optional: string (Standard), number, boolean, string[], json.</summary>
    public string Type { get; set; }
  }

  /// <summary>
  /// Allgemeine Endpunkte der REST-API (Status und Beschreibung).
  /// </summary>
  static internal class cTiaRestInfo {
    static private Dictionary<string, XElement> _docMembers;
    static private string _docError;

    /// <summary>
    /// Prüft, ob die REST-API läuft, und meldet die verwendete TIA-Portal-Version. Benötigt keinen API-Key.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <returns>Text "API is running on version &lt;TIA-Version&gt;"</returns>
    [RestRoute(Network.RouteHealth, Anonymous = true)]
    static public string Health(HttpListenerContext context) {
      context.Response.StatusCode = (int)HttpStatusCode.OK;
      context.Response.ContentType = "text/plain; charset=utf-8";
      return $"API is running on version {RestSettings.Current.TiaVersion}";
    }

    /// <summary>
    /// Listet alle Endpunkte der REST-API mit HTTP-Methode, Beschreibung, Query-Parametern, Request-Body und Rückgabe.
    /// Die Liste wird aus den Routen-Attributen und den Doku-Kommentaren der Handler erzeugt. Benötigt keinen API-Key.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="format">Optional: "html" für eine lesbare Seite im Browser; ohne Angabe JSON</query>
    /// <returns>HelpDocument als JSON oder HTML-Seite</returns>
    [RestRoute(Network.RouteHelp, Anonymous = true)]
    static public string Help(HttpListenerContext context) {
      HelpDocument help = BuildHelp();
      string format = context.Request.QueryString["format"];
      context.Response.StatusCode = (int)HttpStatusCode.OK;
      if (string.Equals(format, "html", StringComparison.OrdinalIgnoreCase)) {
        context.Response.ContentType = "text/html; charset=utf-8";
        return RenderHtml(help);
      }
      context.Response.ContentType = "application/json; charset=utf-8";
      return JsonConvert.SerializeObject(help, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
    }

    static private HelpDocument BuildHelp() {
      LoadDocumentation();
      var endpoints = RestRouter.Routes
        .OrderBy(r => GroupOf(r.Path), StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
        .Select(BuildEndpoint)
        .ToList();
      return new HelpDocument {
        Service = "TIA Openness REST API",
        TiaVersion = RestSettings.Current.TiaVersion,
        ApiKeyHeader = Network.TiaRestApiKeyHeader,
        Note = _docError ?? $"Web UI: {Network.RouteUi} (or /ui/). Endpoints with ApiKeyRequired need the header {Network.TiaRestApiKeyHeader}. " +
          "Repeated query parameters (e.g. path=A&path=B) are passed once per value. Request bodies are JSON (UTF-8).",
        Endpoints = endpoints
      };
    }

    static private HelpEndpoint BuildEndpoint(RestRoute route) {
      var endpoint = new HelpEndpoint {
        Group = GroupOf(route.Path),
        Method = route.Method,
        Path = route.Path,
        ApiKeyRequired = !route.Anonymous
      };
      if (_docMembers != null && _docMembers.TryGetValue(MemberId(route.Handler), out XElement member)) {
        endpoint.Summary = DocText(member.Element("summary"));
        var query = member.Elements("query")
          .Where(q => !string.IsNullOrWhiteSpace((string)q.Attribute("name")))
          .Select(ToHelpParameter)
          .ToList();
        endpoint.Query = query.Count > 0 ? query : null;

        var bodyElements = member.Elements("body").ToList();
        var bodyParams = bodyElements
          .Where(b => !string.IsNullOrWhiteSpace((string)b.Attribute("name")))
          .Select(ToHelpParameter)
          .ToList();
        endpoint.BodyParams = bodyParams.Count > 0 ? bodyParams : null;
        XElement bodyText = bodyElements.FirstOrDefault(b => string.IsNullOrWhiteSpace((string)b.Attribute("name")));
        endpoint.Body = DocText(bodyText);
        endpoint.Returns = DocText(member.Element("returns"));
      }
      return endpoint;
    }

    static private HelpParameter ToHelpParameter(XElement element) {
      string type = ((string)element.Attribute("type") ?? "").Trim();
      return new HelpParameter {
        Name = ((string)element.Attribute("name")).Trim(),
        Description = DocText(element),
        Type = type.Length > 0 ? type : null
      };
    }

    /// <summary>
    /// Gruppe eines Pfads: Bereich zwischen /api/ und der Funktion (z. B. "plc/blocks" für /api/plc/blocks/get),
    /// bei Pfaden ohne Bereich das einzige Segment (z. B. "health", "help").
    /// </summary>
    static private string GroupOf(string path) {
      string[] segments = path.Trim('/').Split('/');
      if (segments[0] == "api") {
        segments = segments.Skip(1).ToArray();
      }
      return segments.Length > 1 ? string.Join("/", segments.Take(segments.Length - 1)) : segments[0];
    }

    /// <summary>
    /// Liest die vom Compiler erzeugte XML-Dokumentation neben der Exe (einmalig).
    /// </summary>
    static private void LoadDocumentation() {
      if (_docMembers != null || _docError != null) {
        return;
      }
      string file = Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".xml");
      try {
        _docMembers = XDocument.Load(file).Descendants("member")
          .Where(m => m.Attribute("name") != null)
          .GroupBy(m => (string)m.Attribute("name"))
          .ToDictionary(g => g.Key, g => g.First());
      } catch (Exception ex) {
        _docError = $"Endpoint descriptions are not available: documentation file {file} could not be read ({ex.Message}).";
      }
    }

    static private string MemberId(MethodInfo method) {
      string parameters = string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName));
      return $"M:{method.DeclaringType.FullName.Replace('+', '.')}.{method.Name}({parameters})";
    }

    /// <summary>
    /// Text eines Doku-Elements mit aufgelösten see/paramref-Verweisen und normalisierten Leerzeichen.
    /// </summary>
    static private string DocText(XElement element) {
      if (element == null) {
        return null;
      }
      var text = new StringBuilder();
      AppendDocText(element, text);
      string result = Regex.Replace(text.ToString(), @"\s+", " ").Trim();
      return result.Length > 0 ? result : null;
    }

    static private void AppendDocText(XElement element, StringBuilder text) {
      foreach (XNode node in element.Nodes()) {
        if (node is XText textNode) {
          text.Append(textNode.Value);
        } else if (node is XElement child) {
          string reference = (string)child.Attribute("cref") ?? (string)child.Attribute("name") ?? (string)child.Attribute("langword");
          if (!child.Nodes().Any() && reference != null) {
            text.Append(reference.Substring(reference.LastIndexOfAny(new[] { ':', '.' }) + 1));
          } else {
            AppendDocText(child, text);
          }
        }
      }
    }

    static private string RenderHtml(HelpDocument help) {
      string Encode(string value) => WebUtility.HtmlEncode(value ?? "");
      var html = new StringBuilder();
      html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(Encode(help.Service)).Append("</title><style>")
        .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}h1{margin-bottom:4px}")
        .Append("h2{margin-top:32px;border-bottom:1px solid #ccc;text-transform:capitalize}")
        .Append(".ep{margin:16px 0;padding:10px 14px;border:1px solid #ddd;border-radius:6px;background:#fafafa}")
        .Append(".m{display:inline-block;min-width:56px;padding:2px 6px;border-radius:4px;color:#fff;font-weight:bold;text-align:center;margin-right:8px}")
        .Append(".GET{background:#2b7bb9}.PUT{background:#c77c11}.POST{background:#2e8b57}.PATCH{background:#8a4fbf}.DELETE{background:#c0392b}")
        .Append("code{font-size:1.05em}.anon{color:#2e8b57;font-size:.9em;margin-left:8px}")
        .Append("table{border-collapse:collapse;margin-top:6px}td{padding:2px 12px 2px 0;vertical-align:top}td:first-child{font-family:Consolas,monospace;white-space:nowrap}")
        .Append(".label{font-weight:bold;margin-top:6px}.note{color:#555}nav a{margin-right:12px}")
        .Append("</style></head><body>");
      html.Append("<h1>").Append(Encode(help.Service)).Append("</h1>");
      html.Append("<div class=\"note\">TIA Portal V").Append(Encode(help.TiaVersion)).Append(" &middot; API key header <code>")
        .Append(Encode(help.ApiKeyHeader)).Append("</code></div>");
      html.Append("<p class=\"note\">").Append(Encode(help.Note)).Append("</p>");

      var groups = help.Endpoints.GroupBy(e => e.Group).ToList();
      html.Append("<nav><a href=\"/\">Web UI</a>");
      foreach (var group in groups) {
        html.Append("<a href=\"#").Append(Encode(group.Key)).Append("\">").Append(Encode(group.Key)).Append("</a>");
      }
      html.Append("</nav>");

      foreach (var group in groups) {
        html.Append("<h2 id=\"").Append(Encode(group.Key)).Append("\">").Append(Encode(group.Key)).Append("</h2>");
        foreach (HelpEndpoint endpoint in group) {
          html.Append("<div class=\"ep\"><span class=\"m ").Append(Encode(endpoint.Method)).Append("\">").Append(Encode(endpoint.Method))
            .Append("</span><code>").Append(Encode(endpoint.Path)).Append("</code>");
          if (!endpoint.ApiKeyRequired) {
            html.Append("<span class=\"anon\">no API key</span>");
          }
          if (endpoint.Summary != null) {
            html.Append("<p>").Append(Encode(endpoint.Summary)).Append("</p>");
          }
          if (endpoint.Query != null) {
            html.Append("<div class=\"label\">Query</div><table>");
            foreach (HelpParameter parameter in endpoint.Query) {
              html.Append("<tr><td>").Append(Encode(parameter.Name));
              if (!string.IsNullOrEmpty(parameter.Type)) {
                html.Append(" <span class=\"note\">(").Append(Encode(parameter.Type)).Append(")</span>");
              }
              html.Append("</td><td>").Append(Encode(parameter.Description)).Append("</td></tr>");
            }
            html.Append("</table>");
          }
          if (endpoint.BodyParams != null) {
            html.Append("<div class=\"label\">Body</div><table>");
            foreach (HelpParameter parameter in endpoint.BodyParams) {
              html.Append("<tr><td>").Append(Encode(parameter.Name));
              if (!string.IsNullOrEmpty(parameter.Type)) {
                html.Append(" <span class=\"note\">(").Append(Encode(parameter.Type)).Append(")</span>");
              }
              html.Append("</td><td>").Append(Encode(parameter.Description)).Append("</td></tr>");
            }
            html.Append("</table>");
          } else if (endpoint.Body != null) {
            html.Append("<div class=\"label\">Body</div><div>").Append(Encode(endpoint.Body)).Append("</div>");
          }
          if (endpoint.Returns != null) {
            html.Append("<div class=\"label\">Returns</div><div>").Append(Encode(endpoint.Returns)).Append("</div>");
          }
          html.Append("</div>");
        }
      }
      html.Append("</body></html>");
      return html.ToString();
    }
  }
}
