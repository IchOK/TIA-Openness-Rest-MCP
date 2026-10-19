using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Liefert die Web-Oberfläche aus dem Ordner wwwroot neben der EXE (anonym, nur GET).
  /// </summary>
  static internal class RestStaticFiles {
    static private readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      { ".html", "text/html; charset=utf-8" },
      { ".css", "text/css; charset=utf-8" },
      { ".js", "application/javascript; charset=utf-8" },
      { ".json", "application/json; charset=utf-8" },
      { ".svg", "image/svg+xml" },
      { ".png", "image/png" },
      { ".ico", "image/x-icon" },
      { ".txt", "text/plain; charset=utf-8" }
    };

    /// <summary>
    /// Versucht, die Anfrage als statische Datei unter / bzw. /ui/… zu beantworten.
    /// </summary>
    /// <returns>true, wenn die Antwort geschrieben wurde</returns>
    static public bool TryServe(HttpListenerContext context) {
      if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase)) {
        return false;
      }

      string relative = MapPath(context.Request.Url.LocalPath);
      if (relative == null) {
        return false;
      }

      string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot"));
      string fullPath = Path.GetFullPath(Path.Combine(root, relative));
      if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
          || fullPath.Length <= root.Length
          || (fullPath[root.Length] != Path.DirectorySeparatorChar && fullPath[root.Length] != Path.AltDirectorySeparatorChar)) {
        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        WriteText(context, "text/plain; charset=utf-8", "Forbidden");
        return true;
      }

      if (!File.Exists(fullPath)) {
        return false;
      }

      string extension = Path.GetExtension(fullPath);
      if (!ContentTypes.TryGetValue(extension, out string contentType)) {
        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        WriteText(context, "text/plain; charset=utf-8", "Forbidden file type");
        return true;
      }

      byte[] bytes = File.ReadAllBytes(fullPath);
      context.Response.StatusCode = (int)HttpStatusCode.OK;
      context.Response.ContentType = contentType;
      context.Response.ContentLength64 = bytes.Length;
      if (!string.Equals(context.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase)) {
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
      }
      context.Response.OutputStream.Close();
      return true;
    }

    /// <summary>
    /// / und /index.html → index.html; /ui/… → Datei unter wwwroot.
    /// </summary>
    static private string MapPath(string localPath) {
      if (string.IsNullOrEmpty(localPath) || localPath == "/"
          || localPath.Equals("/index.html", StringComparison.OrdinalIgnoreCase)
          || localPath.Equals("/ui", StringComparison.OrdinalIgnoreCase)
          || localPath.Equals("/ui/", StringComparison.OrdinalIgnoreCase)) {
        return "index.html";
      }
      if (!localPath.StartsWith("/ui/", StringComparison.OrdinalIgnoreCase)) {
        return null;
      }
      string relative = localPath.Substring(4).Replace('/', Path.DirectorySeparatorChar);
      if (relative.IndexOf("..", StringComparison.Ordinal) >= 0 || Path.IsPathRooted(relative)) {
        return null;
      }
      return relative;
    }

    static private void WriteText(HttpListenerContext context, string contentType, string text) {
      byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text ?? "");
      context.Response.ContentType = contentType;
      context.Response.ContentLength64 = bytes.Length;
      context.Response.OutputStream.Write(bytes, 0, bytes.Length);
      context.Response.OutputStream.Close();
    }
  }
}
