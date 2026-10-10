using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using Tophinke.TiaOpenness.Tool.Consts;

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
      // Handler-Signaturen verweisen auf Siemens.Engineering; erst nach AssemblyResolve per Reflection lesen.
      try {
        RestRouter.Build();
      } catch (InvalidOperationException ex) {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
      }
#if DEBUG
      // Sync Openness firewall entry before any TIA attach (needs admin / elevated process).
      OpennessWhitelist.TryUpdate(settings.TiaVersion);
#endif
      RunServer(settings);
      return 0;
    }

    private static void RunServer(RestSettings settings) {
      string url = $"http://{settings.Host}:{settings.Port}/";
      HttpListener listener = new HttpListener();
      listener.Prefixes.Add(url);
      listener.Start();

      Console.WriteLine($"Erweiterter TIA Sidecar lauscht auf {url} ({RestRouter.Routes.Count} Routen, Beschreibung unter {Network.RouteHelp}) ...");

      while (true) {
        HttpListenerContext context = listener.GetContext();
        HttpListenerResponse response = context.Response;
        string responseString;

        try {
          responseString = RestRouter.Dispatch(context, settings.ApiKey);
        } catch (Exception ex) {
          context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
          context.Response.ContentType = "text/plain";
          responseString = $"Unexpected error: {ex.Message}";
        }

        // Antwort senden
        byte[] buffer = Encoding.UTF8.GetBytes(responseString ?? "");
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
