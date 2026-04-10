using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace OptiscalerClient.Services;

[SupportedOSPlatform("linux")]
public class LinuxGpuDetectionService : IGpuDetectionService
{
    public GpuInfo[] DetectGPUs()
    {
        try
        {
            var gpus = new List<GpuInfo>();
            const string drmPath = "/sys/class/drm";

            if (!Directory.Exists(drmPath))
                return Array.Empty<GpuInfo>();

            foreach (var cardDir in Directory.GetDirectories(drmPath, "card*"))
            {
                // Only process top-level card entries (card0, card1, ...) not sub-connectors
                if (!Regex.IsMatch(Path.GetFileName(cardDir), @"^card\d+$"))
                    continue;

                var devicePath = Path.Combine(cardDir, "device");
                if (!Directory.Exists(devicePath)) continue;

                var vendorFile = Path.Combine(devicePath, "vendor");
                if (!File.Exists(vendorFile)) continue;

                var vendorId = File.ReadAllText(vendorFile).Trim().ToLowerInvariant();
                var vendor = vendorId switch
                {
                    "0x10de" => GpuVendor.NVIDIA,
                    "0x1002" => GpuVendor.AMD,
                    "0x8086" => GpuVendor.Intel,
                    _ => GpuVendor.Unknown
                };

                if (vendor == GpuVendor.Unknown) continue;

                var gpu = new GpuInfo
                {
                    Vendor = vendor,
                    Name = GetGpuName(devicePath, vendor),
                    VideoMemoryBytes = GetVram(devicePath, vendor),
                    DriverVersion = GetDriverVersion(vendor)
                };

                gpus.Add(gpu);
            }

            return gpus.ToArray();
        }
        catch
        {
            return Array.Empty<GpuInfo>();
        }
    }

    private string GetGpuName(string devicePath, GpuVendor vendor)
    {
        try
        {
            var vendorIdRaw = File.ReadAllText(Path.Combine(devicePath, "vendor")).Trim();
            var deviceIdRaw = File.Exists(Path.Combine(devicePath, "device"))
                ? File.ReadAllText(Path.Combine(devicePath, "device")).Trim()
                : "";

            var shortVendor = vendorIdRaw.Replace("0x", "").PadLeft(4, '0');
            var shortDevice = deviceIdRaw.Replace("0x", "").PadLeft(4, '0');

            var output = RunProcess("lspci", $"-d {shortVendor}:{shortDevice} -mm", timeoutMs: 2000);
            if (!string.IsNullOrWhiteSpace(output))
            {
                var parts = Regex.Matches(output, "\"([^\"]*)\"");
                if (parts.Count >= 3)
                    return $"{parts[1].Groups[1].Value} {parts[2].Groups[1].Value}".Trim();
            }
        }
        catch { }

        return vendor switch
        {
            GpuVendor.NVIDIA => "NVIDIA GPU",
            GpuVendor.AMD => "AMD GPU",
            GpuVendor.Intel => "Intel GPU",
            _ => "Unknown GPU"
        };
    }

    private ulong GetVram(string devicePath, GpuVendor vendor)
    {
        // AMD exposes VRAM size directly via sysfs
        if (vendor == GpuVendor.AMD)
        {
            var vramFile = Path.Combine(devicePath, "mem_info_vram_total");
            if (File.Exists(vramFile) && ulong.TryParse(File.ReadAllText(vramFile).Trim(), out var vram))
                return vram;
        }

        // NVIDIA: query via nvidia-smi (returns MiB)
        if (vendor == GpuVendor.NVIDIA)
        {
            var output = RunProcess("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits", timeoutMs: 3000);
            if (ulong.TryParse(output?.Trim(), out var mb))
                return mb * 1024 * 1024;
        }

        return 0;
    }

    private string GetDriverVersion(GpuVendor vendor)
    {
        if (vendor == GpuVendor.NVIDIA)
        {
            var output = RunProcess("nvidia-smi", "--query-gpu=driver_version --format=csv,noheader", timeoutMs: 3000);
            if (!string.IsNullOrWhiteSpace(output))
                return output.Trim();
        }

        var modulePath = vendor switch
        {
            GpuVendor.AMD => "/sys/module/amdgpu/version",
            GpuVendor.Intel => "/sys/module/i915/version",
            _ => null
        };

        if (modulePath != null && File.Exists(modulePath))
            return File.ReadAllText(modulePath).Trim();

        return "Unknown";
    }

    private string? RunProcess(string fileName, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadLine();
            proc.WaitForExit(timeoutMs);
            return output;
        }
        catch
        {
            return null;
        }
    }

    public GpuInfo? GetPrimaryGPU()
    {
        var gpus = DetectGPUs();
        return gpus.Length > 0 ? gpus[0] : null;
    }

    public GpuInfo? GetDiscreteGPU()
    {
        var gpus = DetectGPUs();
        var discreteGpus = gpus.Where(g => g.VideoMemoryBytes > 2L * 1024 * 1024 * 1024).ToArray();

        if (discreteGpus.Length == 0)
            return gpus.Length > 0 ? gpus[0] : null;

        return discreteGpus.FirstOrDefault(g => g.Vendor == GpuVendor.NVIDIA)
            ?? discreteGpus.FirstOrDefault(g => g.Vendor == GpuVendor.AMD)
            ?? discreteGpus[0];
    }

    public bool HasGPU(GpuVendor vendor)
    {
        return DetectGPUs().Any(g => g.Vendor == vendor);
    }

    public string GetGPUDescription()
    {
        var gpus = DetectGPUs();

        if (gpus.Length == 0)
            return "No GPU detected";

        if (gpus.Length == 1)
            return $"{GetVendorIcon(gpus[0].Vendor)} {gpus[0].Name}";

        var discrete = GetDiscreteGPU();
        if (discrete != null)
            return $"{GetVendorIcon(discrete.Vendor)} {discrete.Name} (+{gpus.Length - 1} more)";

        return $"{gpus.Length} GPUs detected";
    }

    private static string GetVendorIcon(GpuVendor vendor) => vendor switch
    {
        GpuVendor.NVIDIA => "🟢",
        GpuVendor.AMD => "🔴",
        GpuVendor.Intel => "🔵",
        _ => "⚪"
    };
}
