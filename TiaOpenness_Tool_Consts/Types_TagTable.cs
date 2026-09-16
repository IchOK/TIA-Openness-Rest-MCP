using System.Collections.Generic;

namespace Tophinke.TiaOpenness.Tool.Types.TagTable {
  public class Info {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string TagTableName { get; set; }
    public bool IsDefault { get; set; }
  }

  public class TagInfo {
    public string Name { get; set; }
    public string DataTypeName { get; set; }
    public string LogicalAddress { get; set; }
  }

  public class ConstantInfo {
    public string Name { get; set; }
    public string DataTypeName { get; set; }
    public string Value { get; set; }
  }

  public class Data {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string TagTableName { get; set; }
    public bool IsDefault { get; set; }
    public List<TagInfo> Tags { get; set; }
    public List<ConstantInfo> UserConstants { get; set; }
    public List<ConstantInfo> SystemConstants { get; set; }
  }
}
