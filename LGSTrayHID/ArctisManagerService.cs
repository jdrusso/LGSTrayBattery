using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using LGSTrayHID.HidApi;
using static LGSTrayHID.HidApi.HidApi;

namespace LGSTrayHID;

public sealed class ArctisManagerService : IHostedService, IDisposable
{
    private readonly List<ArctisHidDevice> _devices = [];
    private readonly CancellationTokenSource _cts = new();

    private static readonly ushort VendorId = 0x1038;
    private static readonly ushort[] ProductIds =
    [
        0x220e, // Arctis 7 Plus
        0x2206, // Arctis Nova 7X
        0x2258, // Arctis Nova 7X v2
        0x220a, // Arctis Nova 7P
        0x223a, // Arctis Nova 7 Diablo IV
        0x2232, // Arctis Nova 5
        0x2253, // Arctis Nova 5X
    ];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        EnumerateDevices();
        return Task.CompletedTask;
    }

    private unsafe void EnumerateDevices()
    {
        HidDeviceInfo* devs = HidEnumerate(VendorId, 0);
        for (var cur = devs; cur != null; cur = cur->Next)
        {
            if (!ProductIds.Contains(cur->ProductId)) continue;
            if (cur->InterfaceNumber != 3) continue;
            var info = *cur;
            _devices.Add(new ArctisHidDevice(info));
        }
        HidFreeEnumeration(devs);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts.Cancel();
        foreach (var d in _devices)
        {
            d.Dispose();
        }
        _devices.Clear();
        _cts.Dispose();
    }
}
