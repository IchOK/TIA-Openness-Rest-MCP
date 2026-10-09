using Newtonsoft.Json;
using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper {
  /// <summary>
  /// Gemeinsame Hilfsmethoden für schreibende REST-Handler (PUT/PATCH).
  /// </summary>
  static internal class cTiaRequestHelpers {
    public const string FormatSd = "SimaticData/SD";
    public const string FormatXml = "SimaticML/XML";

    /// <summary>
    /// Setzt eine Fehlerantwort und gibt die Fehlermeldung zurück.
    /// </summary>
    static public string ErrorResponse(HttpListenerContext context, HttpStatusCode statusCode, string errorMessage) {
      Console.Error.WriteLine(errorMessage);
      context.Response.StatusCode = (int)statusCode;
      context.Response.ContentType = "text/plain";
      return errorMessage;
    }

    /// <summary>
    /// Prüft die HTTP-Methode der Anfrage.
    /// </summary>
    /// <returns>Fehlermeldung (Antwort ist bereits gesetzt) oder null, wenn die Methode passt</returns>
    static public string RequireMethod(HttpListenerContext context, string method, string route) {
      if (string.Equals(context.Request.HttpMethod, method, StringComparison.OrdinalIgnoreCase)) {
        return null;
      }
      context.Response.AddHeader("Allow", method);
      return ErrorResponse(context, HttpStatusCode.MethodNotAllowed, $"Error: Route {route} requires HTTP {method}.");
    }

    /// <summary>
    /// Liest den JSON-Body der Anfrage (UTF-8) und deserialisiert ihn.
    /// </summary>
    static public T ReadBody<T>(HttpListenerContext context) {
      using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8)) {
        return JsonConvert.DeserializeObject<T>(reader.ReadToEnd());
      }
    }

    /// <summary>
    /// Ermittelt das Import-Format aus der Angabe im Request oder aus dem Inhalt.
    /// </summary>
    /// <returns>FormatSd, FormatXml oder null bei unbekanntem Format</returns>
    static public string ResolveImportFormat(string format, string content) {
      if (string.IsNullOrWhiteSpace(format)) {
        return content.TrimStart().StartsWith("<") ? FormatXml : FormatSd;
      }
      if (format.Equals(FormatSd, StringComparison.OrdinalIgnoreCase) || format.Equals("SD", StringComparison.OrdinalIgnoreCase)) {
        return FormatSd;
      }
      if (format.Equals(FormatXml, StringComparison.OrdinalIgnoreCase) || format.Equals("XML", StringComparison.OrdinalIgnoreCase)) {
        return FormatXml;
      }
      return null;
    }

    /// <summary>
    /// Prüft, ob Content und MultiLingualText zum Import-Format passen. Bei SD muss MultiLingualText
    /// für jede in Content referenzierte Text-ID ({ S7_MLC := "id" }) einen Eintrag enthalten.
    /// </summary>
    /// <returns>Fehlermeldung oder null, wenn alles passt</returns>
    static public string ValidateImportContent(string format, string content, string multiLingualText) {
      bool contentIsXml = content.TrimStart().TrimStart('\uFEFF').TrimStart().StartsWith("<");

      if (format == FormatXml) {
        if (!contentIsXml) {
          return $"Error: Format is {FormatXml}, but Content is not XML.";
        }
        if (!string.IsNullOrWhiteSpace(multiLingualText)) {
          return $"Error: MultiLingualText is only used with {FormatSd}. For {FormatXml} the texts are part of the XML content; pass null.";
        }
        return null;
      }

      if (contentIsXml) {
        return $"Error: Format is {FormatSd}, but Content is XML. Use Format {FormatXml}.";
      }

      var referencedIds = Regex.Matches(content, @"S7_MLC\s*:=\s*""([^""]+)""")
        .Cast<Match>()
        .Select(m => m.Groups[1].Value)
        .Distinct(StringComparer.Ordinal)
        .ToList();
      if (referencedIds.Count == 0) {
        return null;
      }
      if (string.IsNullOrWhiteSpace(multiLingualText)) {
        return $"Error: Content references multilingual texts ({string.Join(", ", referencedIds)}), but MultiLingualText is empty. " +
          "Pass the MultiLingualText returned by the corresponding Get call (content of the .s7res file).";
      }
      var missingIds = referencedIds
        .Where(id => !Regex.IsMatch(multiLingualText, @"^\s*-?\s*id:\s*""?" + Regex.Escape(id) + @"""?\s*$", RegexOptions.Multiline))
        .ToList();
      if (missingIds.Count > 0) {
        return $"Error: MultiLingualText has no entries for the text IDs referenced in Content: {string.Join(", ", missingIds)}.";
      }
      return null;
    }

    /// <summary>
    /// Liest eine kommagetrennte Filterliste aus der Query.
    /// </summary>
    /// <returns>Getrimmte, nicht leere Einträge oder null, wenn kein Filter angegeben ist</returns>
    static public string[] ReadListFilter(HttpListenerContext context, string name) {
      string[] values = context.Request.QueryString[name]?
        .Split(',')
        .Select(v => v.Trim())
        .Where(v => v.Length > 0)
        .ToArray();
      return values != null && values.Length > 0 ? values : null;
    }

    /// <summary>
    /// Liest den Ordnerfilter aus der Query; jeder Ordnername ist ein eigener Parameter (path=A&amp;path=B).
    /// </summary>
    /// <returns>Ordnernamen vom Wurzelordner aus oder null, wenn kein Filter angegeben ist</returns>
    static public string[] ReadPathFilter(HttpListenerContext context) {
      string[] values = context.Request.QueryString.GetValues("path")?
        .Where(v => !string.IsNullOrWhiteSpace(v))
        .Select(v => v.Trim())
        .ToArray();
      return values != null && values.Length > 0 ? values : null;
    }

    /// <summary>
    /// Sucht den Unterordner zum angegebenen Pfad (ohne Berücksichtigung der Groß-/Kleinschreibung).
    /// </summary>
    /// <param name="resolvedPath">Out-Parameter mit den Ordnernamen in der Schreibweise des Projekts</param>
    /// <returns>Gefundener Ordner oder null</returns>
    static public T ResolveGroup<T>(T root, string[] path, Func<T, IEnumerable<T>> children, Func<T, string> name, out string[] resolvedPath) where T : class {
      T group = root;
      var names = new List<string>();
      resolvedPath = null;
      foreach (string folder in path ?? new string[0]) {
        group = children(group).FirstOrDefault(g => string.Equals(name(g), folder, StringComparison.OrdinalIgnoreCase));
        if (group == null) {
          return null;
        }
        names.Add(name(group));
      }
      resolvedPath = names.ToArray();
      return group;
    }

    /// <summary>
    /// Vergleicht zwei Ordnerpfade ohne Berücksichtigung der Groß-/Kleinschreibung.
    /// </summary>
    static public bool PathEquals(string[] a, string[] b) {
      return a.Length == b.Length && a.Zip(b, (x, y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)).All(equal => equal);
    }

    /// <summary>
    /// Liest einen mehrsprachigen Text in der angegebenen Sprache.
    /// </summary>
    /// <returns>Text oder null, wenn die Sprache nicht vorhanden ist</returns>
    static public string GetText(MultilingualText text, Language language) {
      if (text == null || language == null) {
        return null;
      }
      try {
        return text.Items.Find(language)?.Text;
      } catch {
        return null;
      }
    }

    /// <summary>
    /// Setzt einen mehrsprachigen Text in der angegebenen Sprache.
    /// </summary>
    static public void SetText(MultilingualText text, Language language, string value) {
      MultilingualTextItem item = language != null ? text?.Items.Find(language) : null;
      if (item == null) {
        throw new InvalidOperationException($"Text language {language?.Culture?.Name ?? "(none)"} is not available.");
      }
      item.Text = value;
    }
  }
}
