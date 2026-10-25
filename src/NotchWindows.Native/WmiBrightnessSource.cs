using System.Management;
using NotchWindows.Providers;

namespace NotchWindows.Native;

/// <summary>Internal panel brightness via WmiMonitorBrightness (root\WMI). Returns null when unsupported (desktop).</summary>
public sealed class WmiBrightnessSource : IBrightnessSource
{
    public int? Read()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    return Convert.ToInt32(obj["CurrentBrightness"]);
                }
            }
            return null;
        }
        catch (ManagementException)
        {
            // Class or namespace missing, or provider not supported: no controllable panel.
            return null;
        }
    }
}
