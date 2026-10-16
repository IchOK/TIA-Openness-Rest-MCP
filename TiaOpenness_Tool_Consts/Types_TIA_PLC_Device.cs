namespace Tophinke.TiaOpenness.Tool.Types.PLC.Device {
  /// <summary>
  /// Ein Gerät mit PLC-Software (CPU).
  /// </summary>
  public class DeviceInfo {
    public string DeviceName { get; set; }       // z. B. "PLC_1" (Station)
    public string DeviceItemName { get; set; }   // z. B. "CPU 1516-3 PN/DP"
    public string PlcName { get; set; }          // Name der PlcSoftware
    public string TypeIdentifier { get; set; }   // Bestellnummer/Version des Geräts bzw. der CPU
    public int BlockCount { get; set; }
    public int UdtCount { get; set; }
    public int TagTableCount { get; set; }
  }
}
