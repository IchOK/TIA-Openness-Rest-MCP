using System.Collections.Generic;

namespace Tophinke.TiaOpenness.Tool.Types.PLC.XRef {
  public class LocationInfo {
    public string Access { get; set; }
    public string ReferenceType { get; set; }
    public string ReferenceLocation { get; set; }
    public string ReferencedAsName { get; set; }
  }

  public class ReferenceInfo {
    public string Name { get; set; }
    public List<LocationInfo> Locations { get; set; }
  }

  public class SourceInfo {
    public string Name { get; set; }
    public string TypeName { get; set; }
    public List<ReferenceInfo> References { get; set; }
    public List<SourceInfo> Children { get; set; }
  }

  public class Data {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string ObjectName { get; set; }
    public string ObjectKind { get; set; }
    public string Filter { get; set; }
    public List<SourceInfo> Sources { get; set; }
  }
}
