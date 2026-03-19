using System;
using System.Linq;
using System.Management;

namespace OptiscalerClient.Services
{
    /// <summary>
    /// GPU vendor types
    /// </summary>
    public enum GpuVendor
    {
        Unknown,
        NVIDIA,
        AMD,
        Intel
    }

    /// <summary>
    /// Information about a detected GPU
    /// </summary>
    public class GpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
        public string DriverVersion { get; set; } = string.Empty;
        public ulong VideoMemoryBytes { get; set; }
        
        public string VideoMemoryGB => $"{VideoMemoryBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    /// <summary>
    /// Service to detect GPU information using WMI
    /// </summary>
    public class GpuDetectionService
    {
        /// <summary>
        /// Detects all GPUs in the system
        /// </summary>
        public GpuInfo[] DetectGPUs()
        {
            try
            {
                var gpus = new System.Collections.Generic.List<GpuInfo>();
                
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var gpu = new GpuInfo();
                        
                        // Get GPU name
                        gpu.Name = obj["Name"]?.ToString() ?? "Unknown GPU";
                        
                        // Detect vendor from name
                        gpu.Vendor = DetectVendorFromName(gpu.Name);
                        
                        // Get driver version
                        gpu.DriverVersion = obj["DriverVersion"]?.ToString() ?? "Unknown";
                        
                        // Get video memory (in bytes)
                        if (obj["AdapterRAM"] != null)
                        {
                            try
                            {
                                gpu.VideoMemoryBytes = Convert.ToUInt64(obj["AdapterRAM"]);
                            }
                            catch { }
                        }
                        
                        gpus.Add(gpu);
                    }
                }
                
                return gpus.ToArray();
            }
            catch
            {
                return Array.Empty<GpuInfo>();
            }
        }

        /// <summary>
        /// Gets the primary (first) GPU
        /// </summary>
        public GpuInfo? GetPrimaryGPU()
        {
            var gpus = DetectGPUs();
            return gpus.Length > 0 ? gpus[0] : null;
        }

        /// <summary>
        /// Gets the most powerful discrete GPU (prefers NVIDIA > AMD > Intel)
        /// </summary>
        public GpuInfo? GetDiscreteGPU()
        {
            var gpus = DetectGPUs();
            
            // Filter out integrated GPUs (usually have less VRAM)
            var discreteGpus = gpus.Where(g => g.VideoMemoryBytes > 2L * 1024 * 1024 * 1024).ToArray(); // > 2GB
            
            if (discreteGpus.Length == 0)
                return gpus.Length > 0 ? gpus[0] : null;
            
            // Prefer NVIDIA, then AMD, then Intel
            var nvidia = discreteGpus.FirstOrDefault(g => g.Vendor == GpuVendor.NVIDIA);
            if (nvidia != null) return nvidia;
            
            var amd = discreteGpus.FirstOrDefault(g => g.Vendor == GpuVendor.AMD);
            if (amd != null) return amd;
            
            var intel = discreteGpus.FirstOrDefault(g => g.Vendor == GpuVendor.Intel);
            if (intel != null) return intel;
            
            return discreteGpus[0];
        }

        /// <summary>
        /// Detects GPU vendor from the GPU name string
        /// </summary>
        private GpuVendor DetectVendorFromName(string gpuName)
        {
            if (string.IsNullOrEmpty(gpuName))
                return GpuVendor.Unknown;
            
            var nameLower = gpuName.ToLowerInvariant();
            
            // NVIDIA detection
            if (nameLower.Contains("nvidia") || 
                nameLower.Contains("geforce") || 
                nameLower.Contains("quadro") || 
                nameLower.Contains("tesla") ||
                nameLower.Contains("rtx") ||
                nameLower.Contains("gtx"))
            {
                return GpuVendor.NVIDIA;
            }
            
            // AMD detection
            if (nameLower.Contains("amd") || 
                nameLower.Contains("radeon") || 
                nameLower.Contains("rx ") ||
                nameLower.Contains("vega") ||
                nameLower.Contains("navi"))
            {
                return GpuVendor.AMD;
            }
            
            // Intel detection
            if (nameLower.Contains("intel") || 
                nameLower.Contains("iris") || 
                nameLower.Contains("uhd graphics") ||
                nameLower.Contains("hd graphics") ||
                nameLower.Contains("arc"))
            {
                return GpuVendor.Intel;
            }
            
            return GpuVendor.Unknown;
        }

        /// <summary>
        /// Checks if the system has a specific GPU vendor
        /// </summary>
        public bool HasGPU(GpuVendor vendor)
        {
            var gpus = DetectGPUs();
            return gpus.Any(g => g.Vendor == vendor);
        }

        /// <summary>
        /// Gets a user-friendly description of the GPU setup
        /// </summary>
        public string GetGPUDescription()
        {
            var gpus = DetectGPUs();
            
            if (gpus.Length == 0)
                return "No GPU detected";
            
            if (gpus.Length == 1)
                return $"{GetVendorIcon(gpus[0].Vendor)} {gpus[0].Name}";
            
            // Multiple GPUs
            var discrete = GetDiscreteGPU();
            if (discrete != null)
                return $"{GetVendorIcon(discrete.Vendor)} {discrete.Name} (+{gpus.Length - 1} more)";
            
            return $"{gpus.Length} GPUs detected";
        }

        /// <summary>
        /// Gets an emoji/icon for the GPU vendor
        /// </summary>
        private string GetVendorIcon(GpuVendor vendor)
        {
            return vendor switch
            {
                GpuVendor.NVIDIA => "🟢",
                GpuVendor.AMD => "🔴",
                GpuVendor.Intel => "🔵",
                _ => "⚪"
            };
        }
    }
}
