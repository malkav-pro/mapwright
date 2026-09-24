using Godot;
using System.Runtime.InteropServices;

namespace Mapwright.Rendering;

public enum RenderHardwareStatus
{
    Ready = 0,
    InspectionOnly = 1
}

public sealed record RenderHardwareSnapshot(
    long ReportedVramBytes,
    long ReportedSystemRamBytes,
    long EngineCompositorHeadroomBytes,
    RenderHardwareStatus Status,
    string Source,
    string Detail);

public sealed class RenderResourceLedger
{
    public const long Mebibyte = 1024L * 1024L;
    public const long Gibibyte = 1024L * Mebibyte;
    public const long MaximumGpuBytes = 4L * Gibibyte;
    public const long MaximumDecodedCpuBytes = 512L * Mebibyte;
    public const long MaximumExportBufferBytes = 512L * Mebibyte;
    public const long MaximumHistoryAccelerationBytes = 4L * Gibibyte;

    public RenderResourceLedger(
        long reportedVramBytes,
        long reportedSystemRamBytes,
        long engineCompositorHeadroomBytes)
    {
        if (reportedVramBytes < 0) throw new ArgumentOutOfRangeException(nameof(reportedVramBytes));
        if (reportedSystemRamBytes <= 0) throw new ArgumentOutOfRangeException(nameof(reportedSystemRamBytes));
        if (engineCompositorHeadroomBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(engineCompositorHeadroomBytes));

        ReportedVramBytes = reportedVramBytes;
        ReportedSystemRamBytes = reportedSystemRamBytes;
        EngineCompositorHeadroomBytes = engineCompositorHeadroomBytes;
        var grossGpuBudget = Math.Min(checked(reportedVramBytes / 4), MaximumGpuBytes);
        GpuBudgetBytes = Math.Max(0, checked(grossGpuBudget - engineCompositorHeadroomBytes));
        ProcessBudgetBytes = checked(reportedSystemRamBytes / 4);
    }

    public long ReportedVramBytes { get; }
    public long ReportedSystemRamBytes { get; }
    public long EngineCompositorHeadroomBytes { get; }
    public long GpuBudgetBytes { get; }
    public long DecodedCpuBudgetBytes => MaximumDecodedCpuBytes;
    public long ExportBufferBudgetBytes => MaximumExportBufferBytes;
    public long ProcessBudgetBytes { get; }
    public long HistoryAccelerationBudgetBytes => MaximumHistoryAccelerationBytes;

    public bool Fits(
        long gpuBytes,
        long decodedCpuBytes,
        long exportBufferBytes,
        long processBytes,
        long historyAccelerationBytes)
    {
        if (gpuBytes < 0 || decodedCpuBytes < 0 || exportBufferBytes < 0 ||
            processBytes < 0 || historyAccelerationBytes < 0)
            return false;
        return gpuBytes <= GpuBudgetBytes &&
               decodedCpuBytes <= DecodedCpuBudgetBytes &&
               exportBufferBytes <= ExportBufferBudgetBytes &&
               processBytes <= ProcessBudgetBytes &&
               historyAccelerationBytes <= HistoryAccelerationBudgetBytes;
    }

    public void AdmitOrThrow(
        long gpuBytes,
        long decodedCpuBytes,
        long exportBufferBytes,
        long processBytes,
        long historyAccelerationBytes,
        string operation)
    {
        if (Fits(gpuBytes, decodedCpuBytes, exportBufferBytes, processBytes, historyAccelerationBytes))
            return;
        throw new RenderBudgetExceededException(
            $"{operation} exceeds a render resource envelope before allocation: " +
            $"gpu={gpuBytes}/{GpuBudgetBytes}, decoded={decodedCpuBytes}/{DecodedCpuBudgetBytes}, " +
            $"export={exportBufferBytes}/{ExportBufferBudgetBytes}, process={processBytes}/{ProcessBudgetBytes}, " +
            $"history={historyAccelerationBytes}/{HistoryAccelerationBudgetBytes} bytes.");
    }

    public int FitBandHeight(
        int width,
        int desiredInteriorHeight,
        int haloRows,
        int bytesPerPixel,
        int concurrency)
    {
        if (width <= 0 || desiredInteriorHeight <= 0 || haloRows < 0 ||
            bytesPerPixel <= 0 || concurrency <= 0)
            return 0;
        try
        {
            var bytesPerRow = checked((long)width * bytesPerPixel * concurrency);
            var maximumRows = Math.Min(ExportBufferBudgetBytes, ProcessBudgetBytes) / bytesPerRow;
            var maximumInteriorRows = checked(maximumRows - checked(haloRows * 2L));
            return maximumInteriorRows <= 0
                ? 0
                : checked((int)Math.Min(desiredInteriorHeight, maximumInteriorRows));
        }
        catch (OverflowException)
        {
            return 0;
        }
    }

    public static RenderHardwareSnapshot CaptureStartup(RenderingDevice? renderingDevice = null)
    {
        var memory = OS.GetMemoryInfo();
        var systemRam = ReadMemory(memory, "physical");
        var device = renderingDevice ?? RenderingServer.GetRenderingDevice();
        if (device is null)
            return new RenderHardwareSnapshot(0, systemRam, 0, RenderHardwareStatus.InspectionOnly,
                "Godot startup query", "Required RenderingDevice is unavailable; rendering is disabled, inspection/recovery remains available.");

        // GetDeviceTotalMemory is tracked *usage*, and is zero in Godot release builds.
        // Budget from the selected Vulkan physical device's advertised local heaps instead.
        var reportedDeviceMemory = VulkanLocalMemoryBytes(device);
        var engineMemory = checked((long)device.GetMemoryUsage(RenderingDevice.MemoryType.Total));
        var headroom = engineMemory;
        if (reportedDeviceMemory <= 0 || systemRam <= 0)
            return new RenderHardwareSnapshot(reportedDeviceMemory, systemRam, headroom,
                RenderHardwareStatus.InspectionOnly, "Vulkan physical-device and OS startup query",
                "The selected backend did not expose a usable local-memory capacity or physical-memory value; rendering is disabled, inspection/recovery remains available.");
        return new RenderHardwareSnapshot(reportedDeviceMemory, systemRam, headroom,
            RenderHardwareStatus.Ready, "Vulkan physical-device memory heaps and OS.GetMemoryInfo",
            "Device and process limits were captured before render allocations.");
    }

    public static RenderResourceLedger FromSnapshot(RenderHardwareSnapshot snapshot)
    {
        if (snapshot.Status != RenderHardwareStatus.Ready)
            throw new NotSupportedException(snapshot.Detail);
        return new RenderResourceLedger(snapshot.ReportedVramBytes, snapshot.ReportedSystemRamBytes,
            snapshot.EngineCompositorHeadroomBytes);
    }

    public static RenderResourceLedger ForCpuInspection()
    {
        var snapshot = CaptureStartup();
        if (snapshot.ReportedSystemRamBytes <= 0)
            throw new NotSupportedException(
                "Physical memory is unavailable; bounded rendering is disabled and only source inspection/recovery is available.");
        return new RenderResourceLedger(0, snapshot.ReportedSystemRamBytes, 0);
    }

    private static long ReadMemory(Godot.Collections.Dictionary memory, StringName key)
    {
        if (!memory.TryGetValue(key, out var value)) return 0;
        var bytes = value.AsInt64();
        return Math.Max(0, bytes);
    }

    private const int VulkanMemoryPropertiesBytes = 520;
    private const int VulkanHeapCountOffset = 260;
    private const int VulkanHeapArrayOffset = 264;
    private const int VulkanHeapStride = 16;
    private const int VulkanMaximumHeaps = 16;
    private const int VulkanDeviceLocalHeapBit = 1;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetPhysicalDeviceMemoryProperties(IntPtr physicalDevice, IntPtr properties);

    private static long VulkanLocalMemoryBytes(RenderingDevice device)
    {
        if (!string.Equals(RenderingServer.GetCurrentRenderingDriverName(), "vulkan",
                StringComparison.OrdinalIgnoreCase))
            return 0;
        var physical = device.GetDriverResource(RenderingDevice.DriverResource.PhysicalDevice, default, 0);
        if (physical == 0) return 0;
        var loader = OperatingSystem.IsWindows() ? "vulkan-1.dll" : "libvulkan.so.1";
        if (!NativeLibrary.TryLoad(loader, out var library)) return 0;
        try
        {
            if (!NativeLibrary.TryGetExport(library, "vkGetPhysicalDeviceMemoryProperties", out var address))
                return 0;
            var query = Marshal.GetDelegateForFunctionPointer<GetPhysicalDeviceMemoryProperties>(address);
            var properties = Marshal.AllocHGlobal(VulkanMemoryPropertiesBytes);
            try
            {
                Marshal.Copy(new byte[VulkanMemoryPropertiesBytes], 0, properties, VulkanMemoryPropertiesBytes);
                query(new IntPtr(unchecked((long)physical)), properties);
                var bytes = new byte[VulkanMemoryPropertiesBytes];
                Marshal.Copy(properties, bytes, 0, bytes.Length);
                return ParseVulkanLocalMemoryBytes(bytes);
            }
            finally
            {
                Marshal.FreeHGlobal(properties);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    internal static long ParseVulkanLocalMemoryBytes(ReadOnlySpan<byte> properties)
    {
        if (properties.Length < VulkanMemoryPropertiesBytes) return 0;
        var heapCount = BitConverter.ToInt32(properties.Slice(VulkanHeapCountOffset, sizeof(int)));
        if (heapCount is < 1 or > VulkanMaximumHeaps) return 0;
        long total = 0;
        for (var index = 0; index < heapCount; index++)
        {
            var offset = VulkanHeapArrayOffset + index * VulkanHeapStride;
            var flags = BitConverter.ToInt32(properties.Slice(offset + 8, sizeof(int)));
            if ((flags & VulkanDeviceLocalHeapBit) == 0) continue;
            var bytes = BitConverter.ToUInt64(properties.Slice(offset, sizeof(ulong)));
            if (bytes == 0 || bytes > long.MaxValue || total > long.MaxValue - (long)bytes)
                return 0;
            total += (long)bytes;
        }
        return total;
    }
}

public sealed class RenderBudgetExceededException(string message) : InvalidOperationException(message);

public enum CoastStyleBranch
{
    UnstyledGeneratedEdge = 0,
    FixedOuterCoast = 1
}

public static class CoastStylePolicy
{
    public static CoastStyleBranch SelectedBranch => CoastStyleBranch.UnstyledGeneratedEdge;
    public static bool DistanceFieldActive => false;
    public const string Reason =
        "D-20 fallback: merged imported coverage cannot prove outer-coast identity separately from river, mouth, and lake-like banks.";
}
