using System;
using System.Collections.Generic;
using System.Text;

namespace Tophinke.TopCtrl.Tool.Types.UDT {
  public class Info {
    // Eindeutige Hardware-Kennungen
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }

    // Software- & Bausteininformationen
    public string PlcName { get; set; }
    public string UdtName { get; set; }
  }

  public class  Data {
    // Eindeutige Hardware-Kennungen
    public string DeviceName { get; set; }
    public string DeviceItemName { get; set; }

    // Software- & Bausteininformationen
    public string PlcName { get; set; }
    public string UdtName { get; set; }
    public string Format { get; set; }
    public string Content { get; set; }
  }
}

