// READ-ONLY probe: listens to adverts, enumerates GATT + RFCOMM services. Never writes.
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

static string Hex(Windows.Storage.Streams.IBuffer b){var a=new byte[b.Length];DataReader.FromBuffer(b).ReadBytes(a);return Convert.ToHexString(a);}
static async Task<T?> Try<T>(string what, Func<Task<T>> f){try{return await f();}catch(Exception e){Console.WriteLine($"  !! {what}: {e.GetType().Name} 0x{e.HResult:X8} {e.Message.Trim()}");return default;}}

var addrs = args.Length>0 ? args.Select(a=>Convert.ToUInt64(a.Replace(":",""),16)).ToArray() : throw new ArgumentException("usage: probe <bt-address> [<bt-address> ...]  e.g. 00:11:22:33:44:55");
int secs = 20;
Console.WriteLine($"== Advertisements ({secs}s) ==");
var seen = new Dictionary<ulong,string>();
var w = new BluetoothLEAdvertisementWatcher{ScanningMode=BluetoothLEScanningMode.Active};
w.Received += (s,a)=>{
  var secsx = string.Join(" ", a.Advertisement.DataSections.Select(d=>$"[{d.DataType:X2}:{Hex(d.Data)}]"));
  var svc = string.Join(",", a.Advertisement.ServiceUuids);
  var line = $"{a.BluetoothAddress:X12} type={a.BluetoothAddressType} name='{a.Advertisement.LocalName}' rssi={a.RawSignalStrengthInDBm} svc={svc} {secsx}";
  lock(seen){ if(!seen.ContainsKey(a.BluetoothAddress) || seen[a.BluetoothAddress].Length < line.Length) seen[a.BluetoothAddress]=line; }
};
w.Start(); await Task.Delay(secs*1000); w.Stop();
foreach(var l in seen.Values.OrderBy(x=>x)) Console.WriteLine(l);
Console.WriteLine($"({seen.Count} advertisers)");

Console.WriteLine("\n== Paired devices (LE + classic) ==");
foreach(var sel in new[]{BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), BluetoothDevice.GetDeviceSelectorFromPairingState(true)})
{ var list = await DeviceInformation.FindAllAsync(sel, new[]{"System.Devices.Aep.DeviceAddress","System.Devices.Aep.IsConnected"});
  foreach(var d in list) Console.WriteLine($"  {d.Name} | addr={d.Properties.GetValueOrDefault("System.Devices.Aep.DeviceAddress")} | connected={d.Properties.GetValueOrDefault("System.Devices.Aep.IsConnected")} | {d.Id}"); }

foreach(var addr in addrs){
  Console.WriteLine($"\n== {addr:X12} : BLE GATT ==");
  var le = await Try("FromBluetoothAddressAsync(LE)", async()=>await BluetoothLEDevice.FromBluetoothAddressAsync(addr));
  if(le==null) Console.WriteLine("  no LE device object");
  else {
    Console.WriteLine($"  name='{le.Name}' status={le.ConnectionStatus} addrType={le.BluetoothAddressType} appearance={le.Appearance?.RawValue}");
    var r = await Try("GetGattServicesAsync", async()=>await le.GetGattServicesAsync(BluetoothCacheMode.Uncached));
    if(r!=null){ Console.WriteLine($"  services status={r.Status} proto={r.ProtocolError}");
      foreach(var s in r.Services){ Console.WriteLine($"  S {s.Uuid}");
        var c = await Try("GetCharacteristics", async()=>await s.GetCharacteristicsAsync(BluetoothCacheMode.Uncached));
        if(c!=null) foreach(var ch in c.Characteristics) Console.WriteLine($"     C {ch.Uuid} props={ch.CharacteristicProperties}"); } }
    le.Dispose();
  }
  Console.WriteLine($"== {addr:X12} : classic RFCOMM/SDP ==");
  var bt = await Try("FromBluetoothAddressAsync(classic)", async()=>await BluetoothDevice.FromBluetoothAddressAsync(addr));
  if(bt==null){Console.WriteLine("  no classic device object"); continue;}
  Console.WriteLine($"  name='{bt.Name}' status={bt.ConnectionStatus} class=0x{bt.ClassOfDevice.RawValue:X6}");
  var rf = await Try("GetRfcommServicesAsync", async()=>await bt.GetRfcommServicesAsync(BluetoothCacheMode.Uncached));
  if(rf!=null){ Console.WriteLine($"  rfcomm status={rf.Error}"); foreach(var s in rf.Services) Console.WriteLine($"  R {s.ServiceId.Uuid}  host={s.ConnectionHostName} svc={s.ConnectionServiceName}"); }
  var rfc = await Try("GetRfcommServicesAsync(cached)", async()=>await bt.GetRfcommServicesAsync(BluetoothCacheMode.Cached));
  if(rfc!=null){ Console.WriteLine($"  rfcomm(cached) status={rfc.Error}"); foreach(var s in rfc.Services) Console.WriteLine($"  Rc {s.ServiceId.Uuid}"); }
  try{ int n=0; foreach(var rec in bt.SdpRecords){ var b=new byte[rec.Length]; DataReader.FromBuffer(rec).ReadBytes(b);
      var uu=new List<string>(); for(int i=0;i+17<=b.Length;i++){ if(b[i]==0x1C){ uu.Add(new Guid(new byte[]{b[i+4],b[i+3],b[i+2],b[i+1],b[i+6],b[i+5],b[i+8],b[i+7],b[i+9],b[i+10],b[i+11],b[i+12],b[i+13],b[i+14],b[i+15],b[i+16]}).ToString()); i+=16;} else if(b[i]==0x19 && i+2<b.Length){ uu.Add($"0x{b[i+1]:X2}{b[i+2]:X2}"); i+=2;} }
      Console.WriteLine($"  SDP#{n++} len={b.Length} uuids: {string.Join(" ", uu.Distinct())}"); } }catch(Exception e){Console.WriteLine("  sdp err "+e.Message);}
  bt.Dispose();
}
Console.WriteLine("\nDONE (read-only)");
