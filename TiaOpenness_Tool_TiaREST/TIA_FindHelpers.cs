using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;

namespace Tophinke.TiaOpenness.Tool.TiaREST {
  /// <summary>
  /// Gemeinsame rekursive Suche, die von mehreren REST-Handlern genutzt wird.
  /// </summary>
  static internal class cTiaFindHelpers {
    /// <summary>
    /// Sucht rekursiv nach einem PLC-Baustein mit dem angegebenen Namen innerhalb einer PLC-Bausteinsgruppe.
    /// </summary>
    /// <param name="group">aktuelle Bausteinsgruppe</param>
    /// <param name="blockName">Name des zu suchenden PLC-Bausteins</param>
    /// <returns>Gefundener PLC-Baustein oder null</returns>
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

    /// <summary>
    /// Sucht rekursiv nach einem PLC-Datentype mit dem angegebenen Namen innerhalb einer PLC-Datentyp-Gruppe.
    /// </summary>
    /// <param name="group">aktuelle Datentyp-Gruppe</param>
    /// <param name="typeName">Name des zu suchenden PLC-Datentyps</param>
    /// <returns>Gefundener PLC-Datentyp oder null</returns>
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
    
    /// <summary>
    /// Sucht rekursiv nach einem PLC-Tagtabelle mit dem angegebenen Namen innerhalb einer PLC-Tagtabelle-Gruppe.
    /// </summary>
    /// <param name="group">aktuelle Tagtabelle-Gruppe</param>
    /// <param name="tagTableName">Name des zu suchenden PLC-Tagtabelle</param>
    /// <returns>Gefundene PLC-Tagtabelle oder null</returns>
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

    /// <summary>
    /// Sucht rekursiv nach einem PLC-Tag mit dem angegebenen Namen innerhalb einer PLC-Tagtabelle-Gruppe.
    /// </summary>
    /// <param name="group">aktuelle Tagtabelle-Gruppe</param>
    /// <param name="tagName">Name des zu suchenden PLC-Tag</param>
    /// <returns>Gefundener PLC-Tag oder null</returns>
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

    /// <summary>
    /// Sucht rekursiv nach einem PLC-Systemkonstanten mit dem angegebenen Namen innerhalb einer PLC-Tagtabelle-Gruppe.
    /// </summary>
    /// <param name="group">aktuelle Tagtabelle-Gruppe</param>
    /// <param name="constantName">Name des zu suchenden PLC-Systemkonstanten</param>
    /// <returns>Gefundene PLC-Systemkonstante oder null</returns>
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
  }
}
