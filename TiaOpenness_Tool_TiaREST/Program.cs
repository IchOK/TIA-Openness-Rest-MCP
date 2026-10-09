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
using Tophinke.TiaOpenness.Tool.TiaREST.PLC;
using Tophinke.TiaOpenness.Tool.TiaREST.TopCtrl.PLC;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  class Program {
    static int Main(string[] args) {
      RestSettings settings;
      try {
        settings = RestSettings.Load(args);
      } catch (InvalidOperationException ex) {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
      }
      Console.WriteLine($"TIA Version: {settings.TiaVersion}, Siemens path: {settings.SiemensPath}");

      AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
#if DEBUG
      // Sync Openness firewall entry before any TIA attach (needs admin / elevated process).
      OpennessWhitelist.TryUpdate(settings.TiaVersion);
#endif
      RunServer(settings);
      return 0;
    }

    private static void RunServer(RestSettings settings) {
      string apiKey = settings.ApiKey;
      string tiaVersion = settings.TiaVersion;
      string url = $"http://{settings.Host}:{settings.Port}/";
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
          
          // Routen für Bausteine (DB, FB, FC, OB, …)
          else if (path == BlockRoutes.List) {
            responseString = cTiaBlocks.GetAll(context);
          } else if (path == BlockRoutes.Get) {
            responseString = cTiaBlocks.Get(context);
          } else if (path == BlockRoutes.GetFile) {
            responseString = cTiaBlocks.GetFile(context);
          } else if (path == BlockRoutes.Put) {
            responseString = cTiaBlocks.Put(context);
          } else if (path == BlockRoutes.PutFile) {
            responseString = cTiaBlocks.PutFile(context);
          } else if (path == BlockRoutes.Patch) {
            responseString = cTiaBlocks.Patch(context);
          } else if (path == BlockRoutes.DiscardTypeVersion) {
            responseString = cTiaBlocks.DiscardTypeVersion(context);
          } else if (path == BlockRoutes.ExportCapabilities) {
            responseString = cTiaBlocks.ListExportCapabilities(context);
          }

          // Routen für Datentypen
          else if (path == UDTRoutes.List) {
            responseString = cTiaUDTs.List(context);
          } else if (path == UDTRoutes.Get) {
            responseString = cTiaUDTs.Get(context);
          } else if (path == UDTRoutes.Put) {
            responseString = cTiaUDTs.Put(context);
          }

          // Routen für das Übersetzen
          else if (path == CompileRoutes.Item) {
            responseString = cTiaCompile.Item(context);
          } else if (path == CompileRoutes.Plc) {
            responseString = cTiaCompile.Plc(context);
          }

          // Routen für Variablentabellen
          else if (path == TagTableRoutes.List) {
            responseString = cTiaTagTables.List(context);
          } else if (path == TagTableRoutes.Get) {
            responseString = cTiaTagTables.Get(context);
          } else if (path == TagTableRoutes.Put) {
            responseString = cTiaTagTables.Put(context);
          } else if (path == TagTableRoutes.Patch) {
            responseString = cTiaTagTables.Patch(context);
          }

          // Routen für Querverweise
          else if (path == XRefRoutes.Get) {
            responseString = cTiaXRefs.Get(context);
          }

          // Routen für Meldungen
          else if (path == MessageRoutes.List) {
            responseString = cTiaMessages.List(context);
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
      string tiaVersion = RestSettings.Current.TiaVersion;
      // PublicAPI der installierten TIA Portal Version: <SiemensPath>\Portal V<Version>\PublicAPI\V<Version>[.AddIn]
      string publicApi = Path.Combine(RestSettings.Current.SiemensPath, $"Portal V{tiaVersion}", "PublicAPI");
      if (assemblyName.Name.StartsWith("Siemens.Engineering.AddIn")) {
        string path = Path.Combine(publicApi, $"V{tiaVersion}.AddIn", assemblyName.Name + ".dll");

        if (File.Exists(path)) {
          return Assembly.LoadFrom(path);
        }
      }
      if (assemblyName.Name.StartsWith("Siemens.Engineering")) {
        string path = Path.Combine(publicApi, $"V{tiaVersion}", assemblyName.Name + ".dll");

        if (File.Exists(path)) {
          return Assembly.LoadFrom(path);
        }
      }
      return null;
    }
  }
}