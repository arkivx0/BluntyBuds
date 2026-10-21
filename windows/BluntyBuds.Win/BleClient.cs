using BluntyBuds.Proto;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BluntyBuds.Win;

/// <summary>WinRT GATT client. Talks only to the earbuds; no network.</summary>
public sealed class BleClient : IDisposable
{
    public event Action<string>? Status;
    public event Action<Event>? EventReceived;
    public event Action? Disconnected;
    public Protocol? Protocol { get; private set; }
    bool _demo;
    /// <summary>Screenshot/demo mode: pretend to be connected over protocol A; writes are dropped.</summary>
    public void EnterDemo() { Close(); _demo = true; Protocol = BluntyBuds.Proto.Protocol.A; }

    BluetoothLEDevice? _dev;
    GattCharacteristic? _write, _notify;
    GattSession? _session;
    readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<bool> ConnectAsync(ulong address, Protocol? hint)
    {
        Close();
        Status?.Invoke($"Connecting to {address:X12}…");
        try
        {
            _dev = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (_dev is null) { Status?.Invoke($"{address:X12}: no Bluetooth LE side reachable. Take the earbuds out of the case, close the phone app, then Scan and retry."); return false; }
            return await SetupAsync(hint);
        }
        catch (Exception ex) { Fail(ex); return false; }
    }

    public async Task<bool> ConnectByIdAsync(string deviceId, Protocol? hint)
    {
        Close();
        Status?.Invoke("Connecting to paired device…");
        try
        {
            _dev = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (_dev is null) { Status?.Invoke("Could not open paired device."); return false; }
            return await SetupAsync(hint);
        }
        catch (Exception ex) { Fail(ex); return false; }
    }

    void Fail(Exception ex)
    {
        var hr = ex.HResult;
        var why = hr == unchecked((int)0x80004004) ? "the earbuds aborted the Bluetooth LE connection (E_ABORT). They are probably in the case, asleep, or held by another phone/app."
                : hr == unchecked((int)0x80070490) ? "device not found." : ex.Message;
        Status?.Invoke($"Connect failed: {why} (0x{hr:X8})");
        try { Close(); } catch { }
    }

    async Task<bool> SetupAsync(Protocol? hint)
    {
        var dev = _dev!;
        dev.ConnectionStatusChanged += (s, _) => { if (s.ConnectionStatus == BluetoothConnectionStatus.Disconnected) Disconnected?.Invoke(); };
        try { _session = await GattSession.FromDeviceIdAsync(dev.BluetoothDeviceId); if (_session != null) _session.MaintainConnection = true; }
        catch (Exception ex) { Program.Log(ex, "GattSession"); Status?.Invoke($"Note: couldn't keep the link open (0x{ex.HResult:X8}); continuing."); }
        var svcs = await dev.GetGattServicesAsync(BluetoothCacheMode.Uncached);
        if (svcs.Status != GattCommunicationStatus.Success) svcs = await dev.GetGattServicesAsync(BluetoothCacheMode.Cached);
        if (svcs.Status != GattCommunicationStatus.Success) { Status?.Invoke($"Service discovery failed: {StatusText(svcs.Status, svcs.ProtocolError)}. Try pairing the earbuds' LE side or toggling Bluetooth."); return false; }

        GattCharacteristic? tw = null, tn = null, jw = null, jn = null;
        var failed = new List<string>();
        foreach (var s in svcs.Services)
        {
            GattCharacteristicsResult cr;
            try { cr = await s.GetCharacteristicsAsync(BluetoothCacheMode.Uncached); }
            catch (Exception ex) { failed.Add($"{s.Uuid.ToString()[..8]}: 0x{ex.HResult:X8}"); continue; }
            if (cr.Status != GattCommunicationStatus.Success) { failed.Add($"{s.Uuid.ToString()[..8]}: {StatusText(cr.Status, cr.ProtocolError)}"); continue; }
            foreach (var c in cr.Characteristics)
            {
                var u = c.Uuid.ToString().ToLowerInvariant();
                if (s.Uuid == ProtoJ.Service && c.Uuid == ProtoJ.Write) jw = c;
                if (s.Uuid == ProtoJ.Service && c.Uuid == ProtoJ.Notify) jn = c;
                if (u.StartsWith(ProtoA.WriteUuidPrefix) || (tw is null && u.Substring(4, 4) == "b611")) tw = c;
                if (u.StartsWith(ProtoA.NotifyUuidPrefix) || (tn is null && u.Substring(4, 4) == "b612")) tn = c;
            }
        }
        bool haveA = tw != null && tn != null, haveJ = jw != null && jn != null;
        Protocol? pick = haveA && haveJ ? (hint ?? BluntyBuds.Proto.Protocol.A) : haveA ? BluntyBuds.Proto.Protocol.A : haveJ ? BluntyBuds.Proto.Protocol.J : null;
        if (pick is null) { Status?.Invoke("No supported control service (protocol A 4157b6xx or protocol J faa0) found on this device." + (failed.Count > 0 ? $" Unreadable services: {string.Join(", ", failed)}" : "")); return false; }
        Protocol = pick;
        (_write, _notify) = pick == BluntyBuds.Proto.Protocol.A ? (tw, tn) : (jw, jn);

        var cccd = _notify!.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
            ? GattClientCharacteristicConfigurationDescriptorValue.Notify : GattClientCharacteristicConfigurationDescriptorValue.Indicate;
        _notify.ValueChanged += OnValue;
        var wr = await _notify.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(cccd);
        if (wr.Status != GattCommunicationStatus.Success) { Status?.Invoke($"Enabling notifications failed: {StatusText(wr.Status, wr.ProtocolError)}"); Close(); return false; }
        Status?.Invoke($"Connected · protocol {pick}");
        return true;
    }

    void OnValue(GattCharacteristic c, GattValueChangedEventArgs a)
    {
      try {
        var r = DataReader.FromBuffer(a.CharacteristicValue);
        var b = new byte[a.CharacteristicValue.Length];
        r.ReadBytes(b);
        IEnumerable<Event> evs = Protocol switch
        {
            BluntyBuds.Proto.Protocol.A => ProtoA.Decode(b) is { } e ? new[] { e } : Array.Empty<Event>(),
            BluntyBuds.Proto.Protocol.J => ProtoJ.Decode(b),
            _ => Array.Empty<Event>()
        };
        foreach (var e in evs) EventReceived?.Invoke(e);
      } catch (Exception ex) { Status?.Invoke("Bad notification: " + ex.Message); }
    }

    public async Task<bool> SendAsync(byte[] frame)
    {
        if (_demo) return true;
        var c = _write; if (c is null) { Status?.Invoke("Not connected."); return false; }
        await _lock.WaitAsync();
        try
        {
          try
          {
            int chunk = Math.Max(20, (_session?.MaxPduSize ?? 23) - 3);
            var opt = c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ? GattWriteOption.WriteWithResponse : GattWriteOption.WriteWithoutResponse;
            for (int i = 0; i < frame.Length; i += chunk)
            {
                var w = new DataWriter();
                w.WriteBytes(frame[i..Math.Min(frame.Length, i + chunk)]);
                var res = await c.WriteValueWithResultAsync(w.DetachBuffer(), opt);
                if (res.Status != GattCommunicationStatus.Success) { Status?.Invoke($"Write failed: {StatusText(res.Status, res.ProtocolError)}"); return false; }
            }
            await Task.Delay(120); // let the reply arrive before the next request
            return true;
          }
          catch (Exception ex) { Status?.Invoke($"Write failed: {ex.Message} (0x{ex.HResult:X8})"); return false; }
        }
        finally { _lock.Release(); }
    }

    /// <summary>GATT status + ATT protocol error as readable text.</summary>
    public static string StatusText(GattCommunicationStatus st, byte? protocolError) => st switch
    {
        GattCommunicationStatus.Unreachable => "unreachable (earbuds out of range, in the case, or asleep)",
        GattCommunicationStatus.AccessDenied => "access denied (try pairing the earbuds again)",
        GattCommunicationStatus.ProtocolError => $"ATT protocol error 0x{protocolError ?? 0:X2} ({GattErrors.Name(protocolError ?? 0)})",
        _ => st.ToString()
    };

    public void Close()
    {
        if (_notify != null) _notify.ValueChanged -= OnValue;
        try { _session?.Dispose(); } catch { } _session = null;
        try { _dev?.Dispose(); } catch { } _dev = null; _write = _notify = null; Protocol = null; _demo = false;
    }
    public void Dispose() => Close();
}
