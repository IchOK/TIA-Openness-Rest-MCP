using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Tophinke.TiaOpenness.Tool.Consts;
using Tophinke.TiaOpenness.Tool.Types.HMI;
using static Tophinke.TiaOpenness.Tool.TiaREST.HMI.cTiaHmi;
using static Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper.cTiaRequestHelpers;

namespace Tophinke.TiaOpenness.Tool.TiaREST.HMI {
  /// <summary>
  /// Unified-Bilder und ScreenItems: listen sowie Gruppen/Elemente mit BMK-Platzhalterersetzung kopieren.
  /// </summary>
  static internal class cTiaHmiScreens {
    const string DefaultTagPlaceholder = "tvPer";
    static readonly BindingFlags PropFlags = BindingFlags.Instance | BindingFlags.Public;
    static readonly string[] SkipAttributeNames = {
      "Name", "Parent", "CurrentQuality", "Dynamizations", "PropertyEventHandlers", "EventHandlers",
      "ScreenItems", "Interface"
    };

    /// <summary>
    /// Listet alle Bilder eines Unified-Geräts (inkl. Bildgruppen-Pfad).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts; optional, wenn nur eines vorhanden ist</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements; optional</query>
    /// <returns>JSON ListResult von ScreenInfo</returns>
    [RestRoute(HmiRoutes.ScreensList)]
    static public string ListScreens(HttpListenerContext context) {
      return ExecuteOnHmi(context, (project, target) => {
        var items = AllScreens(target.Software)
          .Select(s => new ScreenInfo { Name = s.Screen.Name, Path = s.Path })
          .OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
          .ToList();
        return ListResponse(context, target, items);
      });
    }

    /// <summary>
    /// Listet die ScreenItems eines Bildes (inkl. Typ, Tag-Bindungen und Kind-Elemente von Gruppen, soweit Openness sie exponiert).
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts; optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements; optional</query>
    /// <query name="screen">Bildname oder Pfad, z. B. Peripherie oder 0001 Controls/Peripherie</query>
    /// <query name="namePattern">Namensfilter mit * und ? (optional)</query>
    /// <returns>JSON ListResult von ScreenItemInfo</returns>
    [RestRoute(HmiRoutes.ScreenItemsList)]
    static public string ListItems(HttpListenerContext context) {
      string screenRef = context.Request.QueryString["screen"];
      if (string.IsNullOrWhiteSpace(screenRef)) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: screen (name or path) is required.");
      }
      Func<string, bool> filter = NameFilter(context.Request.QueryString["namePattern"]);
      return ExecuteOnHmi(context, (project, target) => {
        HmiScreen screen = FindScreen(target.Software, screenRef);
        if (screen == null) {
          return ErrorResponse(context, HttpStatusCode.NotFound, $"Error: Screen '{screenRef}' not found.");
        }
        var items = new List<ScreenItemInfo>();
        foreach (HmiScreenItemBase item in screen.ScreenItems) {
          if (!filter(item.Name)) {
            continue;
          }
          items.Add(DescribeItem(item));
        }
        return ListResponse(context, target, items);
      });
    }

    /// <summary>
    /// Kopiert ScreenItems (Gruppen/Elemente) von Quell- auf Zielbilder, ersetzt den Ersetztext
    /// (SourceElement) in Namen durch BMK und TagPlaceholder (Standard tvPer) in Tag-Bindungen durch BMK.
    /// Body: ScreenItemCopyRequest { TagPlaceholder?, Actions[{ SourceScreen, SourceElement, TargetScreen, Bmk, TagPlaceholder? }] }.
    /// </summary>
    /// <param name="context">HTTP-Anfrage-Kontext</param>
    /// <query name="processId">Prozess-ID des TIA-Projekts</query>
    /// <query name="projectName">Name des TIA-Projekts</query>
    /// <query name="deviceName">Name des WinCC-Unified-Geräts; optional</query>
    /// <query name="deviceItemName">Name des HMI-Geräteelements; optional</query>
    /// <body>ScreenItemCopyRequest mit TagPlaceholder? und Actions[{ SourceScreen, SourceElement, TargetScreen, Bmk, TagPlaceholder? }]</body>
    /// <returns>JSON ScreenItemCopyBatchResult</returns>
    [RestRoute(HmiRoutes.ScreenItemsCopy, "POST")]
    static public string CopyItems(HttpListenerContext context) {
      ScreenItemCopyRequest request = ReadRequest<ScreenItemCopyRequest>(context, out string error);
      if (request == null) {
        return error;
      }
      if (request.Actions == null || request.Actions.Count == 0) {
        return ErrorResponse(context, HttpStatusCode.BadRequest, "Error: Actions must contain at least one object.");
      }
      foreach (ScreenItemCopyAction action in request.Actions) {
        if (string.IsNullOrWhiteSpace(action?.SourceScreen)
            || string.IsNullOrWhiteSpace(action.SourceElement)
            || string.IsNullOrWhiteSpace(action.TargetScreen)
            || string.IsNullOrWhiteSpace(action.Bmk)) {
          return ErrorResponse(context, HttpStatusCode.BadRequest,
            "Error: Each action needs SourceScreen, SourceElement, TargetScreen and Bmk.");
        }
      }

      string defaultTagPlaceholder = string.IsNullOrWhiteSpace(request.TagPlaceholder)
        ? DefaultTagPlaceholder
        : request.TagPlaceholder.Trim();

      return ExecuteOnHmi(context, (project, target) => {
        var results = new List<ScreenItemCopyResult>();
        foreach (ScreenItemCopyAction action in request.Actions) {
          results.Add(CopyOne(target.Software, action, defaultTagPlaceholder));
        }
        if (results.Any(r => r.Error != null)) {
          context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        }
        context.Response.ContentType = "application/json";
        return JsonConvert.SerializeObject(new ScreenItemCopyBatchResult {
          DeviceName = target.Device.Name,
          DeviceItemName = target.DeviceItem.Name,
          HmiName = target.Software.Name,
          Results = results
        }, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
      });
    }

    static ScreenItemCopyResult CopyOne(HmiSoftware hmi, ScreenItemCopyAction action, string defaultTagPlaceholder) {
      string tagFrom = !string.IsNullOrWhiteSpace(action.TagPlaceholder)
        ? action.TagPlaceholder.Trim()
        : (defaultTagPlaceholder ?? DefaultTagPlaceholder);
      var result = new ScreenItemCopyResult {
        SourceScreen = action.SourceScreen,
        SourceElement = action.SourceElement,
        TargetScreen = action.TargetScreen,
        Bmk = action.Bmk,
        TagPlaceholder = tagFrom,
        Renames = new List<string>(),
        TagReplacements = new List<string>()
      };
      try {
        HmiScreen sourceScreen = FindScreen(hmi, action.SourceScreen)
          ?? throw new InvalidOperationException($"Source screen '{action.SourceScreen}' not found.");
        HmiScreen targetScreen = FindScreen(hmi, action.TargetScreen)
          ?? throw new InvalidOperationException($"Target screen '{action.TargetScreen}' not found.");
        // Openness exponiert UI-Gruppen nicht als HmiGroup, sondern als flache Items mit gemeinsamem Namenspräfix
        // (z. B. Analog_State, Analog_BMK). SourceElement ist der Ersetztext/Präfix.
        string nameFrom = action.SourceElement.Trim();
        string nameTo = action.Bmk.Trim();
        List<HmiScreenItemBase> sources = FindGroupItems(sourceScreen, nameFrom);
        if (sources.Count == 0) {
          throw new InvalidOperationException(
            $"No screen item named '{nameFrom}' or starting with '{nameFrom}_' on screen '{ScreenPath(hmi, sourceScreen)}'.");
        }

        var created = new List<string>();
        foreach (HmiScreenItemBase source in sources) {
          string newName = RemapToken(source.Name, nameFrom, nameTo);
          if (targetScreen.ScreenItems.Find(newName) != null) {
            throw new InvalidOperationException($"Target already contains an item named '{newName}'.");
          }
          HmiScreenItemBase copy = DeepCopyItem(source, targetScreen.ScreenItems, nameFrom, nameTo, tagFrom, nameTo, result);
          created.Add(copy.Name);
        }
        result.CreatedName = string.Join(", ", created);
        result.Action = "Copied";
        // Openness V20: UI-Objektgruppen (Group > Group) sind nicht erzeugbar/ansprechbar.
        // Create("Group"/HmiContainerBase) liefert "not supported"; Parent der Items bleibt HmiScreen.
        result.Grouped = false;
        result.GroupWarning =
          "TIA Openness (V20) unterstützt das Zusammenfassen von ScreenItems zu einer UI-Gruppe nicht. "
          + "Die Objekte wurden flach auf das Zielbild kopiert; bitte in TIA Portal manuell gruppieren "
          + "(Mehrfachauswahl → Gruppe > Gruppieren) und ggf. auf den BMK-Namen umbenennen.";
      } catch (Exception ex) {
        result.Action = "Failed";
        result.Error = ex.Message;
      }
      return result;
    }

    /// <summary>
    /// Erzeugt ein Zielobjekt gleichen Typs, kopiert Attribute/Dynamisierungen und Kind-Elemente rekursiv.
    /// </summary>
    static HmiScreenItemBase DeepCopyItem(
        HmiScreenItemBase source,
        HmiScreenItemBaseComposition targetParent,
        string nameFrom,
        string nameTo,
        string tagFrom,
        string tagTo,
        ScreenItemCopyResult result) {
      string newName = RemapToken(source.Name, nameFrom, nameTo);
      if (!string.Equals(source.Name, newName, StringComparison.Ordinal)) {
        result.Renames.Add($"{source.Name} -> {newName}");
      }

      HmiScreenItemBase copy = CreateLike(source, targetParent, newName);
      CopyAttributes(source, copy, tagFrom, tagTo, result);
      CopyDynamizations(source, copy, tagFrom, tagTo, result);
      RemapNestedStringAttributes(copy, tagFrom, tagTo, result);

      HmiScreenItemBaseComposition sourceChildren = GetChildItems(source);
      HmiScreenItemBaseComposition targetChildren = GetChildItems(copy);
      if (sourceChildren != null && targetChildren != null) {
        foreach (HmiScreenItemBase child in sourceChildren.ToList()) {
          DeepCopyItem(child, targetChildren, nameFrom, nameTo, tagFrom, tagTo, result);
        }
      } else if (sourceChildren != null && targetChildren == null) {
        throw new InvalidOperationException(
          $"Item '{source.Name}' ({source.GetType().Name}) has child ScreenItems, but the created copy does not expose a child composition. "
          + "Openness may not fully support copying this group type.");
      }
      return copy;
    }

    static HmiScreenItemBase CreateLike(HmiScreenItemBase source, HmiScreenItemBaseComposition targetParent, string newName) {
      Type type = source.GetType();
      string containedType = TryGetContainedType(source);

      // Create<T>(name) bzw. Create<T>(name, containedType)
      foreach (MethodInfo method in typeof(HmiScreenItemBaseComposition).GetMethods(PropFlags)) {
        if (method.Name != "Create" || !method.IsGenericMethodDefinition) {
          continue;
        }
        ParameterInfo[] parameters = method.GetParameters();
        try {
          if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string) && string.IsNullOrEmpty(containedType)) {
            MethodInfo generic = method.MakeGenericMethod(type);
            return (HmiScreenItemBase)generic.Invoke(targetParent, new object[] { newName });
          }
          if (parameters.Length == 2
              && parameters[0].ParameterType == typeof(string)
              && parameters[1].ParameterType == typeof(string)
              && !string.IsNullOrEmpty(containedType)) {
            MethodInfo generic = method.MakeGenericMethod(type);
            return (HmiScreenItemBase)generic.Invoke(targetParent, new object[] { newName, containedType });
          }
        } catch (TargetInvocationException ex) {
          throw new InvalidOperationException(
            $"Create<{type.Name}>(...) failed for '{newName}': {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
        }
      }

      // Fallback: Create(name) / Create(name, containedType) ohne Generics
      try {
        if (!string.IsNullOrEmpty(containedType)) {
          MethodInfo create2 = typeof(HmiScreenItemBaseComposition).GetMethod("Create", new[] { typeof(string), typeof(string) });
          if (create2 != null && !create2.IsGenericMethodDefinition) {
            return (HmiScreenItemBase)create2.Invoke(targetParent, new object[] { newName, containedType });
          }
        }
        MethodInfo create1 = typeof(HmiScreenItemBaseComposition).GetMethod("Create", new[] { typeof(string) });
        if (create1 != null && !create1.IsGenericMethodDefinition) {
          return (HmiScreenItemBase)create1.Invoke(targetParent, new object[] { newName });
        }
      } catch (TargetInvocationException ex) {
        throw new InvalidOperationException(
          $"Create(...) failed for '{newName}' ({type.Name}): {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
      }

      throw new InvalidOperationException(
        $"Cannot create screen item of type {type.Name}. Openness may not support this object (e.g. UI groups).");
    }

    static void CopyAttributes(
        HmiScreenItemBase source,
        HmiScreenItemBase target,
        string tagFrom,
        string tagTo,
        ScreenItemCopyResult result) {
      IList<EngineeringAttributeInfo> infos;
      try {
        infos = ((IEngineeringObject)source).GetAttributeInfos();
      } catch {
        return;
      }
      var pairs = new List<KeyValuePair<string, object>>();
      foreach (EngineeringAttributeInfo info in infos) {
        if (info == null || string.IsNullOrEmpty(info.Name)) {
          continue;
        }
        if (SkipAttributeNames.Any(s => string.Equals(s, info.Name, StringComparison.OrdinalIgnoreCase))) {
          continue;
        }
        if (info.AccessMode == EngineeringAttributeAccessMode.Read) {
          continue;
        }
        object value;
        try {
          value = ((IEngineeringObject)source).GetAttribute(info.Name);
        } catch {
          continue;
        }
        if (value == null || value is IEngineeringObject || value is IEnumerable && !(value is string)) {
          continue;
        }
        if (value is string text) {
          string mapped = RemapToken(text, tagFrom, tagTo);
          if (!string.Equals(text, mapped, StringComparison.Ordinal)) {
            result.TagReplacements.Add($"{source.Name}.{info.Name}: {text} -> {mapped}");
          }
          value = mapped;
        }
        pairs.Add(new KeyValuePair<string, object>(info.Name, value));
      }
      if (pairs.Count == 0) {
        return;
      }
      try {
        ((IEngineeringObject)target).SetAttributes(pairs);
      } catch {
        foreach (KeyValuePair<string, object> pair in pairs) {
          try {
            ((IEngineeringObject)target).SetAttribute(pair.Key, pair.Value);
          } catch {
            // einzelne Attribute können typspezifisch schreibgeschützt sein
          }
        }
      }
    }

    /// <summary>
    /// Ersetzt Platzhalter in String-Attributen verschachtelter Openness-Objekte (z. B. Faceplate-Interface).
    /// </summary>
    static void RemapNestedStringAttributes(
        IEngineeringObject root,
        string tagFrom,
        string tagTo,
        ScreenItemCopyResult result) {
      var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
      var queue = new Queue<object>();
      queue.Enqueue(root);
      int depthGuard = 0;
      while (queue.Count > 0 && depthGuard++ < 500) {
        object current = queue.Dequeue();
        if (current == null || !seen.Add(current)) {
          continue;
        }
        if (!(current is IEngineeringObject eng)) {
          continue;
        }
        try {
          foreach (EngineeringAttributeInfo info in eng.GetAttributeInfos()) {
            if (info == null || info.AccessMode == EngineeringAttributeAccessMode.Read) {
              continue;
            }
            if (string.Equals(info.Name, "Name", StringComparison.OrdinalIgnoreCase)
                || string.Equals(info.Name, "Parent", StringComparison.OrdinalIgnoreCase)) {
              continue;
            }
            object value;
            try {
              value = eng.GetAttribute(info.Name);
            } catch {
              continue;
            }
            if (value is string text) {
              string mapped = RemapToken(text, tagFrom, tagTo);
              if (!string.Equals(text, mapped, StringComparison.Ordinal)) {
                try {
                  eng.SetAttribute(info.Name, mapped);
                  result.TagReplacements.Add($"{info.Name}: {text} -> {mapped}");
                } catch { /* ignore */ }
              }
            }
          }
        } catch { /* ignore */ }
        try {
          foreach (EngineeringCompositionInfo comp in eng.GetCompositionInfos()) {
            object composition;
            try {
              composition = eng.GetComposition(comp.Name);
            } catch {
              continue;
            }
            if (composition is IEnumerable enumerable && !(composition is string)) {
              foreach (object child in enumerable) {
                if (child is IEngineeringObject) {
                  queue.Enqueue(child);
                }
              }
            } else if (composition is IEngineeringObject childObj) {
              queue.Enqueue(childObj);
            }
          }
        } catch { /* ignore */ }
      }
    }

    sealed class ReferenceEqualityComparer : IEqualityComparer<object> {
      public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
      public new bool Equals(object x, object y) => ReferenceEquals(x, y);
      public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    static void CopyDynamizations(
        HmiScreenItemBase source,
        HmiScreenItemBase target,
        string tagFrom,
        string tagTo,
        ScreenItemCopyResult result) {
      if (source.Dynamizations == null || target.Dynamizations == null) {
        return;
      }
      foreach (DynamizationBase dyn in source.Dynamizations.ToList()) {
        if (!(dyn is TagDynamization tagDyn)) {
          // Scripts/Expressions: PropertyName übernehmen und String-Attribute mit Platzhalter ersetzen, soweit möglich
          try {
            DynamizationBase created = CreateDynamizationLike(target.Dynamizations, dyn);
            if (created != null) {
              CopyDynamizationAttributes(dyn, created, tagFrom, tagTo, result);
            }
          } catch {
            // nicht jede Dynamisierungsart lässt sich über Create erzeugen
          }
          continue;
        }
        TagDynamization createdTag;
        try {
          createdTag = target.Dynamizations.Create<TagDynamization>(tagDyn.PropertyName);
        } catch {
          continue;
        }
        string oldTag = tagDyn.Tag;
        string newTag = RemapToken(oldTag, tagFrom, tagTo);
        try {
          createdTag.Tag = newTag;
          if (!string.Equals(oldTag, newTag, StringComparison.Ordinal) && !string.IsNullOrEmpty(oldTag)) {
            result.TagReplacements.Add($"{tagDyn.PropertyName}: {oldTag} -> {newTag}");
          }
        } catch {
          // ignore
        }
        try {
          createdTag.UseIndirectAddressing = tagDyn.UseIndirectAddressing;
        } catch { /* ignore */ }
      }
    }

    static DynamizationBase CreateDynamizationLike(DynamizationBaseComposition target, DynamizationBase source) {
      Type type = source.GetType();
      foreach (MethodInfo method in typeof(DynamizationBaseComposition).GetMethods(PropFlags)) {
        if (method.Name != "Create" || !method.IsGenericMethodDefinition) {
          continue;
        }
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) {
          continue;
        }
        try {
          MethodInfo generic = method.MakeGenericMethod(type);
          return (DynamizationBase)generic.Invoke(target, new object[] { source.PropertyName });
        } catch {
          return null;
        }
      }
      return null;
    }

    static void CopyDynamizationAttributes(
        DynamizationBase source,
        DynamizationBase target,
        string tagFrom,
        string tagTo,
        ScreenItemCopyResult result) {
      IList<EngineeringAttributeInfo> infos;
      try {
        infos = ((IEngineeringObject)source).GetAttributeInfos();
      } catch {
        return;
      }
      foreach (EngineeringAttributeInfo info in infos) {
        if (info == null || info.AccessMode == EngineeringAttributeAccessMode.Read) {
          continue;
        }
        if (string.Equals(info.Name, "PropertyName", StringComparison.OrdinalIgnoreCase)
            || string.Equals(info.Name, "Parent", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
        object value;
        try {
          value = ((IEngineeringObject)source).GetAttribute(info.Name);
        } catch {
          continue;
        }
        if (value is string text) {
          string mapped = RemapToken(text, tagFrom, tagTo);
          if (!string.Equals(text, mapped, StringComparison.Ordinal)) {
            result.TagReplacements.Add($"{source.PropertyName}.{info.Name}: {text} -> {mapped}");
          }
          try {
            ((IEngineeringObject)target).SetAttribute(info.Name, mapped);
          } catch { /* ignore */ }
        } else if (value != null && !(value is IEngineeringObject) && !(value is IEnumerable)) {
          try {
            ((IEngineeringObject)target).SetAttribute(info.Name, value);
          } catch { /* ignore */ }
        }
      }
    }

    static ScreenItemInfo DescribeItem(HmiScreenItemBase item) {
      var info = new ScreenItemInfo {
        Name = item.Name,
        Type = item.GetType().Name,
        ContainedType = TryGetContainedType(item),
        Compositions = SafeCompositionNames(item),
        TagBindings = CollectTagBindings(item),
        Children = new List<ScreenItemInfo>()
      };
      HmiScreenItemBaseComposition children = GetChildItems(item);
      if (children != null) {
        foreach (HmiScreenItemBase child in children) {
          info.Children.Add(DescribeItem(child));
        }
      }
      if (info.Children.Count == 0) {
        info.Children = null;
      }
      if (info.Compositions != null && info.Compositions.Count == 0) {
        info.Compositions = null;
      }
      if (info.TagBindings != null && info.TagBindings.Count == 0) {
        info.TagBindings = null;
      }
      return info;
    }

    static List<string> CollectTagBindings(HmiScreenItemBase item) {
      var tags = new List<string>();
      if (item.Dynamizations == null) {
        return tags;
      }
      foreach (DynamizationBase dyn in item.Dynamizations) {
        if (dyn is TagDynamization tagDyn && !string.IsNullOrEmpty(tagDyn.Tag)) {
          tags.Add($"{tagDyn.PropertyName}={tagDyn.Tag}");
        }
      }
      return tags;
    }

    static List<string> SafeCompositionNames(HmiScreenItemBase item) {
      try {
        return ((IEngineeringObject)item).GetCompositionInfos().Select(i => i.Name).ToList();
      } catch {
        return new List<string>();
      }
    }

    static HmiScreenItemBaseComposition GetChildItems(HmiScreenItemBase item) {
      // Öffentliche Property ScreenItems (falls vorhanden)
      PropertyInfo prop = item.GetType().GetProperty("ScreenItems", PropFlags);
      if (prop != null && typeof(HmiScreenItemBaseComposition).IsAssignableFrom(prop.PropertyType)) {
        try {
          return prop.GetValue(item) as HmiScreenItemBaseComposition;
        } catch {
          // fall through
        }
      }
      // Composition über IEngineeringObject
      try {
        object composition = ((IEngineeringObject)item).GetComposition("ScreenItems");
        return composition as HmiScreenItemBaseComposition;
      } catch {
        return null;
      }
    }

    static string TryGetContainedType(HmiScreenItemBase item) {
      PropertyInfo prop = item.GetType().GetProperty("ContainedType", PropFlags);
      if (prop == null || prop.PropertyType != typeof(string)) {
        return null;
      }
      try {
        return prop.GetValue(item) as string;
      } catch {
        return null;
      }
    }

    static HmiScreen FindScreen(HmiSoftware hmi, string screenRef) {
      string want = (screenRef ?? "").Trim().Replace('\\', '/');
      if (want.Length == 0) {
        return null;
      }
      List<(HmiScreen Screen, string Path)> all = AllScreens(hmi).ToList();
      (HmiScreen Screen, string Path) exactPath = all.FirstOrDefault(s =>
        string.Equals(s.Path, want, StringComparison.OrdinalIgnoreCase));
      if (exactPath.Screen != null) {
        return exactPath.Screen;
      }
      (HmiScreen Screen, string Path) byName = all.FirstOrDefault(s =>
        string.Equals(s.Screen.Name, want, StringComparison.OrdinalIgnoreCase));
      if (byName.Screen != null) {
        return byName.Screen;
      }
      // Pfad-Ende: ".../Peripherie"
      return all.FirstOrDefault(s =>
        s.Path.EndsWith("/" + want, StringComparison.OrdinalIgnoreCase)).Screen;
    }

    /// <summary>
    /// Alle Items der logischen Gruppe: exakter Name oder Präfix "token_".
    /// </summary>
    static List<HmiScreenItemBase> FindGroupItems(HmiScreen screen, string token) {
      var items = new List<HmiScreenItemBase>();
      string prefix = token + "_";
      foreach (HmiScreenItemBase item in screen.ScreenItems) {
        if (string.Equals(item.Name, token, StringComparison.OrdinalIgnoreCase)
            || item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
          items.Add(item);
        }
      }
      // Nested (falls Openness doch Gruppen mit Children liefert)
      foreach (HmiScreenItemBase item in screen.ScreenItems.ToList()) {
        CollectNestedGroupItems(item, token, prefix, items);
      }
      return items;
    }

    static void CollectNestedGroupItems(HmiScreenItemBase item, string token, string prefix, List<HmiScreenItemBase> items) {
      HmiScreenItemBaseComposition children = GetChildItems(item);
      if (children == null) {
        return;
      }
      foreach (HmiScreenItemBase child in children) {
        if ((string.Equals(child.Name, token, StringComparison.OrdinalIgnoreCase)
            || child.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            && !items.Any(i => ReferenceEquals(i, child))) {
          items.Add(child);
        }
        CollectNestedGroupItems(child, token, prefix, items);
      }
    }

    static string ScreenPath(HmiSoftware hmi, HmiScreen screen) {
      return AllScreens(hmi).FirstOrDefault(s => ReferenceEquals(s.Screen, screen)).Path ?? screen.Name;
    }

    static IEnumerable<(HmiScreen Screen, string Path)> AllScreens(HmiSoftware hmi) {
      foreach (HmiScreen screen in hmi.Screens) {
        yield return (screen, screen.Name);
      }
      var stack = new Stack<(HmiScreenGroup Group, string Path)>();
      foreach (HmiScreenGroup group in hmi.ScreenGroups) {
        stack.Push((group, group.Name));
      }
      while (stack.Count > 0) {
        var entry = stack.Pop();
        foreach (HmiScreen screen in entry.Group.Screens) {
          yield return (screen, entry.Path + "/" + screen.Name);
        }
        foreach (HmiScreenGroup child in entry.Group.Groups) {
          stack.Push((child, entry.Path + "/" + child.Name));
        }
      }
    }

    /// <summary>
    /// Ersetzt Platzhalter tokenFrom durch tokenTo in Namen/Tags.
    /// Ganztreffer: exakter Name. Sonst: Präfix "tokenFrom_" → "tokenTo_" und sonstige Vorkommen.
    /// </summary>
    static string RemapToken(string value, string tokenFrom, string tokenTo) {
      if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(tokenFrom)) {
        return value;
      }
      if (string.Equals(value, tokenFrom, StringComparison.OrdinalIgnoreCase)) {
        return tokenTo;
      }
      string fromPrefix = tokenFrom + "_";
      string toPrefix = tokenTo + "_";
      if (value.StartsWith(fromPrefix, StringComparison.OrdinalIgnoreCase)) {
        return toPrefix + value.Substring(fromPrefix.Length);
      }
      return ReplaceIgnoreCase(value, tokenFrom, tokenTo);
    }

    static string ReplaceIgnoreCase(string input, string oldValue, string newValue) {
      if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue)) {
        return input;
      }
      int index = 0;
      var sb = new System.Text.StringBuilder();
      while (index < input.Length) {
        int found = input.IndexOf(oldValue, index, StringComparison.OrdinalIgnoreCase);
        if (found < 0) {
          sb.Append(input.Substring(index));
          break;
        }
        sb.Append(input, index, found - index);
        sb.Append(newValue);
        index = found + oldValue.Length;
      }
      return sb.ToString();
    }
  }
}
