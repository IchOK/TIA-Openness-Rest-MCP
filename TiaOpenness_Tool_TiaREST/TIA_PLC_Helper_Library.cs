using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using System;
using System.Collections.Generic;
using System.IO;
using Tophinke.TiaOpenness.Tool.Types.PLC.Library;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper {
  /// <summary>
  /// Optionen für das Schreiben in einen Bibliothekstyp.
  /// </summary>
  internal class LibraryWriteOptions {
    public string Mode;              // ModeNone, ModeInTest oder ModeRelease
    public Version VersionNumber;    // nur Release; null = Nummer der In-Test-Version
    public string Author;            // nur Release; null = Autor der bisherigen Version
    public string Comment;           // nur Release
  }

  /// <summary>
  /// Gemeinsame Hilfsmethoden für Instanzen von Typen der Projektbibliothek.
  /// </summary>
  static internal class cTiaLibraryHelpers {
    public const string ModeNone = "None";
    public const string ModeInTest = "InTest";
    public const string ModeRelease = "Release";

    /// <summary>
    /// Prüft die Bibliotheksoptionen aus dem Request.
    /// </summary>
    /// <param name="mode">"InTest", "Release" oder leer</param>
    /// <param name="version">Versionsnummer für Release, z. B. "0.1.37"</param>
    /// <param name="author">Autor für Release</param>
    /// <param name="comment">Kommentar für Release</param>
    /// <param name="options">Out-Parameter mit den geprüften Optionen</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static public string ResolveWriteOptions(string mode, string version, string author, string comment, out LibraryWriteOptions options) {
      options = null;
      string resolvedMode;
      if (string.IsNullOrWhiteSpace(mode) || mode.Trim().Equals(ModeNone, StringComparison.OrdinalIgnoreCase)) {
        resolvedMode = ModeNone;
      } else if (mode.Trim().Equals(ModeInTest, StringComparison.OrdinalIgnoreCase)) {
        resolvedMode = ModeInTest;
      } else if (mode.Trim().Equals(ModeRelease, StringComparison.OrdinalIgnoreCase)) {
        resolvedMode = ModeRelease;
      } else {
        return $"Error: Unknown libraryMode '{mode}'. Allowed: {ModeInTest}, {ModeRelease} or empty.";
      }

      Version versionNumber = null;
      if (!string.IsNullOrWhiteSpace(version)) {
        if (resolvedMode != ModeRelease) {
          return $"Error: libraryVersion is only used with libraryMode {ModeRelease}.";
        }
        if (!Version.TryParse(version.Trim(), out versionNumber) || versionNumber.Build < 0 || versionNumber.Revision >= 0) {
          return $"Error: Invalid libraryVersion '{version}'. Expected three numbers, e.g. 0.1.37.";
        }
      }

      options = new LibraryWriteOptions {
        Mode = resolvedMode,
        VersionNumber = versionNumber,
        Author = string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
        Comment = comment
      };
      return null;
    }

    /// <summary>
    /// Liefert die Bibliotheksverbindung eines Objekts.
    /// </summary>
    /// <returns>Bibliotheksverbindung oder null, wenn das Objekt keine Instanz eines Bibliothekstyps ist</returns>
    static public LibraryTypeInstanceInfo GetInstanceInfo(IEngineeringServiceProvider obj) {
      try {
        LibraryTypeInstanceInfo info = obj?.GetService<LibraryTypeInstanceInfo>();
        return info?.LibraryTypeVersion != null ? info : null;
      } catch {
        return null;
      }
    }

    /// <summary>
    /// Liefert Typname, Version und Zustand des Bibliothekstyps eines Objekts.
    /// </summary>
    /// <returns>LibraryInfo oder null, wenn das Objekt keine Instanz eines Bibliothekstyps ist</returns>
    static public LibraryInfo GetLibraryInfo(IEngineeringServiceProvider obj) {
      return ToLibraryInfo(GetInstanceInfo(obj)?.LibraryTypeVersion);
    }

    static public LibraryInfo ToLibraryInfo(LibraryTypeVersion version) {
      if (version == null) {
        return null;
      }
      return new LibraryInfo {
        TypeName = version.TypeObject?.Name,
        Version = version.VersionNumber?.ToString(),
        State = version.State.ToString()
      };
    }

    /// <summary>
    /// Beschreibt eine Typversion für Meldungen, z. B. "library type tqCipActivity V0.1.36 (InWork)".
    /// </summary>
    static public string Describe(LibraryTypeVersion version) {
      return $"library type {version.TypeObject?.Name} V{version.VersionNumber} ({version.State})";
    }

    /// <summary>
    /// Schreibt Dokumente als In-Test-Version in den Bibliothekstyp einer Instanz und gibt sie bei ModeRelease frei.
    /// Ist die verbundene Version freigegeben, wird zuerst eine In-Test-Version angelegt; schlägt das Schreiben fehl,
    /// wird diese wieder verworfen. Eine bereits vorhandene In-Test-Version wird überschrieben.
    /// </summary>
    /// <param name="instanceInfo">Bibliotheksverbindung der Instanz</param>
    /// <param name="targetEnvironment">Ordner der Testinstanz (PlcBlockGroup bzw. PlcTypeGroup)</param>
    /// <param name="directory">Verzeichnis mit den Dokumenten</param>
    /// <param name="fileName">Basisname der Dokumente ohne Endung</param>
    /// <param name="options">Bibliotheksoptionen (ModeInTest oder ModeRelease)</param>
    /// <param name="messages">Out-Parameter mit den Meldungen der einzelnen Schritte</param>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static public string WriteTypeVersion(LibraryTypeInstanceInfo instanceInfo, IEngineeringObject targetEnvironment, DirectoryInfo directory, string fileName, LibraryWriteOptions options, out string[] messages) {
      var log = new List<string>();
      messages = new string[0];
      LibraryTypeVersion version = instanceInfo.LibraryTypeVersion;
      LibraryType type = version?.TypeObject;
      if (type == null) {
        return "Error: The library type of the object could not be determined.";
      }

      bool editedHere = false;
      LibraryTypeVersion written;
      try {
        if (version.State == LibraryTypeVersionState.Committed) {
          version = version.Edit(instanceInfo);
          editedHere = true;
          log.Add($"Created in-test version {version.VersionNumber} of library type {type.Name}.");
        }
        VersionCreateTransferResults result = type.Versions.CreateFromDocuments(directory, fileName, targetEnvironment, CreateOptions.Override, LibraryImportOptions.None);
        log.AddRange(GetMessages(result?.Messages));
        written = result?.CreatedVersion ?? version;
        log.Add($"Wrote in-test version {written.VersionNumber} of library type {type.Name}.");
      } catch (Exception ex) {
        string error = $"Error: Writing library type {type.Name} failed: {ex.Message}";
        if (editedHere) {
          try {
            version.Discard();
            error += " The in-test version created for this call was discarded.";
          } catch (Exception discardEx) {
            error += $" Discarding the in-test version created for this call failed: {discardEx.Message}";
          }
        }
        messages = log.ToArray();
        return error;
      }

      if (options.Mode == ModeRelease) {
        Version number = options.VersionNumber ?? written.VersionNumber;
        string author = options.Author ?? written.Author ?? Environment.UserName;
        try {
          written.Release(CreateOrReleaseDependenciesMode.DoNotAutomaticallyCreateOrReleaseDependencies, number, author, options.Comment ?? "");
          log.Add($"Released version {number} of library type {type.Name}.");
        } catch (Exception ex) {
          messages = log.ToArray();
          return $"Error: The content was written to in-test version {written.VersionNumber} of library type {type.Name}, but releasing it failed: {ex.Message}";
        }
      }

      messages = log.ToArray();
      return null;
    }

    static private IEnumerable<string> GetMessages(TransferResultMessageComposition messages) {
      if (messages == null) {
        yield break;
      }
      foreach (TransferResultMessage message in messages) {
        if (message != null && !string.IsNullOrWhiteSpace(message.Message)) {
          yield return message.Message;
        }
      }
    }
  }
}
