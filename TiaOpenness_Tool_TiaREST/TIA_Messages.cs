using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using Tophinke.TiaOpenness.Tool.Types.Message;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Sammelt Meldungen aus Meldekonfigurationen (tbMsg_ConfElement_T) in PLC-Datenbausteinen.
  /// </summary>
  static internal class cTiaMessages {
    const string ConfElementType = "tbMsg_ConfElement_T";
    const int MessagesPerUnit = 16;
    const int MaxArrayProbe = 512;

    static readonly string[] HmiInterfaceTypes = {
      "tbMsg_Drv16SCADA_T",
      "tbMsg_Drv16Unified_T",
      "tbMsg_Drv16CP_T"
    };

    /// <summary>
    /// Listet alle relevanten Meldungen (xSelect = true) eines TIA-Projekts.
    /// Query: processId, projectName; optional deviceName, deviceItemName (sonst alle PLCs).
    /// </summary>
    static public string List(HttpListenerContext context) {
      string processIdStr = context.Request.QueryString["processId"];
      string projectName = context.Request.QueryString["projectName"];
      string deviceName = context.Request.QueryString["deviceName"];
      string deviceItemName = context.Request.QueryString["deviceItemName"];

      try {
        var retValue = TiaConnectionManager.Instance.ExecuteWithProject(processIdStr, projectName, project => {
          var messages = new List<Info>();

          foreach (Device device in project.Devices) {
            foreach (DeviceItem deviceItem in device.DeviceItems) {
              if (!string.IsNullOrEmpty(deviceName) &&
                  !device.Name.Equals(deviceName, StringComparison.OrdinalIgnoreCase)) {
                continue;
              }
              if (!string.IsNullOrEmpty(deviceItemName) &&
                  !deviceItem.Name.Equals(deviceItemName, StringComparison.OrdinalIgnoreCase)) {
                continue;
              }

              SoftwareContainer container = deviceItem.GetService<SoftwareContainer>();
              if (container == null || !(container.Software is PlcSoftware plcSoftware)) {
                continue;
              }

              CollectFromBlockGroup(
                plcSoftware.BlockGroup,
                device.Name,
                deviceItem.Name,
                plcSoftware.Name,
                new string[0],
                messages);
            }
          }

          context.Response.ContentType = "application/json";
          return JsonConvert.SerializeObject(messages);
        });
        return retValue;
      } catch (EngineeringObjectDisposedException ex) {
        Console.Error.WriteLine($"TIA session disposed: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.ContentType = "text/plain";
        return "Error: TIA session unavailable. Please re-open TIA Portal.";
      } catch (Exception ex) {
        Console.Error.WriteLine($"Error collecting messages: {ex.Message}");
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "text/plain";
        return $"Error collecting messages: {ex.Message}";
      }
    }

    static void CollectFromBlockGroup(
      PlcBlockGroup group,
      string deviceName,
      string deviceItemName,
      string plcName,
      string[] path,
      List<Info> messages) {

      foreach (PlcBlock block in group.Blocks) {
        DataBlock dataBlock = block as DataBlock;
        if (dataBlock == null) {
          continue;
        }
        try {
          CollectFromDataBlock(dataBlock, deviceName, deviceItemName, plcName, path, messages);
        } catch (Exception ex) {
          Console.Error.WriteLine($"Skip DB '{block.Name}': {ex.Message}");
        }
      }

      foreach (PlcBlockUserGroup userGroup in group.Groups) {
        string[] childPath = new string[path.Length + 1];
        Array.Copy(path, childPath, path.Length);
        childPath[path.Length] = userGroup.Name;
        CollectFromBlockGroup(userGroup, deviceName, deviceItemName, plcName, childPath, messages);
      }
    }

    static void CollectFromDataBlock(
      DataBlock dataBlock,
      string deviceName,
      string deviceItemName,
      string plcName,
      string[] path,
      List<Info> messages) {

      PlcBlockInterface blockInterface = dataBlock.Interface;
      if (blockInterface == null || blockInterface.Members == null) {
        return;
      }

      MemberComposition rootMembers = blockInterface.Members;
      var confRoots = new List<ConfRoot>();
      FindConfRoots(rootMembers, "", confRoots);

      foreach (ConfRoot conf in confRoots) {
        bool treatAsMultiBlock = conf.Elements.Count > MessagesPerUnit || conf.Meldeblock.HasValue;

        for (int i = 0; i < conf.Elements.Count; i++) {
          ConfElement el = conf.Elements[i];
          if (!el.XSelect) {
            continue;
          }

          int meldeindex;
          int? meldeblock;
          if (treatAsMultiBlock) {
            meldeblock = conf.Meldeblock ?? (i / MessagesPerUnit);
            meldeindex = i % MessagesPerUnit;
          } else {
            meldeblock = null;
            meldeindex = i;
          }

          messages.Add(new Info {
            Meldetext = el.Comment ?? "",
            Meldetype = el.IType ?? "",
            Meldeblock = meldeblock,
            Meldeindex = meldeindex,
            DbName = dataBlock.Name,
            Path = path,
            DeviceName = deviceName,
            DeviceItemName = deviceItemName,
            PlcName = plcName,
            ConfMemberPath = el.MemberPath
          });
        }
      }
    }

    #region Interface-Scan

    class ConfRoot {
      public string MemberPath;
      public int? Meldeblock;
      public List<ConfElement> Elements = new List<ConfElement>();
    }

    class ConfElement {
      public string MemberPath;
      public string Comment;
      public string IType;
      public bool XSelect;
    }

    static void FindConfRoots(MemberComposition rootMembers, string prefix, List<ConfRoot> results) {
      if (rootMembers == null) {
        return;
      }

      foreach (Member member in rootMembers) {
        string path = CombinePath(prefix, member.Name);
        string dataType = GetDataTypeName(member);

        if (IsArrayOfConfElement(dataType)) {
          var root = new ConfRoot {
            MemberPath = path,
            Meldeblock = ExtractFirstArrayIndex(path)
          };
          CollectArrayConfElements(rootMembers, path, root.Elements);
          if (root.Elements.Count > 0) {
            results.Add(root);
          }
          continue;
        }

        if (IsConfElementType(dataType) || LooksLikeConfElement(rootMembers, path)) {
          var root = new ConfRoot { MemberPath = path };
          root.Elements.Add(ReadConfElement(rootMembers, path));
          results.Add(root);
          continue;
        }

        // Struct/UDT: entweder selbst eine Struct-of-ConfElements, oder tiefer suchen
        if (IsArrayOfHmiType(dataType)) {
          // HMI-Array: Konfig liegt typischerweise nicht hier, aber Kinder können Conf tragen
          CollectNestedUnderArray(rootMembers, path, results);
          continue;
        }

        // Probe nested members via Find(path.child) — top-level only here;
        // nested UDT fields are reached when their containing member is expanded by probing known patterns
        // or when datatype indicates a plain struct: try common conf member names and generic child probe.
        TryCollectStructOfConfElements(rootMembers, path, dataType, results);
      }
    }

    static void CollectNestedUnderArray(MemberComposition rootMembers, string arrayPath, List<ConfRoot> results) {
      for (int i = 0; i < MaxArrayProbe; i++) {
        string indexed = arrayPath + "[" + i + "]";
        Member element = SafeFind(rootMembers, indexed);
        if (element == null) {
          break;
        }
        // In jedem HMI-Array-Element nach Conf-Membern suchen (selten direkt)
        // Zusätzlich: wenn das Element selbst Conf-Array/Struct enthält — über Geschwister-DBs abgedeckt.
      }
    }

    static void TryCollectStructOfConfElements(
      MemberComposition rootMembers,
      string path,
      string dataType,
      List<ConfRoot> results) {

      // Heuristik: Member-Pfad + "[0]" oder ".0" / bekannte Kindnamen als ConfElement
      var elements = new List<ConfElement>();

      // Variante A: Array-ähnliche Kinder unter Struct-Namen über Find(path[i])
      CollectArrayConfElements(rootMembers, path, elements);
      if (elements.Count >= 1) {
        results.Add(new ConfRoot {
          MemberPath = path,
          Meldeblock = ExtractFirstArrayIndex(path),
          Elements = elements
        });
        return;
      }

      // Variante B: benannte Struct-Felder path.Field0 … — nur wenn Datentyp kein einfacher Basis-Typ ist
      if (string.IsNullOrEmpty(dataType) || IsPrimitiveOrSimple(dataType)) {
        return;
      }

      // Probe: path.iType würde bedeuten, path selbst ist ConfElement (bereits oben behandelt)
      // Für Struct-of-16: Kinder oft "Msg01"/"Element_1"/numerisch — ohne Schema schwer.
      // Export-Fallback nicht hier; leere Structs überspringen.
    }

    static void CollectArrayConfElements(MemberComposition rootMembers, string arrayPath, List<ConfElement> elements) {
      for (int i = 0; i < MaxArrayProbe; i++) {
        string elementPath = arrayPath + "[" + i + "]";
        Member element = SafeFind(rootMembers, elementPath);
        if (element == null) {
          // Manche Exports nutzen ".i" statt "[i]"
          elementPath = arrayPath + "." + i;
          element = SafeFind(rootMembers, elementPath);
        }
        if (element == null) {
          break;
        }

        string elementType = GetDataTypeName(element);
        if (IsConfElementType(elementType) || LooksLikeConfElement(rootMembers, elementPath)) {
          elements.Add(ReadConfElement(rootMembers, elementPath));
        } else {
          // Kein Conf-Element — Array ist kein Conf-Array
          elements.Clear();
          return;
        }
      }
    }

    static ConfElement ReadConfElement(MemberComposition rootMembers, string confPath) {
      var result = new ConfElement {
        MemberPath = confPath,
        Comment = GetMemberComment(SafeFind(rootMembers, confPath)),
        IType = GetStartValueString(SafeFind(rootMembers, confPath + ".iType")),
        XSelect = IsTrueStartValue(GetStartValueString(SafeFind(rootMembers, confPath + ".xSelect")))
      };
      return result;
    }

    static bool LooksLikeConfElement(MemberComposition rootMembers, string path) {
      return SafeFind(rootMembers, path + ".iType") != null
        || SafeFind(rootMembers, path + ".xSelect") != null;
    }

    static Member SafeFind(MemberComposition members, string path) {
      if (members == null || string.IsNullOrEmpty(path)) {
        return null;
      }
      try {
        return members.Find(path);
      } catch {
        return null;
      }
    }

    #endregion

    #region Type / Value helpers

    static string GetDataTypeName(Member member) {
      if (member == null) {
        return "";
      }
      try {
        object value = member.GetAttribute("DataTypeName");
        if (value != null) {
          return value.ToString();
        }
      } catch {
      }
      return "";
    }

    static string GetStartValueString(Member member) {
      if (member == null) {
        return null;
      }
      try {
        object sv = member.GetAttribute("StartValue");
        if (sv != null) {
          return sv.ToString();
        }
      } catch {
      }
      return null;
    }

    static string GetMemberComment(Member member) {
      if (member == null) {
        return null;
      }
      try {
        object comment = member.GetAttribute("Comment");
        if (comment == null) {
          return null;
        }
        MultilingualText ml = comment as MultilingualText;
        if (ml != null && ml.Items != null) {
          foreach (MultilingualTextItem item in ml.Items) {
            if (item != null && !string.IsNullOrWhiteSpace(item.Text)) {
              return item.Text;
            }
          }
        }
        return comment.ToString();
      } catch {
        return null;
      }
    }

    static bool IsTrueStartValue(string value) {
      if (string.IsNullOrWhiteSpace(value)) {
        return false;
      }
      string v = value.Trim().Trim('\'', '"');
      return v.Equals("true", StringComparison.OrdinalIgnoreCase)
        || v.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsConfElementType(string dataTypeName) {
      return TypeNameEquals(dataTypeName, ConfElementType);
    }

    static bool IsArrayOfConfElement(string dataTypeName) {
      if (string.IsNullOrEmpty(dataTypeName)) {
        return false;
      }
      return Regex.IsMatch(
        dataTypeName,
        @"Array\s*\[.*\]\s*of\s*""?" + Regex.Escape(ConfElementType) + @"""?",
        RegexOptions.IgnoreCase);
    }

    static bool IsArrayOfHmiType(string dataTypeName) {
      if (string.IsNullOrEmpty(dataTypeName)) {
        return false;
      }
      foreach (string hmiType in HmiInterfaceTypes) {
        if (Regex.IsMatch(
          dataTypeName,
          @"Array\s*\[.*\]\s*of\s*""?" + Regex.Escape(hmiType) + @"""?",
          RegexOptions.IgnoreCase)) {
          return true;
        }
      }
      return false;
    }

    static bool IsPrimitiveOrSimple(string dataTypeName) {
      string n = (dataTypeName ?? "").Trim().Trim('"').ToUpperInvariant();
      return n == "BOOL" || n == "INT" || n == "DINT" || n == "UINT" || n == "UDINT"
        || n == "REAL" || n == "LREAL" || n == "BYTE" || n == "WORD" || n == "DWORD"
        || n == "CHAR" || n == "WCHAR" || n.StartsWith("STRING") || n.StartsWith("WSTRING")
        || n == "TIME" || n == "DATE" || n == "TOD" || n == "LTIME";
    }

    static bool TypeNameEquals(string dataTypeName, string expected) {
      if (string.IsNullOrEmpty(dataTypeName)) {
        return false;
      }
      string normalized = dataTypeName.Trim().Trim('"');
      return normalized.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    static string CombinePath(string prefix, string name) {
      if (string.IsNullOrEmpty(prefix)) {
        return name ?? "";
      }
      if (!string.IsNullOrEmpty(name) && name.StartsWith("[")) {
        return prefix + name;
      }
      return prefix + "." + name;
    }

    static int? ExtractFirstArrayIndex(string path) {
      if (string.IsNullOrEmpty(path)) {
        return null;
      }
      Match m = Regex.Match(path, @"\[(\d+)\]");
      if (m.Success) {
        int index;
        if (int.TryParse(m.Groups[1].Value, out index)) {
          return index;
        }
      }
      return null;
    }

    #endregion
  }
}
