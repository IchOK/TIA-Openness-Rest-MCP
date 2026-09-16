using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.IO;
using Tophinke.TiaOpenness.Tool.Types.Block;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Gemeinsame Hilfsmethoden für rekursive Suche und Export von PLC-Bausteinen.
  /// </summary>
  static internal class cTiaBlockHelpers {
    static public PlcBlock FindBlock(PlcBlockGroup group, string blockName) {
      foreach (var block in group.Blocks) {
        if (block.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase)) {
          return block;
        }
      }
      foreach (var userGroup in group.Groups) {
        var foundBlock = FindBlock(userGroup, blockName);
        if (foundBlock != null) {
          return foundBlock;
        }
      }
      return null;
    }

    static public PlcType FindType(PlcTypeGroup group, string typeName) {
      foreach (var type in group.Types) {
        if (type.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)) {
          return type;
        }
      }
      foreach (var userGroup in group.Groups) {
        var foundType = FindType(userGroup, typeName);
        if (foundType != null) {
          return foundType;
        }
      }
      return null;
    }

    static public PlcTagTable FindTagTable(PlcTagTableGroup group, string tagTableName) {
      foreach (var table in group.TagTables) {
        if (table.Name.Equals(tagTableName, StringComparison.OrdinalIgnoreCase)) {
          return table;
        }
      }
      foreach (var userGroup in group.Groups) {
        var found = FindTagTable(userGroup, tagTableName);
        if (found != null) {
          return found;
        }
      }
      return null;
    }

    static public PlcTag FindTag(PlcTagTableGroup group, string tagName) {
      foreach (var table in group.TagTables) {
        foreach (PlcTag tag in table.Tags) {
          if (tag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase)) {
            return tag;
          }
        }
      }
      foreach (var userGroup in group.Groups) {
        var found = FindTag(userGroup, tagName);
        if (found != null) {
          return found;
        }
      }
      return null;
    }

    static public PlcSystemConstant FindSystemConstant(PlcTagTableGroup group, string constantName) {
      foreach (var table in group.TagTables) {
        foreach (PlcSystemConstant constant in table.SystemConstants) {
          if (constant.Name.Equals(constantName, StringComparison.OrdinalIgnoreCase)) {
            return constant;
          }
        }
      }
      foreach (var userGroup in group.Groups) {
        var found = FindSystemConstant(userGroup, constantName);
        if (found != null) {
          return found;
        }
      }
      return null;
    }

    /// <summary>
    /// Exportiert einen Baustein als SimaticData-Dokument und liest den Inhalt.
    /// </summary>
    /// <returns>Fehlermeldung oder null bei Erfolg</returns>
    static public string ExportBlockAsDocuments(PlcBlock block, string blockName, out string content) {
      content = null;
      string tempFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "export");
      FileInfo tempFileInfo = new FileInfo(Path.Combine(tempFilePath, $"{blockName}.s7dcl"));
      try {
        if (!Directory.Exists(tempFilePath)) {
          Directory.CreateDirectory(tempFilePath);
        }
        if (tempFileInfo.Exists) {
          File.Delete(tempFileInfo.FullName);
        }
        DocumentExportResult exportResult = block.ExportAsDocuments(tempFileInfo.Directory, blockName);
        if (exportResult == null || exportResult.State != DocumentResultState.Success) {
          return $"Error exporting block {blockName}.";
        }
        content = File.ReadAllText(tempFileInfo.FullName);
        return null;
      } catch (Exception ex) {
        return $"Error exporting block {blockName}: {ex.Message}";
      }
    }
  }
}
