using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using Tophinke.TiaOpenness.Tool.Consts;

namespace Tophinke.TiaOpenness.Tool.TiaMCP.PLC;

[McpServerToolType]
public class PlcDeviceTools {
  [McpServerTool(Name = "tia_plc_devices_list"), Description(
    "Lists all PLC devices (controllers / CPUs) of a TIA project, including devices in device folders and nested device items (e.g. CPU in a rack). " +
    "Response: McpApiResponse with StatusCode, IsSuccess, optional Error, and Data. " +
    "On success, Data is a JSON array with one object per PLC: DeviceName and DeviceItemName (use for the other tia_plc_* tools), " +
    "PlcName (PLC software name), TypeIdentifier (order number/version), " +
    "BlockCount, UdtCount, TagTableCount.")]
  public static async Task<CallToolResult> ListDevices(
    [Description("The ProcessId of the TIA Portal instance")] int processId,
    [Description("The name of the project")] string projectName) {
    var activity = ToolActivity.Start<PlcDeviceTools>("PLC-Geräte werden abgerufen");
    try {
      string route = $"{PlcRoutes.DevicesList}?processId={processId}&projectName={Uri.EscapeDataString(projectName)}";
      using HttpResponseMessage response = await TiaRestClient.Client.GetAsync(route);
      return activity.Complete(await TiaRestClient.FromHttpResponseAsync(response));
    } catch (Exception ex) {
      return activity.Complete(TiaRestClient.FromException(ex));
    }
  }
}
