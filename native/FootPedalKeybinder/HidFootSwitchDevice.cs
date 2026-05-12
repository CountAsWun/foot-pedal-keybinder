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

    public void WriteAllSlots(KeyBinding binding)
    {
        using var handle = Open();
        WritePacket(handle, [ReportId, 0x80, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00]);
        Thread.Sleep(1000);

        for (byte slot = 1; slot <= 3; slot++)
        {
            WritePacket(handle, [ReportId, 0x81, 0x08, slot, 0x00, 0x00, 0x00, 0x00]);
            Thread.Sleep(30);
            WritePacket(handle, [ReportId, 0x08, 0x01, binding.Modifier, binding.Usage, 0x00, 0x00, 0x00]);
            Thread.Sleep(30);
        }
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

    private static void WritePacket(SafeFileHandle handle, byte[] packet)
    {
        if (packet.Length != 8)
        {
            throw new ArgumentException("Footswitch packets must be exactly 8 bytes.", nameof(packet));
        }

        var ok = WriteFile(handle, packet, packet.Length, out var written, IntPtr.Zero);
        if (!ok || written != packet.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to write the HID report.");
        }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }
}
