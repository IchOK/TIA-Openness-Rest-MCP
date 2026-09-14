using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.TiaREST;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  class Program {
    static string tiaVersion = Network.TiaVersionDefault;
    static string defaultApiKey = Network.TiaRestApiKeyDefault;

    static void Main(string[] args) {
      // Prüfen ob globale Parameter definert wurden
      if (args.Length > 0) {
        for (int i = 0; i < args.Length; i++) {
          if (args[i] == "-v" || args[i] == "--version") {
            if (i + 1 < args.Length) {
              tiaVersion = args[i + 1];
              Console.WriteLine($"TIA Version gesetzt: {tiaVersion}");
            }
          }
        }
      }

      AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
      RunServer(args);
    }

    private static void RunServer(string[] args) {
      // Standardwerte für den REST-Server
      int restPort = Network.TiaRestPort;
      string apiKey = "";
      if (args.Length > 0) {
        for (int i = 0; i < args.Length; i++) {
          if (args[i] == "-k" || args[i] == "--key" || args[i] == "--apikey") {
            if (i + 1 < args.Length) {
              apiKey = args[i + 1];
            }
          }
          if (args[i] == "-p" || args[i] == "--port") {
            if (i + 1 < args.Length) {
              restPort = int.Parse(args[i + 1]);
            }
          }
        }
      }
      if (apiKey == "") {
        apiKey = defaultApiKey;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("ACHTUNG: REST-Server läuft mit Default-Key");
        Console.ResetColor();
      }

      string url = $"http://localhost:{restPort}/";
      HttpListener listener = new HttpListener();
      listener.Prefixes.Add(url);
      listener.Start();

      Console.WriteLine($"Erweiterter TIA Sidecar lauscht auf {url} ...");

      while (true) {
        HttpListenerContext context = listener.GetContext();
        HttpListenerResponse response = context.Response;
        string path = context.Request.Url.LocalPath;
        string responseString = "";

        try {
          if (path == Network.RouteHealth) {
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            responseString = $"API is running on version {tiaVersion}";
          } else if(context.Request.Headers[Network.TiaRestApiKeyHeader] != apiKey) {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "text/plain";
            responseString = $"Error: Unauthorized access. Invalid or missing {Network.TiaRestApiKeyHeader}.";
          }
          
          // Routen für TIA-Projekte
          else if (path == ProjectRoutes.List) {
            responseString = cTiaProject.List(context);
          }
          
          // Routen für Datenbausteine
          else if (path == DBRoutes.List) {
            responseString = cTiaDBs.List(context);
          } else if (path == DBRoutes.Get) {
            responseString = cTiaDBs.Get(context);
          }

          // Routen für Datentypen
          else if (path == UDTRoutes.List) {
            responseString = cTiaUDTs.List(context);
          } else if (path == UDTRoutes.Get) {
            responseString = cTiaUDTs.Get(context);
          }

          // Route nicht gefunden
          else {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            responseString = $"Error: Unknown route: {path}";
          }
        } catch (Exception ex) {
          context.Response.StatusCode = (int)HttpStatusCode.NotFound;
          context.Response.ContentType = "text/plain";
          responseString = $"Unexpected error: {ex.Message}";
        }

        // Antwort senden
        byte[] buffer = Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
      }
    }

    private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args) {
      var assemblyName = new AssemblyName(args.Name);
      if (assemblyName.Name.StartsWith("Siemens.Engineering.AddIn")) {
        // Pfad zur offiziellen PublicAPI der installierten TIA Portal Version (hier V20)
        string path = $@"C:\Program Files\Siemens\Automation\Portal V{tiaVersion}\PublicAPI\V{tiaVersion}.AddIn\{assemblyName.Name}.dll";

        if (File.Exists(path)) {
          return Assembly.LoadFrom(path);
        }
      }
      if (assemblyName.Name.StartsWith("Siemens.Engineering")) {
        // Pfad zur offiziellen PublicAPI der installierten TIA Portal Version (hier V20)
        string path = $@"C:\Program Files\Siemens\Automation\Portal V{tiaVersion}\PublicAPI\V{tiaVersion}\{assemblyName.Name}.dll";

        if (File.Exists(path)) {
          return Assembly.LoadFrom(path);
        }
      }
      return null;
    }
  }
}