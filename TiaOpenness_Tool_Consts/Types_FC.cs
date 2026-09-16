namespace Tophinke.TiaOpenness.Tool.Types.FC {
  public class Info {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string BlockName { get; set; }
    public int BlockNumber { get; set; }
    public string BlockType { get; set; }
  }

  public class Data {
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }
    public string PlcName { get; set; }
    public string BlockName { get; set; }
    public int BlockNumber { get; set; }
    public string BlockType { get; set; }
    public string Format { get; set; }
    public string Content { get; set; }
  }
}
