using System.Diagnostics;
using System.Management;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Diagnostics;

public sealed record SensorReadings(double? CpuTemp, double? GpuTemp, double? VramGb, double? BusyThread, double? CpuMhz, double? DiskBusy);
public sealed record DiagnosticSample(LiveSample Load, SensorReadings Sensors, FrameStats? Frames, double[] Times, string FrameSource, double CollectionMs);

public interface IDiagnosticSource : IDisposable
{
    DiagnosticSample Read(int frames);
}

// Each new subscriber shares the current source and serialized, off-thread reads.
public sealed class DiagnosticFeed(Func<IDiagnosticSource> create)
{
    private readonly object _gate = new();
    private IDiagnosticSource? _source;
    private DiagnosticSample? _latest;
    private long _stamp;
    private int _window, _users;

    public async Task RunAsync(int intervalMs, int window, Action<DiagnosticSample> publish, CancellationToken token)
    {
        await Task.Run(async () =>
        {
            lock (_gate) _users++;
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    DiagnosticSample sample;
                    lock (_gate)
                    {
                        _source ??= create();
                        if (_latest == null || window != _window || Stopwatch.GetElapsedTime(_stamp).TotalMilliseconds >= Math.Min(500, intervalMs))
                        {
                            _latest = _source.Read(window);
                            _stamp = Stopwatch.GetTimestamp();
                            _window = window;
                        }
                        sample = _latest;
                    }
                    token.ThrowIfCancellationRequested();
                    publish(sample);
                    await Task.Delay(intervalMs, token);
                }
            }
            finally
            {
                lock (_gate)
                {
                    if (--_users == 0)
                    {
                        _source?.Dispose();
                        _source = null;
                        _latest = null;
                    }
                }
            }
        }, token);
    }
}

public sealed class WindowsDiagnosticSource : IDiagnosticSource
{
    private readonly LiveMetrics _load = new();
    private SensorReadings? _sensors;
    private long _sensorStamp;
    private string? _lastPath;
    private long _lastLength, _lastGrowth;
    public DiagnosticSample Read(int frames)
    {
        var timer = Stopwatch.StartNew();
        var load = _load.Sample();
        if (_sensors == null || Stopwatch.GetElapsedTime(_sensorStamp).TotalSeconds >= 5)
        {
            _sensors = ReadSensors();
            _sensorStamp = Stopwatch.GetTimestamp();
        }
        var capture = FpsCapture.Active;
        double[] times = [];
        var reason = "Запустите запись PresentMon для выбранной игры";
        if (capture is { HasExited: false })
        {
            reason = "Ожидание кадров " + capture.ExeName;
            try
            {
                using var file = new FileStream(capture.CsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long length = file.Length;
                if (_lastPath != capture.CsvPath || length != _lastLength)
                {
                    _lastGrowth = Stopwatch.GetTimestamp();
                    _lastPath = capture.CsvPath;
                    _lastLength = length;
                }
                if (Stopwatch.GetElapsedTime(_lastGrowth).TotalSeconds < 3)
                {
                    // Fixed byte budget even for a 90-minute match. Ignore incomplete rows.
                    using var reader = new StreamReader(file);
                    var header = reader.ReadLine();
                    long offset = Math.Max(0, length - 512 * 1024);
                    reader.DiscardBufferedData();
                    file.Position = offset;
                    reader.ReadLine();
                    var buffer = new char[512 * 1024];
                    int count = reader.ReadBlock(buffer, 0, (int)Math.Min(buffer.Length, length-offset));
                    var tail = new string(buffer, 0, count);
                    var end = tail.LastIndexOf('\n');
                    if (end >= 0)
                        times = PresentMonCsv.ReadFrameTimes(header + "\n" + tail[..end], capture.ExeName)
                            .Where(ms => ms is > 0 and < 5000).TakeLast(frames).ToArray();
                    if (times.Length >= 10) reason = capture.ExeName;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { reason = "Нет доступа к CSV PresentMon"; }
        }
        else if (capture != null)
            reason = capture.StartError ?? (capture.Declined ? "Запись отклонена" : "Запись PresentMon завершена");
        return new(load, _sensors, FrameStats.FromFrameTimes(times), times, reason, timer.Elapsed.TotalMilliseconds);
    }

    private static SensorReadings ReadSensors()
    {
        double? cpu = null, gpu = null, vram = null, thread = null, mhz = null, disk = null;
        void Query(string scope, string sql, Action<ManagementBaseObject> visit)
        {
            try
            {
                using var query = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(sql),
                    new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), Rewindable = false });
                using var rows = query.Get();
                foreach (var row in rows) using (row) visit(row);
            }
            catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        }
        Query(@"root\LibreHardwareMonitor", "SELECT Identifier,Value FROM Sensor WHERE SensorType='Temperature'", row =>
        {
            var id = row["Identifier"] as string ?? "";
            if (row["Value"] == null) return;
            var value = Convert.ToDouble(row["Value"]);
            if (value is < 1 or > 130) return;
            if (id.Contains("cpu", StringComparison.OrdinalIgnoreCase)) cpu = Math.Max(cpu ?? value, value);
            if (id.Contains("gpu", StringComparison.OrdinalIgnoreCase)) gpu = Math.Max(gpu ?? value, value);
        });
        Query(@"root\CIMV2", "SELECT DedicatedUsage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory", row =>
        {
            if (row["DedicatedUsage"] is {} bytes) vram = Math.Max(vram ?? 0, Convert.ToDouble(bytes) / (1L << 30));
        });
        Query(@"root\CIMV2", "SELECT Name,PercentProcessorTime,ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation", row =>
        {
            if ((row["Name"] as string)?.Contains("Total") != false) return;
            if (row["PercentProcessorTime"] is {} busy) thread = Math.Max(thread ?? 0, Math.Min(100, Convert.ToDouble(busy)));
            if (row["ProcessorFrequency"] is {} freq) mhz = Math.Max(mhz ?? 0, Convert.ToDouble(freq));
        });
        Query(@"root\CIMV2", "SELECT PercentIdleTime FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'", row =>
        {
            if (row["PercentIdleTime"] is {} idle) disk = Math.Clamp(100 - Convert.ToDouble(idle), 0, 100);
        });
        return new(cpu, gpu, vram, thread, mhz, disk);
    }

    public void Dispose() => _load.Dispose();
}
