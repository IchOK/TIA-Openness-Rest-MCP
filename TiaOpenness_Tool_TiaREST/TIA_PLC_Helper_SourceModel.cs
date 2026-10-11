using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Tophinke.TiaOpenness.Tool.TiaREST.PLC.Helper {
  /// <summary>
  /// Deklaration einer Variablen (Schnittstelle eines Bausteins, Member einer Struktur oder eines UDT).
  /// </summary>
  internal class SourceDecl {
    /// <summary>Name der Variablen.</summary>
    public string Name;

    /// <summary>Schnittstellenbereich auf oberster Ebene: Input, Output, InOut, Static, Temp, Constant; sonst null.</summary>
    public string Section;

    /// <summary>Datentyp ohne Namensraum-Präfix und Anführungszeichen ("Struct" für STRUCT); bei Arrays der Elementtyp. Null bei reinen Startwert-Überschreibungen.</summary>
    public string TypeName;

    /// <summary>Array-Grenzen je Dimension (Low, High); null, wenn die Variable kein Array ist. int.MinValue für unbekannte Grenzen.</summary>
    public List<int[]> Bounds;

    /// <summary>Kommentar je Sprache (z. B. "de-DE").</summary>
    public Dictionary<string, string> Comments;

    /// <summary>Startwert als Text (Konstanten ohne Namensraum-Präfix).</summary>
    public string StartValue;

    /// <summary>Unterelemente (STRUCT-Member oder Startwert-Überschreibungen für UDT-Member).</summary>
    public List<SourceDecl> Children = new List<SourceDecl>();

    public SourceDecl Child(string name) {
      return Children.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public SourceDecl GetOrAddChild(string name) {
      SourceDecl child = Child(name);
      if (child == null) {
        child = new SourceDecl { Name = name };
        Children.Add(child);
      }
      return child;
    }
  }

  /// <summary>
  /// Segment eines Operanden: Name und optionale Array-Indizes.
  /// </summary>
  internal class SourceSegment {
    public string Name;
    public List<string> Indices = new List<string>();

    public SourceSegment Clone() {
      return new SourceSegment { Name = Name, Indices = new List<string>(Indices) };
    }
  }

  /// <summary>
  /// Einfacher Operand wie "DB".a.b[3] oder #a.b[#i].
  /// </summary>
  internal class SourceOperand {
    /// <summary>Das erste Segment ist ein globaler Name (Datenbaustein, Instanz-DB).</summary>
    public bool Global;

    /// <summary>Das erste Segment ist eine lokale Variable (#name).</summary>
    public bool Local;

    public List<SourceSegment> Segments = new List<SourceSegment>();

    public string Root => Segments.Count > 0 ? Segments[0].Name : null;

    public SourceOperand Clone() {
      return new SourceOperand { Global = Global, Local = Local, Segments = Segments.Select(s => s.Clone()).ToList() };
    }

    /// <summary>Hängt Member und Indizes an eine Kopie des Operanden an.</summary>
    public SourceOperand Append(IEnumerable<SourceSegment> segments) {
      SourceOperand result = Clone();
      result.Segments.AddRange(segments.Select(s => s.Clone()));
      return result;
    }

    /// <summary>Hängt einen Member mit optionalem Index an eine Kopie des Operanden an.</summary>
    public SourceOperand Append(string name, params string[] indices) {
      SourceOperand result = Clone();
      var segment = new SourceSegment { Name = name };
      segment.Indices.AddRange(indices);
      result.Segments.Add(segment);
      return result;
    }

    /// <summary>Hängt einen Index an das letzte Segment einer Kopie des Operanden an.</summary>
    public SourceOperand AppendIndex(string index) {
      SourceOperand result = Clone();
      result.Segments[result.Segments.Count - 1].Indices.Add(index);
      return result;
    }

    /// <summary>Member-Pfad ohne das erste Segment (z. B. "SstMsg.stHwFault").</summary>
    public string MemberPath => FormatSegments(Segments.Skip(1), false);

    /// <summary>Vergleichsschlüssel (Groß-/Kleinschreibung ignoriert).</summary>
    public string Key => ToString().ToUpperInvariant();

    public override string ToString() {
      if (Segments.Count == 0) {
        return "";
      }
      var sb = new StringBuilder();
      SourceSegment first = Segments[0];
      if (Global) {
        sb.Append('"').Append(first.Name).Append('"');
      } else if (Local) {
        sb.Append('#').Append(first.Name);
      } else {
        sb.Append(first.Name);
      }
      AppendIndices(sb, first);
      foreach (SourceSegment segment in Segments.Skip(1)) {
        sb.Append('.').Append(segment.Name);
        AppendIndices(sb, segment);
      }
      return sb.ToString();
    }

    static string FormatSegments(IEnumerable<SourceSegment> segments, bool leadingDot) {
      var sb = new StringBuilder();
      foreach (SourceSegment segment in segments) {
        if (sb.Length > 0 || leadingDot) {
          sb.Append('.');
        }
        sb.Append(segment.Name);
        AppendIndices(sb, segment);
      }
      return sb.ToString();
    }

    static void AppendIndices(StringBuilder sb, SourceSegment segment) {
      if (segment.Indices.Count > 0) {
        sb.Append('[').Append(string.Join(",", segment.Indices)).Append(']');
      }
    }

    /// <summary>Globaler Operand aus einem Baustein-Namen.</summary>
    static public SourceOperand ForBlock(string blockName) {
      var op = new SourceOperand { Global = true };
      op.Segments.Add(new SourceSegment { Name = blockName });
      return op;
    }
  }

  /// <summary>
  /// Aufruf eines Bausteins (FC-Aufruf, Instanzaufruf oder Multiinstanzaufruf) mit seinen Parametern.
  /// </summary>
  internal class SourceCall {
    /// <summary>Aufgerufener Name: "FC" bzw. "Instanz-DB" (global) oder #Multiinstanz (lokal).</summary>
    public SourceOperand Callee;

    /// <summary>Typ des aufgerufenen Bausteins, falls im Export angegeben (nur XML).</summary>
    public string CalleeTypeName;

    /// <summary>Parameter in Aufrufreihenfolge; Operand ist null, wenn der Wert kein einfacher Operand ist.</summary>
    public List<KeyValuePair<string, SourceOperand>> Args = new List<KeyValuePair<string, SourceOperand>>();

    public SourceOperand Arg(params string[] names) {
      foreach (var arg in Args) {
        if (names.Any(n => string.Equals(n, arg.Key, StringComparison.OrdinalIgnoreCase))) {
          return arg.Value;
        }
      }
      return null;
    }
  }

  /// <summary>
  /// Aus einem SD- oder SimaticML-Export gelesenes Modell eines Bausteins oder Datentyps.
  /// </summary>
  internal class SourceModel {
    /// <summary>Art: FUNCTION_BLOCK, FUNCTION, ORGANIZATION_BLOCK, DATA_BLOCK oder TYPE.</summary>
    public string Kind;

    public string Name;

    /// <summary>Bei Instanz-DBs der FB, bei typisierten DBs der Datentyp.</summary>
    public string InstanceOf;

    /// <summary>Deklarationen auf oberster Ebene (bei TYPE die Member der Struktur).</summary>
    public List<SourceDecl> Decls = new List<SourceDecl>();

    /// <summary>Startwerte aus dem Datenbereich eines DBs (Pfad relativ zum DB, z. B. "SstMsg.stX.stConfig.xSelect").</summary>
    public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public List<SourceCall> Calls = new List<SourceCall>();

    /// <summary>Zuweisungen der Form Operand := Operand (nur SCL).</summary>
    public List<KeyValuePair<SourceOperand, SourceOperand>> Assignments = new List<KeyValuePair<SourceOperand, SourceOperand>>();

    public SourceDecl Decl(string name) {
      return Decls.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    }
  }

  /// <summary>
  /// Liest SIMATIC-SD-Dokumente (.s7dcl/.s7res) und SimaticML-Exporte in ein SourceModel.
  /// Ausgewertet werden Deklarationen, Kommentare, Startwerte und Bausteinaufrufe; Programmlogik wird nicht interpretiert.
  /// </summary>
  static internal class cSourceParser {
    #region Tokenizer

    internal enum TokKind { Ident, Local, Quoted, Literal, Punct }

    internal class Tok {
      public TokKind Kind;
      public string Text;

      public bool Is(string text) {
        return (Kind == TokKind.Punct || Kind == TokKind.Ident) && string.Equals(Text, text, StringComparison.OrdinalIgnoreCase);
      }

      public override string ToString() {
        switch (Kind) {
          case TokKind.Local: return "#" + Text;
          case TokKind.Quoted: return "\"" + Text + "\"";
          default: return Text;
        }
      }
    }

    static readonly string[] TwoCharPuncts = { ":=", "=>", "..", "<>", "<=", ">=", "**" };

    static internal List<Tok> Tokenize(string text) {
      var tokens = new List<Tok>();
      int i = 0;
      int n = text.Length;
      while (i < n) {
        char c = text[i];
        if (char.IsWhiteSpace(c)) {
          i++;
          continue;
        }
        if (c == '/' && i + 1 < n && text[i + 1] == '/') {
          while (i < n && text[i] != '\n') i++;
          continue;
        }
        if (c == '(' && i + 1 < n && text[i + 1] == '*') {
          int end = text.IndexOf("*)", i + 2, StringComparison.Ordinal);
          i = end < 0 ? n : end + 2;
          continue;
        }
        if (c == '/' && i + 1 < n && text[i + 1] == '*') {
          int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
          i = end < 0 ? n : end + 2;
          continue;
        }
        if (c == '"') {
          int end = text.IndexOf('"', i + 1);
          if (end < 0) end = n;
          tokens.Add(new Tok { Kind = TokKind.Quoted, Text = text.Substring(i + 1, Math.Max(0, end - i - 1)) });
          i = end + 1;
          continue;
        }
        if (c == '\'') {
          int start = i;
          i = SkipString(text, i);
          tokens.Add(new Tok { Kind = TokKind.Literal, Text = text.Substring(start, i - start) });
          continue;
        }
        if (c == '#') {
          if (i + 1 < n && text[i + 1] == '"') {
            int end = text.IndexOf('"', i + 2);
            if (end < 0) end = n;
            tokens.Add(new Tok { Kind = TokKind.Local, Text = text.Substring(i + 2, Math.Max(0, end - i - 2)) });
            i = end + 1;
            continue;
          }
          if (i + 1 < n && IsIdentStart(text[i + 1])) {
            int start = i + 1;
            i = start;
            while (i < n && IsIdentPart(text[i])) i++;
            tokens.Add(new Tok { Kind = TokKind.Local, Text = text.Substring(start, i - start) });
            continue;
          }
        }
        if (IsIdentStart(c)) {
          int start = i;
          while (i < n && IsIdentPart(text[i])) i++;
          if (i < n && text[i] == '#') {
            // Typisierte Konstante wie WSTRING#'x', T#1s, DINT#5
            i++;
            i = i < n && text[i] == '\'' ? SkipString(text, i) : SkipLiteralChars(text, i);
            tokens.Add(new Tok { Kind = TokKind.Literal, Text = text.Substring(start, i - start) });
            continue;
          }
          tokens.Add(new Tok { Kind = TokKind.Ident, Text = text.Substring(start, i - start) });
          continue;
        }
        if (char.IsDigit(c)) {
          int start = i;
          while (i < n && (char.IsDigit(text[i]) || text[i] == '_')) i++;
          if (i < n && text[i] == '#') {
            i = SkipLiteralChars(text, i + 1);
          } else {
            if (i + 1 < n && text[i] == '.' && char.IsDigit(text[i + 1])) {
              i++;
              while (i < n && (char.IsDigit(text[i]) || text[i] == '_')) i++;
            }
            if (i < n && (text[i] == 'e' || text[i] == 'E') && i + 1 < n && (char.IsDigit(text[i + 1]) || text[i + 1] == '-' || text[i + 1] == '+')) {
              i += 2;
              while (i < n && char.IsDigit(text[i])) i++;
            }
          }
          tokens.Add(new Tok { Kind = TokKind.Literal, Text = text.Substring(start, i - start) });
          continue;
        }
        string two = i + 1 < n ? text.Substring(i, 2) : null;
        if (two != null && TwoCharPuncts.Contains(two)) {
          tokens.Add(new Tok { Kind = TokKind.Punct, Text = two });
          i += 2;
          continue;
        }
        tokens.Add(new Tok { Kind = TokKind.Punct, Text = c.ToString() });
        i++;
      }
      return tokens;
    }

    static bool IsIdentStart(char c) {
      return char.IsLetter(c) || c == '_';
    }

    static bool IsIdentPart(char c) {
      return char.IsLetterOrDigit(c) || c == '_';
    }

    static int SkipString(string text, int i) {
      i++;
      while (i < text.Length) {
        if (text[i] == '$' && i + 1 < text.Length) {
          i += 2;
          continue;
        }
        if (text[i] == '\'') {
          if (i + 1 < text.Length && text[i + 1] == '\'') {
            i += 2;
            continue;
          }
          return i + 1;
        }
        i++;
      }
      return i;
    }

    static int SkipLiteralChars(string text, int i) {
      while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.' || text[i] == ':' || text[i] == '-' || text[i] == '+')) {
        if (text[i] == '.' && i + 1 < text.Length && text[i + 1] == '.') {
          break;
        }
        i++;
      }
      return i;
    }

    #endregion

    #region SD-Parser

    /// <summary>
    /// Liest ein SD-Dokument (.s7dcl) mit den mehrsprachigen Texten (.s7res).
    /// </summary>
    static public SourceModel ParseSd(string content, string multiLingualText) {
      var parser = new SdParser(Tokenize(content ?? ""), ParseMultiLingualTexts(multiLingualText));
      return parser.Parse();
    }

    /// <summary>
    /// Liest die YAML-Datei der mehrsprachigen Texte: "- id: MLC_x" gefolgt von "de-DE: Text".
    /// </summary>
    static public Dictionary<string, Dictionary<string, string>> ParseMultiLingualTexts(string yaml) {
      var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
      if (string.IsNullOrEmpty(yaml)) {
        return result;
      }
      string[] lines = yaml.Replace("\r\n", "\n").Split('\n');
      Dictionary<string, string> current = null;
      for (int i = 0; i < lines.Length; i++) {
        string line = lines[i];
        string trimmed = line.Trim();
        if (trimmed.StartsWith("- id:")) {
          string id = UnquoteYaml(trimmed.Substring(5).Trim());
          current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
          result[id] = current;
          continue;
        }
        if (current == null) {
          continue;
        }
        int colon = trimmed.IndexOf(':');
        if (colon <= 0) {
          continue;
        }
        string lang = trimmed.Substring(0, colon).Trim();
        string value = trimmed.Substring(colon + 1).Trim();
        if (value == "|" || value == ">" || value == "|-" || value == ">-") {
          int indent = line.Length - line.TrimStart().Length;
          var block = new List<string>();
          while (i + 1 < lines.Length && (lines[i + 1].Trim().Length == 0 || lines[i + 1].Length - lines[i + 1].TrimStart().Length > indent)) {
            i++;
            block.Add(lines[i].Trim());
          }
          value = string.Join(value.StartsWith(">") ? " " : "\n", block).Trim();
        } else {
          value = UnquoteYaml(value);
        }
        current[lang] = value;
      }
      return result;
    }

    static string UnquoteYaml(string value) {
      if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'') {
        return value.Substring(1, value.Length - 2).Replace("''", "'");
      }
      if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"') {
        return value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
      }
      return value;
    }

    class SdParser {
      readonly List<Tok> _t;
      readonly Dictionary<string, Dictionary<string, string>> _texts;
      int _p;

      public SdParser(List<Tok> tokens, Dictionary<string, Dictionary<string, string>> texts) {
        _t = tokens;
        _texts = texts;
      }

      Tok Peek(int offset = 0) {
        int i = _p + offset;
        return i < _t.Count ? _t[i] : null;
      }

      Tok Next() {
        return _p < _t.Count ? _t[_p++] : null;
      }

      bool At(string text) {
        Tok tok = Peek();
        return tok != null && tok.Is(text);
      }

      bool Accept(string text) {
        if (At(text)) {
          _p++;
          return true;
        }
        return false;
      }

      bool Eof => _p >= _t.Count;

      public SourceModel Parse() {
        var model = new SourceModel();
        while (!Eof) {
          if (At("{")) {
            ParseAttributes();
            continue;
          }
          Tok tok = Next();
          if (tok.Is("TYPE")) {
            ParseType(model);
          } else if (tok.Is("DATA_BLOCK")) {
            ParseDataBlock(model);
          } else if (tok.Is("FUNCTION_BLOCK") || tok.Is("FUNCTION") || tok.Is("ORGANIZATION_BLOCK")) {
            ParseCodeBlock(model, tok.Text.ToUpperInvariant());
          }
        }
        return model;
      }

      void ParseType(SourceModel model) {
        model.Kind = "TYPE";
        Dictionary<string, string> attributes = At("{") ? ParseAttributes() : null;
        SourceDecl root = ParseMember(attributes);
        if (root != null) {
          model.Name = root.Name;
          model.Decls = root.Children;
        }
        while (!Eof && !Accept("END_TYPE")) {
          Next();
        }
      }

      void ParseDataBlock(SourceModel model) {
        model.Kind = "DATA_BLOCK";
        model.Name = NameText(Next());
        if (Accept(":")) {
          model.InstanceOf = ReadTypeName(new[] { "VAR", "BEGIN", "END_DATA_BLOCK", "{" }, stopAtLineStartIdent: true);
        }
        while (!Eof) {
          if (At("{")) {
            ParseAttributes();
          } else if (Accept("END_DATA_BLOCK")) {
            return;
          } else if (Accept("BEGIN")) {
            continue;
          } else if (IsSectionStart()) {
            ParseSection(model);
          } else {
            ParseValueAssignment(model);
          }
        }
      }

      void ParseCodeBlock(SourceModel model, string kind) {
        model.Kind = kind;
        model.Name = NameText(Next());
        if (kind == "FUNCTION" && Accept(":")) {
          Next();
        }
        while (!Eof) {
          if (At("{")) {
            ParseAttributes();
          } else if (IsSectionStart()) {
            ParseSection(model);
          } else if (At("NETWORK") || At("BEGIN")) {
            break;
          } else if (At("END_FUNCTION_BLOCK") || At("END_FUNCTION") || At("END_ORGANIZATION_BLOCK")) {
            return;
          } else {
            Next();
          }
        }
        model.Calls.AddRange(ParseCalls(_t, _p, _t.Count));
        model.Assignments.AddRange(ParseAssignments(_t, _p, _t.Count));
        _p = _t.Count;
      }

      bool IsSectionStart() {
        Tok tok = Peek();
        return tok != null && tok.Kind == TokKind.Ident && tok.Text.StartsWith("VAR", StringComparison.OrdinalIgnoreCase)
          && !tok.Text.Equals("VARIANT", StringComparison.OrdinalIgnoreCase);
      }

      void ParseSection(SourceModel model) {
        string keyword = Next().Text.ToUpperInvariant();
        string section;
        switch (keyword) {
          case "VAR_INPUT": section = "Input"; break;
          case "VAR_OUTPUT": section = "Output"; break;
          case "VAR_IN_OUT": section = "InOut"; break;
          case "VAR_TEMP": section = "Temp"; break;
          default: section = "Static"; break;
        }
        while (At("RETAIN") || At("NON_RETAIN") || At("CONSTANT") || At("DB_SPECIFIC")) {
          if (Next().Is("CONSTANT")) {
            section = "Constant";
          }
        }
        foreach (SourceDecl decl in ParseMemberList("END_VAR")) {
          decl.Section = section;
          model.Decls.Add(decl);
        }
      }

      List<SourceDecl> ParseMemberList(string endKeyword) {
        var members = new List<SourceDecl>();
        while (!Eof) {
          if (Accept(endKeyword)) {
            break;
          }
          Dictionary<string, string> attributes = At("{") ? ParseAttributes() : null;
          if (Accept(endKeyword)) {
            break;
          }
          SourceDecl member = ParseMember(attributes);
          if (member != null) {
            members.Add(member);
          }
        }
        return members;
      }

      SourceDecl ParseMember(Dictionary<string, string> attributes) {
        Tok nameTok = Next();
        if (nameTok == null) {
          return null;
        }
        if (At("{")) {
          var more = ParseAttributes();
          attributes = attributes ?? more;
        }
        if (!Accept(":")) {
          SkipTo(";");
          return null;
        }
        var decl = new SourceDecl { Name = NameText(nameTok) };
        ApplyAttributes(decl, attributes);
        ParseTypeSpec(decl);
        if (Accept(":=")) {
          ParseInit(decl);
        }
        SkipTo(";");
        return decl;
      }

      void ApplyAttributes(SourceDecl decl, Dictionary<string, string> attributes) {
        if (attributes == null) {
          return;
        }
        if (attributes.TryGetValue("S7_MLC", out string id) && _texts.TryGetValue(id, out var texts)) {
          decl.Comments = new Dictionary<string, string>(texts, StringComparer.OrdinalIgnoreCase);
        }
      }

      void ParseTypeSpec(SourceDecl decl) {
        if (Accept("STRUCT")) {
          decl.TypeName = "Struct";
          decl.Children = ParseMemberList("END_STRUCT");
          return;
        }
        if (At("Array") && Peek(1) != null && Peek(1).Is("[")) {
          Next();
          Next();
          decl.Bounds = new List<int[]>();
          while (!Eof && !Accept("]")) {
            var low = new List<Tok>();
            while (!Eof && !At("..") && !At(",") && !At("]")) low.Add(Next());
            var high = new List<Tok>();
            if (Accept("..")) {
              while (!Eof && !At(",") && !At("]")) high.Add(Next());
            }
            decl.Bounds.Add(new[] { ParseBound(low), ParseBound(high) });
            Accept(",");
          }
          Accept("of");
          ParseTypeSpec(decl);
          return;
        }
        decl.TypeName = ReadTypeName(new[] { ";", ":=", "{" }, stopAtLineStartIdent: false);
      }

      static int ParseBound(List<Tok> tokens) {
        string text = string.Concat(tokens.Select(t => t.Text));
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : int.MinValue;
      }

      string ReadTypeName(string[] stops, bool stopAtLineStartIdent) {
        var parts = new List<Tok>();
        int depth = 0;
        while (!Eof) {
          Tok tok = Peek();
          if (depth == 0 && stops.Any(s => tok.Is(s))) {
            break;
          }
          if (stopAtLineStartIdent && parts.Count > 0 && depth == 0 && tok.Kind != TokKind.Punct && parts[parts.Count - 1].Kind != TokKind.Punct) {
            break;
          }
          if (tok.Is("[")) depth++;
          if (tok.Is("]")) depth--;
          parts.Add(Next());
        }
        return NormalizeTypeName(string.Concat(parts.Select(t => t.Kind == TokKind.Quoted ? t.Text : t.Text)));
      }

      void ParseInit(SourceDecl decl) {
        if (Accept("(")) {
          while (!Eof && !Accept(")")) {
            Tok nameTok = Next();
            if (!Accept(":=")) {
              continue;
            }
            SourceDecl child = decl.GetOrAddChild(NameText(nameTok));
            if (At("(")) {
              ParseInit(child);
            } else if (At("[")) {
              SkipBalanced();
            } else {
              child.StartValue = ReadValue(new[] { ",", ")" });
            }
            Accept(",");
          }
          return;
        }
        if (At("[")) {
          SkipBalanced();
          return;
        }
        decl.StartValue = ReadValue(new[] { ";" });
      }

      void ParseValueAssignment(SourceModel model) {
        var path = new StringBuilder();
        while (!Eof && !At(":=") && !At(";")) {
          Tok tok = Next();
          path.Append(tok.Kind == TokKind.Quoted ? tok.Text : tok.Text);
        }
        if (Accept(":=")) {
          string value = ReadValue(new[] { ";" });
          if (path.Length > 0) {
            model.Values[path.ToString()] = value;
          }
        }
        Accept(";");
      }

      string ReadValue(string[] stops) {
        var parts = new List<string>();
        int depth = 0;
        while (!Eof) {
          Tok tok = Peek();
          if (depth == 0 && stops.Any(s => tok.Is(s))) {
            break;
          }
          if (tok.Is("(") || tok.Is("[")) depth++;
          if (tok.Is(")") || tok.Is("]")) depth--;
          parts.Add(Next().ToString());
        }
        return NormalizeValue(string.Concat(parts));
      }

      void SkipBalanced() {
        int depth = 0;
        while (!Eof) {
          Tok tok = Next();
          if (tok.Is("(") || tok.Is("[")) depth++;
          if (tok.Is(")") || tok.Is("]")) {
            depth--;
            if (depth <= 0) {
              return;
            }
          }
        }
      }

      void SkipTo(string text) {
        int depth = 0;
        while (!Eof) {
          Tok tok = Peek();
          if (depth == 0 && tok.Is(text)) {
            Next();
            return;
          }
          if (depth == 0 && (tok.Is("END_VAR") || tok.Is("END_STRUCT"))) {
            return;
          }
          if (tok.Is("(") || tok.Is("[")) depth++;
          if (tok.Is(")") || tok.Is("]")) depth--;
          Next();
        }
      }

      Dictionary<string, string> ParseAttributes() {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Accept("{");
        while (!Eof && !Accept("}")) {
          Tok name = Next();
          if (Accept(":=")) {
            Tok value = Next();
            if (value != null) {
              attributes[name.Text] = value.Kind == TokKind.Quoted ? value.Text : value.ToString();
            }
          }
          Accept(";");
        }
        return attributes;
      }

      static string NameText(Tok tok) {
        return tok == null ? null : tok.Text;
      }
    }

    /// <summary>Entfernt Namensraum-Präfix ("_.") und Anführungszeichen aus einem Typnamen.</summary>
    static internal string NormalizeTypeName(string typeName) {
      if (string.IsNullOrEmpty(typeName)) {
        return typeName;
      }
      string result = typeName.Trim();
      if (result.StartsWith("_.")) {
        result = result.Substring(2);
      }
      return result.Replace("\"", "");
    }

    /// <summary>Entfernt Namensraum-Präfixe ("_.") aus einem Wert.</summary>
    static internal string NormalizeValue(string value) {
      if (value == null) {
        return null;
      }
      string result = value.Trim();
      if (result.StartsWith("_.")) {
        result = result.Substring(2);
      }
      return result.Replace("\"", "");
    }

    #endregion

    #region Aufrufe

    /// <summary>
    /// Sucht Bausteinaufrufe ("FC"(...), "Instanz"(...), #Multiinstanz(...)) in einem Token-Bereich.
    /// </summary>
    static internal List<SourceCall> ParseCalls(List<Tok> tokens, int start, int end) {
      var calls = new List<SourceCall>();
      for (int i = start; i < end; i++) {
        Tok tok = tokens[i];
        if (tok.Kind != TokKind.Quoted && tok.Kind != TokKind.Local) {
          continue;
        }
        if (i > start && tokens[i - 1].Is(".")) {
          continue;
        }
        var callee = new SourceOperand { Global = tok.Kind == TokKind.Quoted, Local = tok.Kind == TokKind.Local };
        var segment = new SourceSegment { Name = tok.Text };
        callee.Segments.Add(segment);
        int j = i + 1;
        if (tok.Kind == TokKind.Local && j < end && tokens[j].Is("[")) {
          int close = FindClose(tokens, j, end);
          if (close < 0) {
            continue;
          }
          segment.Indices.AddRange(SplitTopLevel(tokens, j + 1, close).Select(TokensText));
          j = close + 1;
        }
        if (j >= end || !tokens[j].Is("(")) {
          continue;
        }
        int closeParen = FindClose(tokens, j, end);
        if (closeParen < 0) {
          continue;
        }
        var call = new SourceCall { Callee = callee };
        foreach (List<Tok> arg in SplitTopLevel(tokens, j + 1, closeParen)) {
          if (arg.Count >= 2 && (arg[1].Is(":=") || arg[1].Is("=>")) && (arg[0].Kind == TokKind.Ident || arg[0].Kind == TokKind.Quoted)) {
            List<Tok> value = arg.Skip(2).SkipWhile(t => t.Is(":=") || t.Is("=>")).ToList();
            call.Args.Add(new KeyValuePair<string, SourceOperand>(arg[0].Text, ParseOperand(value)));
          } else if (arg.Count > 0) {
            call.Args.Add(new KeyValuePair<string, SourceOperand>("", ParseOperand(arg)));
          }
        }
        calls.Add(call);
      }
      return calls;
    }

    /// <summary>
    /// Sucht Anweisungen der Form Operand := Operand; (z. B. #a.stConfig := #b.stConfig;).
    /// </summary>
    static internal List<KeyValuePair<SourceOperand, SourceOperand>> ParseAssignments(List<Tok> tokens, int start, int end) {
      var assignments = new List<KeyValuePair<SourceOperand, SourceOperand>>();
      for (int i = start; i < end; i++) {
        Tok tok = tokens[i];
        if ((tok.Kind != TokKind.Quoted && tok.Kind != TokKind.Local) || (i > start && (tokens[i - 1].Is(".") || tokens[i - 1].Is("[")))) {
          continue;
        }
        int j = OperandEnd(tokens, i, end);
        if (j >= end || !tokens[j].Is(":=")) {
          continue;
        }
        int semicolon = j + 1;
        while (semicolon < end && !tokens[semicolon].Is(";")) semicolon++;
        SourceOperand target = ParseOperand(tokens.GetRange(i, j - i));
        SourceOperand source = ParseOperand(tokens.GetRange(j + 1, semicolon - j - 1));
        if (target != null && source != null) {
          assignments.Add(new KeyValuePair<SourceOperand, SourceOperand>(target, source));
          i = semicolon;
        }
      }
      return assignments;
    }

    static int OperandEnd(List<Tok> tokens, int i, int end) {
      int j = i + 1;
      while (j < end) {
        if (tokens[j].Is(".") && j + 1 < end && (tokens[j + 1].Kind == TokKind.Ident || tokens[j + 1].Kind == TokKind.Quoted)) {
          j += 2;
        } else if (tokens[j].Is("[")) {
          int close = FindClose(tokens, j, end);
          if (close < 0) {
            return end;
          }
          j = close + 1;
        } else {
          break;
        }
      }
      return j;
    }

    static int FindClose(List<Tok> tokens, int open, int end) {
      int depth = 0;
      for (int k = open; k < end; k++) {
        if (tokens[k].Is("(") || tokens[k].Is("[")) depth++;
        if (tokens[k].Is(")") || tokens[k].Is("]")) {
          depth--;
          if (depth == 0) {
            return k;
          }
        }
      }
      return -1;
    }

    static List<List<Tok>> SplitTopLevel(List<Tok> tokens, int start, int end) {
      var parts = new List<List<Tok>>();
      var current = new List<Tok>();
      int depth = 0;
      for (int k = start; k < end; k++) {
        Tok tok = tokens[k];
        if (tok.Is("(") || tok.Is("[")) depth++;
        if (tok.Is(")") || tok.Is("]")) depth--;
        if (depth == 0 && tok.Is(",")) {
          parts.Add(current);
          current = new List<Tok>();
          continue;
        }
        current.Add(tok);
      }
      if (current.Count > 0 || parts.Count > 0) {
        parts.Add(current);
      }
      return parts.Where(p => p.Count > 0).ToList();
    }

    static string TokensText(List<Tok> tokens) {
      return string.Concat(tokens.Select(t => t.ToString()));
    }

    /// <summary>
    /// Liest einen einfachen Operanden ("DB".a.b[1], #a.b[#i]); null bei Ausdrücken, Konstanten oder leeren Parametern.
    /// </summary>
    static internal SourceOperand ParseOperand(List<Tok> tokens) {
      if (tokens == null || tokens.Count == 0) {
        return null;
      }
      Tok first = tokens[0];
      if (first.Kind != TokKind.Quoted && first.Kind != TokKind.Local) {
        return null;
      }
      var op = new SourceOperand { Global = first.Kind == TokKind.Quoted, Local = first.Kind == TokKind.Local };
      var segment = new SourceSegment { Name = first.Text };
      op.Segments.Add(segment);
      int i = 1;
      while (i < tokens.Count) {
        Tok tok = tokens[i];
        if (tok.Is(".")) {
          if (i + 1 >= tokens.Count || (tokens[i + 1].Kind != TokKind.Ident && tokens[i + 1].Kind != TokKind.Quoted)) {
            return null;
          }
          segment = new SourceSegment { Name = tokens[i + 1].Text };
          op.Segments.Add(segment);
          i += 2;
        } else if (tok.Is("[")) {
          int close = FindClose(tokens, i, tokens.Count);
          if (close < 0) {
            return null;
          }
          segment.Indices.AddRange(SplitTopLevel(tokens, i + 1, close).Select(TokensText));
          i = close + 1;
        } else {
          return null;
        }
      }
      return op;
    }

    #endregion

    #region SimaticML

    /// <summary>
    /// Liest einen SimaticML-Export (Baustein oder Datentyp).
    /// </summary>
    static public SourceModel ParseXml(string xml) {
      var model = new SourceModel();
      XDocument doc = XDocument.Parse(xml);
      XElement item = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("SW."));
      if (item == null) {
        return model;
      }
      string itemName = item.Name.LocalName;
      if (itemName.EndsWith(".FB")) model.Kind = "FUNCTION_BLOCK";
      else if (itemName.EndsWith(".FC")) model.Kind = "FUNCTION";
      else if (itemName.EndsWith(".OB")) model.Kind = "ORGANIZATION_BLOCK";
      else if (itemName.Contains("DB")) model.Kind = "DATA_BLOCK";
      else model.Kind = "TYPE";

      XElement attributeList = Child(item, "AttributeList");
      model.Name = (string)Child(attributeList, "Name");
      string instanceOf = (string)Child(attributeList, "InstanceOfName");
      if (!string.IsNullOrEmpty(instanceOf)) {
        model.InstanceOf = instanceOf;
      }

      XElement sections = attributeList?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Sections");
      if (sections != null) {
        foreach (XElement section in sections.Elements().Where(e => e.Name.LocalName == "Section")) {
          string sectionName = (string)section.Attribute("Name");
          foreach (XElement member in section.Elements().Where(e => e.Name.LocalName == "Member")) {
            SourceDecl decl = ConvertMember(member);
            decl.Section = sectionName;
            model.Decls.Add(decl);
          }
        }
        if (model.Kind == "TYPE" && model.Decls.Count == 1 && model.Decls[0].Section == "None" && model.Decls[0].TypeName == "Struct" && model.Decls[0].Name == null) {
          model.Decls = model.Decls[0].Children;
        }
      }

      foreach (XElement source in item.Descendants().Where(e => e.Name.LocalName == "NetworkSource")) {
        foreach (XElement content in source.Elements()) {
          if (content.Name.LocalName == "StructuredText") {
            string text = RenderStructuredText(content);
            List<Tok> tokens = Tokenize(text);
            model.Calls.AddRange(ParseCalls(tokens, 0, tokens.Count));
            model.Assignments.AddRange(ParseAssignments(tokens, 0, tokens.Count));
          } else if (content.Name.LocalName == "FlgNet") {
            model.Calls.AddRange(ParseFlgNet(content));
          }
        }
      }
      return model;
    }

    static XElement Child(XElement parent, string localName) {
      return parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
    }

    static SourceDecl ConvertMember(XElement member) {
      var decl = new SourceDecl { Name = (string)member.Attribute("Name") };
      string dataType = (string)member.Attribute("Datatype");
      if (dataType != null) {
        ParseXmlDataType(decl, dataType);
      }
      XElement comment = Child(member, "Comment");
      if (comment != null) {
        decl.Comments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement text in comment.Elements().Where(e => e.Name.LocalName == "MultiLanguageText")) {
          string lang = (string)text.Attribute("Lang") ?? "";
          decl.Comments[lang] = text.Value;
        }
      }
      XElement startValue = Child(member, "StartValue");
      if (startValue != null) {
        string constantName = (string)startValue.Attribute("ConstantName");
        decl.StartValue = NormalizeValue(!string.IsNullOrEmpty(constantName) ? constantName : startValue.Value);
      }
      foreach (XElement child in member.Elements()) {
        if (child.Name.LocalName == "Member") {
          decl.Children.Add(ConvertMember(child));
        } else if (child.Name.LocalName == "Sections") {
          foreach (XElement section in child.Elements().Where(e => e.Name.LocalName == "Section")) {
            foreach (XElement sub in section.Elements().Where(e => e.Name.LocalName == "Member")) {
              decl.Children.Add(ConvertMember(sub));
            }
          }
        }
      }
      return decl;
    }

    static void ParseXmlDataType(SourceDecl decl, string dataType) {
      string text = dataType.Trim();
      if (text.StartsWith("Array", StringComparison.OrdinalIgnoreCase)) {
        int open = text.IndexOf('[');
        int close = text.IndexOf(']');
        int of = text.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
        if (open > 0 && close > open && of > close) {
          decl.Bounds = new List<int[]>();
          foreach (string dimension in text.Substring(open + 1, close - open - 1).Split(',')) {
            string[] parts = dimension.Split(new[] { ".." }, StringSplitOptions.None);
            decl.Bounds.Add(new[] { ParseInt(parts[0]), parts.Length > 1 ? ParseInt(parts[1]) : int.MinValue });
          }
          text = text.Substring(of + 4);
        }
      }
      decl.TypeName = NormalizeTypeName(text);
    }

    static int ParseInt(string text) {
      return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : int.MinValue;
    }

    /// <summary>
    /// Wandelt SCL-Code aus SimaticML in Text um. Kommentare werden ausgelassen.
    /// </summary>
    static string RenderStructuredText(XElement structuredText) {
      var sb = new StringBuilder();
      foreach (XElement element in structuredText.Elements()) {
        RenderStElement(element, sb);
      }
      return sb.ToString();
    }

    static void RenderStElement(XElement element, StringBuilder sb) {
      switch (element.Name.LocalName) {
        case "Token":
          sb.Append((string)element.Attribute("Text"));
          return;
        case "Blank":
          sb.Append(' ', Math.Max(1, (int?)element.Attribute("Num") ?? 1));
          return;
        case "NewLine":
          sb.Append('\n', Math.Max(1, (int?)element.Attribute("Num") ?? 1));
          return;
        case "LineComment":
        case "Comment":
        case "Text":
          sb.Append(' ');
          return;
        case "Access":
          RenderStAccess(element, sb);
          return;
        case "Parameter": {
          sb.Append(' ').Append((string)element.Attribute("Name")).Append(' ');
          var inner = new StringBuilder();
          foreach (XElement child in element.Elements()) {
            RenderStElement(child, inner);
          }
          string innerText = inner.ToString().Trim();
          if (!innerText.StartsWith(":=") && !innerText.StartsWith("=>")) {
            sb.Append(":= ");
          }
          sb.Append(innerText);
          return;
        }
        default:
          foreach (XElement child in element.Elements()) {
            RenderStElement(child, sb);
          }
          return;
      }
    }

    static void RenderStAccess(XElement access, StringBuilder sb) {
      string scope = (string)access.Attribute("Scope") ?? "";
      switch (scope) {
        case "Call": {
          XElement callInfo = Child(access, "CallInfo");
          if (callInfo == null) {
            return;
          }
          XElement instance = Child(callInfo, "Instance");
          sb.Append(' ');
          if (instance != null) {
            sb.Append(RenderSymbol(instance, (string)instance.Attribute("Scope") == "GlobalVariable", (string)instance.Attribute("Scope") == "LocalVariable"));
          } else {
            sb.Append('"').Append((string)callInfo.Attribute("Name")).Append('"');
          }
          foreach (XElement child in callInfo.Elements().Where(e => e.Name.LocalName != "Instance")) {
            RenderStElement(child, sb);
          }
          return;
        }
        case "GlobalVariable":
          sb.Append(' ').Append(RenderSymbol(Child(access, "Symbol") ?? access, true, false)).Append(' ');
          return;
        case "LocalVariable":
          sb.Append(' ').Append(RenderSymbol(Child(access, "Symbol") ?? access, false, true)).Append(' ');
          return;
        case "LiteralConstant":
        case "TypedConstant": {
          XElement value = access.Descendants().FirstOrDefault(e => e.Name.LocalName == "ConstantValue");
          sb.Append(' ').Append(value?.Value ?? "0").Append(' ');
          return;
        }
        case "GlobalConstant":
        case "LocalConstant": {
          XElement constant = Child(access, "Constant");
          string name = (string)constant?.Attribute("Name") ?? "";
          sb.Append(' ').Append(scope == "LocalConstant" ? "#" + name : name).Append(' ');
          return;
        }
        default:
          foreach (XElement child in access.Elements()) {
            RenderStElement(child, sb);
          }
          return;
      }
    }

    /// <summary>
    /// Formatiert ein Symbol (Folge von Component-Elementen) als Operand-Text, z. B. "DB".a.b[1] oder #a.b[#i].
    /// </summary>
    static string RenderSymbol(XElement symbol, bool global, bool local) {
      var sb = new StringBuilder();
      bool first = true;
      foreach (XElement component in symbol.Elements().Where(e => e.Name.LocalName == "Component")) {
        string name = (string)component.Attribute("Name");
        if (first) {
          if (global) sb.Append('"').Append(name).Append('"');
          else if (local) sb.Append('#').Append(name);
          else sb.Append(name);
          first = false;
        } else {
          sb.Append('.').Append(name);
        }
        List<string> indices = component.Elements().Where(e => e.Name.LocalName == "Access").Select(RenderIndex).ToList();
        if (indices.Count > 0) {
          sb.Append('[').Append(string.Join(",", indices)).Append(']');
        }
      }
      return sb.ToString();
    }

    static string RenderIndex(XElement access) {
      var sb = new StringBuilder();
      RenderStAccess(access, sb);
      return sb.ToString().Replace(" ", "");
    }

    /// <summary>
    /// Liest die Bausteinaufrufe eines KOP/FUP-Netzwerks (FlgNet) über die Verdrahtung der Aufrufparameter.
    /// </summary>
    static List<SourceCall> ParseFlgNet(XElement flgNet) {
      var calls = new List<SourceCall>();
      XElement parts = Child(flgNet, "Parts");
      XElement wires = Child(flgNet, "Wires");
      if (parts == null) {
        return calls;
      }

      var operands = new Dictionary<string, SourceOperand>();
      foreach (XElement access in parts.Elements().Where(e => e.Name.LocalName == "Access")) {
        string uid = (string)access.Attribute("UId");
        string scope = (string)access.Attribute("Scope");
        if (uid == null || (scope != "GlobalVariable" && scope != "LocalVariable")) {
          continue;
        }
        string text = RenderSymbol(Child(access, "Symbol") ?? access, scope == "GlobalVariable", scope == "LocalVariable");
        List<Tok> tokens = Tokenize(text);
        SourceOperand op = ParseOperand(tokens);
        if (op != null) {
          operands[uid] = op;
        }
      }

      var pins = new Dictionary<string, List<KeyValuePair<string, string>>>();
      if (wires != null) {
        foreach (XElement wire in wires.Elements().Where(e => e.Name.LocalName == "Wire")) {
          List<string> idents = wire.Elements().Where(e => e.Name.LocalName == "IdentCon").Select(e => (string)e.Attribute("UId")).ToList();
          if (idents.Count == 0) {
            continue;
          }
          foreach (XElement nameCon in wire.Elements().Where(e => e.Name.LocalName == "NameCon")) {
            string uid = (string)nameCon.Attribute("UId");
            string name = (string)nameCon.Attribute("Name");
            if (uid == null || name == null) {
              continue;
            }
            if (!pins.TryGetValue(uid, out var list)) {
              list = new List<KeyValuePair<string, string>>();
              pins[uid] = list;
            }
            list.Add(new KeyValuePair<string, string>(name, idents[0]));
          }
        }
      }

      foreach (XElement call in parts.Elements().Where(e => e.Name.LocalName == "Call")) {
        XElement callInfo = Child(call, "CallInfo");
        if (callInfo == null) {
          continue;
        }
        string typeName = (string)callInfo.Attribute("Name");
        XElement instance = Child(callInfo, "Instance");
        SourceOperand callee;
        if (instance != null) {
          string scope = (string)instance.Attribute("Scope");
          callee = ParseOperand(Tokenize(RenderSymbol(instance, scope == "GlobalVariable", scope == "LocalVariable")));
        } else {
          callee = SourceOperand.ForBlock(typeName);
        }
        if (callee == null) {
          continue;
        }
        var sourceCall = new SourceCall { Callee = callee, CalleeTypeName = typeName };
        string callUid = (string)call.Attribute("UId");
        if (callUid != null && pins.TryGetValue(callUid, out var connections)) {
          foreach (var connection in connections) {
            operands.TryGetValue(connection.Value, out SourceOperand op);
            sourceCall.Args.Add(new KeyValuePair<string, SourceOperand>(connection.Key, op));
          }
        }
        calls.Add(sourceCall);
      }
      return calls;
    }

    #endregion
  }
}
