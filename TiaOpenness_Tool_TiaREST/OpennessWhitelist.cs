using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Keeps the TIA Portal Openness firewall whitelist entry in sync with this executable.
  /// </summary>
  internal static class OpennessWhitelist {
    /// <summary>
    /// Updates HKLM whitelist Entry for the running EXE when elevated; otherwise verifies match.
    /// </summary>
    public static void TryUpdate(string tiaMajorMinorVersion) {
      try {
        string exePath = GetProcessImagePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) {
          Console.WriteLine("[Openness] Whitelist: executable path not found.");
          return;
        }

        var fileInfo = new FileInfo(Path.GetFullPath(exePath));
        string fileHash = ComputeHash(fileInfo);
        string dateModified = FormatDate(fileInfo.LastWriteTimeUtc);
        string version = NormalizeVersion(tiaMajorMinorVersion);

        if (!IsAdministrator()) {
          bool match = RegistryMatches(version, fileInfo.Name, fileInfo.FullName, fileHash, dateModified);
          if (match) {
            Console.WriteLine("[Openness] Whitelist: OK (registry matches this process).");
          } else {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Openness] Whitelist: MISMATCH or missing. TIA may deny attach.");
            Console.WriteLine("[Openness]   Process: " + fileInfo.FullName);
            Console.WriteLine("[Openness]   DateModified: " + dateModified);
            Console.WriteLine("[Openness]   FileHash: " + fileHash);
            Console.WriteLine("[Openness]   Run task 'update-openness-whitelist' (UAC), then restart TIA Portal.");
            Console.ResetColor();
          }
          return;
        }

        WriteEntries(version, fileInfo, fileHash, dateModified);
        Console.WriteLine("[Openness] Whitelist updated for " + fileInfo.Name + " (TIA " + version + ")");
        Console.WriteLine("[Openness]   Path: " + fileInfo.FullName);
        Console.WriteLine("[Openness]   DateModified: " + dateModified);
        Console.WriteLine("[Openness]   FileHash: " + fileHash);
      } catch (Exception ex) {
        Console.WriteLine("[Openness] Whitelist update failed: " + ex.Message);
      }
    }

    static void WriteEntries(string version, FileInfo fileInfo, string fileHash, string dateModified) {
      string[] roots = {
        @"SOFTWARE\Siemens\Automation\Openness\" + version + @"\Whitelist\" + fileInfo.Name,
        @"SOFTWARE\WOW6432Node\Siemens\Automation\Openness\" + version + @"\Whitelist\" + fileInfo.Name
      };

      foreach (string root in roots) {
        using (RegistryKey baseKey = Registry.LocalMachine.CreateSubKey(root)) {
          if (baseKey == null) {
            continue;
          }
          foreach (string subName in baseKey.GetSubKeyNames()) {
            if (subName.StartsWith("Entry", StringComparison.OrdinalIgnoreCase)) {
              baseKey.DeleteSubKeyTree(subName, false);
            }
          }
          using (RegistryKey entry = baseKey.CreateSubKey("Entry")) {
            entry.SetValue("Path", fileInfo.FullName, RegistryValueKind.String);
            entry.SetValue("FileHash", fileHash, RegistryValueKind.String);
            entry.SetValue("DateModified", dateModified, RegistryValueKind.String);
          }
        }
      }
    }

    static bool RegistryMatches(string version, string exeName, string path, string fileHash, string dateModified) {
      string[] roots = {
        @"SOFTWARE\Siemens\Automation\Openness\" + version + @"\Whitelist\" + exeName + @"\Entry",
        @"SOFTWARE\WOW6432Node\Siemens\Automation\Openness\" + version + @"\Whitelist\" + exeName + @"\Entry"
      };

      foreach (string root in roots) {
        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(root)) {
          if (key == null) {
            continue;
          }
          string regPath = key.GetValue("Path") as string;
          string regHash = key.GetValue("FileHash") as string;
          string regDate = key.GetValue("DateModified") as string;
          if (string.Equals(regPath, path, StringComparison.OrdinalIgnoreCase)
              && string.Equals(regHash, fileHash, StringComparison.Ordinal)
              && string.Equals(regDate, dateModified, StringComparison.Ordinal)) {
            return true;
          }
        }
      }
      return false;
    }

    static string ComputeHash(FileInfo fileInfo) {
      using (var stream = fileInfo.OpenRead())
      using (var sha = SHA256.Create()) {
        return Convert.ToBase64String(sha.ComputeHash(stream));
      }
    }

    static string FormatDate(DateTime utc) {
      return utc.ToString("yyyy'/'MM'/'dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    static bool IsAdministrator() {
      using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) {
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
      }
    }

    static string GetProcessImagePath() {
      try {
        return Process.GetCurrentProcess().MainModule.FileName;
      } catch {
        return typeof(OpennessWhitelist).Assembly.Location;
      }
    }

    static string NormalizeVersion(string version) {
      if (string.IsNullOrWhiteSpace(version)) {
        return "20.0";
      }
      version = version.Trim();
      if (version.IndexOf('.') < 0) {
        return version + ".0";
      }
      string[] parts = version.Split('.');
      if (parts.Length >= 2) {
        return parts[0] + "." + parts[1];
      }
      return version;
    }
  }
}
