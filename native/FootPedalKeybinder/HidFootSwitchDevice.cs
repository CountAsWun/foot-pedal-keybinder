using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FootPedalKeybinder;

internal sealed class HidFootSwitchDevice
{
    private const ushort VendorId = 0x3553;
    private const ushort ProductId = 0xB001;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const byte ReportId = 0x01;

    private HidFootSwitchDevice(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static IReadOnlyList<HidFootSwitchDevice> FindAll()
    {
        HidD_GetHidGuid(out var hidGuid);
        var infoSet = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == IntPtr.Zero || infoSet.ToInt64() == -1)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate HID devices.");
        }

        try
        {
            var devices = new List<HidFootSwitchDevice>();
            uint index = 0;
            while (true)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    CbSize = Marshal.SizeOf<SpDeviceInterfaceData>(),
                };

                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "Unable to enumerate HID device interfaces.");
                }

                var path = GetDevicePath(infoSet, interfaceData);
                if (IsTargetFootSwitch(path))
                {
                    devices.Add(new HidFootSwitchDevice(path));
                }

                index++;
            }

            return devices;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    public IReadOnlyList<string> WriteAllSlots(KeyBinding binding)
    {
        var results = new List<string>();
        using var handle = Open();
        results.Add("start: " + WritePacket(handle, [ReportId, 0x80, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00]));
        Thread.Sleep(1000);

        for (byte slot = 1; slot <= 3; slot++)
        {
            results.Add($"slot {slot} header: " + WritePacket(handle, [ReportId, 0x81, 0x08, slot, 0x00, 0x00, 0x00, 0x00]));
            Thread.Sleep(30);
            results.Add($"slot {slot} data: " + WritePacket(handle, [ReportId, 0x08, 0x01, binding.Modifier, binding.Usage, 0x00, 0x00, 0x00]));
            Thread.Sleep(30);
        }

        return results;
    }

    public IReadOnlyList<string> ReadSlots()
    {
        var results = new List<string>();
        using var handle = Open();

        for (byte slot = 1; slot <= 3; slot++)
        {
            var writeMethod = WritePacket(handle, [ReportId, 0x82, 0x08, slot, 0x00, 0x00, 0x00, 0x00]);
            var response = ReadPacket(handle);
            results.Add($"slot {slot} read query: {writeMethod}");
            results.Add($"slot {slot} raw: {ToHex(response)}");

            var report = NormalizeResponse(response);
            if (report.Length >= 4)
            {
                results.Add($"slot {slot} decoded: type 0x{report[1]:X2}, {KeyBindings.DescribeUsage(report[2], report[3])}");
            }
        }

        return results;
    }

    private SafeFileHandle Open()
    {
        var handle = CreateFile(
            Path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open the foot pedal HID interface.");
        }

        return handle;
    }

    private static string WritePacket(SafeFileHandle handle, byte[] packet)
    {
        if (packet.Length != 8)
        {
            throw new ArgumentException("Footswitch packets must be exactly 8 bytes.", nameof(packet));
        }

        var caps = GetCaps(handle);
        var failures = new List<string>();

        foreach (var report in BuildCandidateReports(packet, caps.OutputReportByteLength))
        {
            var ok = WriteFile(handle, report.Buffer, report.Buffer.Length, out var written, IntPtr.Zero);
            if (ok && written == report.Buffer.Length)
            {
                return $"WriteFile/{report.Label}";
            }

            failures.Add($"WriteFile/{report.Label}: {FormatLastError()} wrote {written}/{report.Buffer.Length} bytes");

            if (HidD_SetOutputReport(handle, report.Buffer, report.Buffer.Length))
            {
                return $"HidD_SetOutputReport/{report.Label}";
            }

            failures.Add($"HidD_SetOutputReport/{report.Label}: {FormatLastError()}");
        }

        foreach (var report in BuildCandidateReports(packet, caps.FeatureReportByteLength))
        {
            if (HidD_SetFeature(handle, report.Buffer, report.Buffer.Length))
            {
                return $"HidD_SetFeature/{report.Label}";
            }

            failures.Add($"HidD_SetFeature/{report.Label}: {FormatLastError()}");
        }

        throw new InvalidOperationException("Unable to write the HID report. " + string.Join("; ", failures));
    }

    private static HidCaps GetCaps(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get HID preparsed data.");
        }

        try
        {
            var result = HidP_GetCaps(preparsedData, out var caps);
            if (result != 0x00110000)
            {
                throw new InvalidOperationException($"Unable to read HID capabilities. HidP_GetCaps returned 0x{result:X8}.");
            }

            return caps;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    private static IEnumerable<(string Label, byte[] Buffer)> BuildCandidateReports(byte[] packet, ushort reportLength)
    {
        if (reportLength < packet.Length)
        {
            yield break;
        }

        if (reportLength >= packet.Length + 1)
        {
            var zeroPrefixed = new byte[reportLength];
            Array.Copy(packet, 0, zeroPrefixed, 1, packet.Length);
            yield return ($"zero-prefixed length {reportLength}", zeroPrefixed);
        }

        var raw = new byte[reportLength];
        Array.Copy(packet, raw, packet.Length);
        yield return ($"raw length {reportLength}", raw);
    }

    private static byte[] ReadPacket(SafeFileHandle handle)
    {
        var caps = GetCaps(handle);
        var buffer = new byte[Math.Max(caps.InputReportByteLength, (ushort)8)];
        if (!ReadFile(handle, buffer, buffer.Length, out var read, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read the HID report.");
        }

        return buffer.Take(read).ToArray();
    }

    private static byte[] NormalizeResponse(byte[] response)
    {
        if (response.Length >= 9 && response[0] == 0x00)
        {
            return response.Skip(1).Take(8).ToArray();
        }

        return response.Take(8).ToArray();
    }

    private static string ToHex(byte[] data)
    {
        return string.Join(" ", data.Select(value => value.ToString("X2")));
    }

    private static string FormatLastError()
    {
        var error = Marshal.GetLastWin32Error();
        return error == 0 ? "no Windows error code returned" : $"{error} ({new Win32Exception(error).Message})";
    }

    private static string GetDevicePath(IntPtr infoSet, SpDeviceInterfaceData interfaceData)
    {
        SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
        var error = Marshal.GetLastWin32Error();
        if (requiredSize == 0 && error != 122)
        {
            throw new Win32Exception(error, "Unable to get HID device path size.");
        }

        var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
        try
        {
            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get HID device path.");
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, 4)) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(detailBuffer);
        }
    }

    private static bool IsTargetFootSwitch(string path)
    {
        var normalized = path.ToLowerInvariant();
        return normalized.Contains("vid_3553") &&
               normalized.Contains("pid_b001") &&
               normalized.Contains("mi_01");
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        int bytesToWrite,
        out int bytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(
        SafeFileHandle file,
        byte[] buffer,
        int bytesToRead,
        out int bytesRead,
        IntPtr overlapped);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(
        SafeFileHandle hidDeviceObject,
        out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HidCaps capabilities);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
}
