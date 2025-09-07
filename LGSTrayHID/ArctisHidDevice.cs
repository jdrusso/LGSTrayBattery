using System.Runtime.InteropServices;
using static LGSTrayHID.HidApi.HidApi;
using LGSTrayHID.HidApi;
using LGSTrayPrimitives;
using LGSTrayPrimitives.MessageStructs;

namespace LGSTrayHID;

/* SteelSeries Arctis devices respond to an output report [0x00, 0xB0]
   which returns an input report where byte 2 is battery level (0-4) and byte 3 indicates
   charging status (0=disconnected, 1=charging, 3=discharging).
   Reference: https://aarol.dev/posts/arctis-hid */

internal sealed class ArctisHidDevice : IDisposable
{
    private readonly nint _handle;
    private readonly string _identifier;
    private readonly string _name;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public unsafe ArctisHidDevice(HidDeviceInfo info)
    {
        _handle = HidOpenPath(ref info);
        _identifier = info.GetPath();
        _name = Marshal.PtrToStringUni((nint)info.ProductString) ?? "SteelSeries";

        HidppManagerContext.Instance.SignalDeviceEvent(
            IPCMessageType.INIT,
            new InitMessage(_identifier, _name, true, DeviceType.Headset)
        );

        _loop = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var (percent, status) = ReadBattery();
            HidppManagerContext.Instance.SignalDeviceEvent(
                IPCMessageType.UPDATE,
                new UpdateMessage(_identifier, percent, status, 0, DateTimeOffset.Now)
            );

#if DEBUG
            await Task.Delay(1000, _cts.Token);
#else
            await Task.Delay(GlobalSettings.settings.PollPeriod * 1000, _cts.Token);
#endif
        }
    }

    private (double, PowerSupplyStatus) ReadBattery()
    {
        byte[] cmd = [0x00, 0xB0];
        _ = HidWrite(_handle, cmd, (nuint)cmd.Length);

        byte[] buf = new byte[4];
        int ret = HidReadTimeOut(_handle, buf, (nuint)buf.Length, 100);
        if (ret < 4)
        {
            return (-1, PowerSupplyStatus.POWER_SUPPLY_STATUS_UNKNOWN);
        }

        double percent = buf[2] switch
        {
            0 => 0,
            1 => 25,
            2 => 50,
            3 => 75,
            4 => 100,
            _ => -1,
        };

        PowerSupplyStatus status = buf[3] switch
        {
            1 => PowerSupplyStatus.POWER_SUPPLY_STATUS_CHARGING,
            3 => PowerSupplyStatus.POWER_SUPPLY_STATUS_DISCHARGING,
            _ => PowerSupplyStatus.POWER_SUPPLY_STATUS_NOT_CHARGING,
        };

        return (percent, status);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _loop.Wait();
        HidClose(_handle);
        _cts.Dispose();
    }
}
