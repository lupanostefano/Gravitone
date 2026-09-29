using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

internal enum NetworkKind { None, Ethernet, Wifi }

/// <summary>
/// What the menu bar's status icons show: output volume, network and battery. Volume and battery
/// are cheap to poll; the network list is only rebuilt when Windows reports an address change.
/// </summary>
internal sealed class SystemStatus : IDisposable
{
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    IMMDeviceEnumerator? _audio;
    Guid? _wifiInterface;
    int _ticks;

    public SystemStatus()
    {
        try
        {
            _audio = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Accessing the volume");
        }
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        _timer.Tick += (_, _) => Poll();
        RefreshNetwork();
        Poll();
        _timer.Start();
    }

    public event Action? Changed;

    /// <summary>0..1, or null without an output device.</summary>
    public double? Volume { get; private set; }
    public bool Muted { get; private set; }

    public NetworkKind Network { get; private set; }
    /// <summary>Wi-Fi signal 0..100.</summary>
    public int WifiSignal { get; private set; }

    public bool HasBattery { get; private set; }
    /// <summary>0..100, or null when unknown.</summary>
    public int? BatteryPercent { get; private set; }
    public bool Charging { get; private set; }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        if (_audio is not null) Marshal.ReleaseComObject(_audio);
        _audio = null;
        if (_wlan != IntPtr.Zero) WlanCloseHandle(_wlan, IntPtr.Zero);
        _wlan = IntPtr.Zero;
    }

    bool _disposed;
    IntPtr _wlan;

    /// <summary>Stops polling while nobody can see the icons (screen off, session locked).</summary>
    public void SetPaused(bool paused)
    {
        if (_disposed) return;
        if (paused) _timer.Stop();
        else if (!_timer.IsEnabled) _timer.Start();
    }

    void OnNetworkChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        if (_disposed) return;
        RefreshNetwork();
        Changed?.Invoke();
    });

    /// <summary>Reads everything again; with <paramref name="force"/> the network too, and listeners are told even if nothing changed.</summary>
    public void Poll(bool force)
    {
        if (_disposed) return;
        if (force) RefreshNetwork();
        Poll();
        if (force) Changed?.Invoke();
    }

    void Poll()
    {
        var before = (Volume, Muted, WifiSignal, HasBattery, BatteryPercent, Charging);
        ReadVolume();
        ReadBattery();
        // Signal strength moves slowly; every few seconds is plenty.
        if (Network == NetworkKind.Wifi && _ticks++ % 5 == 0) WifiSignal = ReadWifiSignal();
        if (before != (Volume, Muted, WifiSignal, HasBattery, BatteryPercent, Charging)) Changed?.Invoke();
    }

    void ReadVolume()
    {
        IMMDevice? device = null;
        object? endpoint = null;
        try
        {
            // The default device changes (headphones plugged in), so it is looked up every time.
            if (_audio is null || _audio.GetDefaultAudioEndpoint(eRender, eMultimedia, out device) != 0)
            {
                Volume = null;
                return;
            }
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out endpoint) != 0) return;
            var volume = (IAudioEndpointVolume)endpoint;
            if (volume.GetMasterVolumeLevelScalar(out float level) == 0) Volume = level;
            if (volume.GetMute(out bool muted) == 0) Muted = muted;
        }
        catch (Exception)
        {
            Volume = null;
        }
        finally
        {
            if (endpoint is not null) Marshal.ReleaseComObject(endpoint);
            if (device is not null) Marshal.ReleaseComObject(device);
        }
    }

    void ReadBattery()
    {
        if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == BATTERY_FLAG_NO_BATTERY || s.BatteryFlag == BATTERY_FLAG_UNKNOWN)
        {
            HasBattery = false;
            return;
        }
        HasBattery = true;
        BatteryPercent = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : null;
        Charging = s.ACLineStatus == 1;
    }

    /// <summary>The best active connection: a wired one wins over Wi-Fi, as on the taskbar.</summary>
    void RefreshNetwork()
    {
        var kind = NetworkKind.None;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                // No gateway: a virtual switch or a cable to nothing, not a way out.
                if (!nic.GetIPProperties().GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)))
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    if (kind == NetworkKind.None) kind = NetworkKind.Wifi;
                }
                else
                {
                    kind = NetworkKind.Ethernet;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading network connections");
        }
        Network = kind;
        _wifiInterface = null;
        WifiSignal = kind == NetworkKind.Wifi ? ReadWifiSignal() : 0;
    }

    int ReadWifiSignal()
    {
        // One WLAN session for the whole run: opening one every few seconds costs more than the query.
        if (_wlan == IntPtr.Zero && WlanOpenHandle(2, IntPtr.Zero, out _, out _wlan) != 0)
        {
            _wlan = IntPtr.Zero;
            return 100;
        }
        _wifiInterface ??= ConnectedWifiInterface(_wlan);
        if (_wifiInterface is not Guid guid) return 100;
        if (WlanQueryInterface(_wlan, ref guid, WLAN_INTF_OPCODE_CURRENT_CONNECTION, IntPtr.Zero,
                out uint size, out var data, IntPtr.Zero) != 0)
        {
            // The adapter went away (or the service restarted): start over next time.
            _wifiInterface = null;
            WlanCloseHandle(_wlan, IntPtr.Zero);
            _wlan = IntPtr.Zero;
            return 100;
        }
        try
        {
            return size > WLAN_SIGNAL_QUALITY_OFFSET ? Math.Clamp(Marshal.ReadInt32(data, WLAN_SIGNAL_QUALITY_OFFSET), 0, 100) : 100;
        }
        finally
        {
            WlanFreeMemory(data);
        }
    }

    static Guid? ConnectedWifiInterface(IntPtr handle)
    {
        if (WlanEnumInterfaces(handle, IntPtr.Zero, out var list) != 0) return null;
        try
        {
            // WLAN_INTERFACE_INFO_LIST: count, index, then items of GUID + WCHAR[256] + state.
            const int itemSize = 16 + 512 + 4;
            int count = Marshal.ReadInt32(list);
            for (int i = 0; i < count; i++)
            {
                var item = list + 8 + i * itemSize;
                int state = Marshal.ReadInt32(item, 16 + 512);
                if (state == 1) return Marshal.PtrToStructure<Guid>(item); // wlan_interface_state_connected
            }
            return null;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }
}
