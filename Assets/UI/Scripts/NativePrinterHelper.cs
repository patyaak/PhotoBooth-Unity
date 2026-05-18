using System;
using System.Runtime.InteropServices;

public static class NativePrinterHelper
{
    // Win32 API References
    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "GetPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool GetPrinter(IntPtr hPrinter, int dwLevel, IntPtr pAddr, int dwBuf, out int dwNeeded);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint GetPrinterData(IntPtr hPrinter, string pValueName, out uint pType, IntPtr pData, uint nSize, out uint pcbNeeded);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint GetPrinterDataEx(IntPtr hPrinter, string pKeyName, string pValueName, out uint pType, IntPtr pData, uint nSize, out uint pcbNeeded);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern uint EnumPrinterDataEx(IntPtr hPrinter, string pKeyName, IntPtr pEnumValues, uint cbEnumValues, out uint pcbEnumValues, out uint pnEnumValues);


    public const uint REG_SZ = 1;
    public const uint REG_BINARY = 3;
    public const uint REG_DWORD = 4;

    // Structure for Printer Info Level 2 (Standard Status)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PRINTER_INFO_2
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pServerName;
        [MarshalAs(UnmanagedType.LPStr)] public string pPrinterName;
        [MarshalAs(UnmanagedType.LPStr)] public string pShareName;
        [MarshalAs(UnmanagedType.LPStr)] public string pPortName;
        [MarshalAs(UnmanagedType.LPStr)] public string pDriverName;
        [MarshalAs(UnmanagedType.LPStr)] public string pComment;
        [MarshalAs(UnmanagedType.LPStr)] public string pLocation;
        public IntPtr pDevMode;
        [MarshalAs(UnmanagedType.LPStr)] public string pSepFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pPrintProcessor;
        [MarshalAs(UnmanagedType.LPStr)] public string pDatatype;
        [MarshalAs(UnmanagedType.LPStr)] public string pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs; 
        public uint AveragePPM;
    }

    // Status Constants
    public const uint PRINTER_STATUS_PAUSED = 0x00000001;
    public const uint PRINTER_STATUS_ERROR = 0x00000002;
    public const uint PRINTER_STATUS_PENDING_DELETION = 0x00000004;
    public const uint PRINTER_STATUS_PAPER_JAM = 0x00000008;
    public const uint PRINTER_STATUS_PAPER_OUT = 0x00000010;
    public const uint PRINTER_STATUS_MANUAL_FEED = 0x00000020;
    public const uint PRINTER_STATUS_PAPER_PROBLEM = 0x00000040;
    public const uint PRINTER_STATUS_OFFLINE = 0x00000080;
    public const uint PRINTER_STATUS_IO_ACTIVE = 0x00000100;
    public const uint PRINTER_STATUS_BUSY = 0x00000200;
    public const uint PRINTER_STATUS_PRINTING = 0x00000400;
    public const uint PRINTER_STATUS_OUTPUT_BIN_FULL = 0x00000800;
    public const uint PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;
    public const uint PRINTER_STATUS_WAITING = 0x00002000;
    public const uint PRINTER_STATUS_PROCESSING = 0x00004000;
    public const uint PRINTER_STATUS_INITIALIZING = 0x00008000;
    public const uint PRINTER_STATUS_WARMING_UP = 0x00010000;
    public const uint PRINTER_STATUS_TONER_LOW = 0x00020000;
    public const uint PRINTER_STATUS_NO_TONER = 0x00040000;
    public const uint PRINTER_STATUS_PAGE_PUNT = 0x00080000;
    public const uint PRINTER_STATUS_USER_INTERVENTION = 0x00100000;
    public const uint PRINTER_STATUS_OUT_OF_MEMORY = 0x00200000;
    public const uint PRINTER_STATUS_DOOR_OPEN = 0x00400000;
    public const uint PRINTER_STATUS_SERVER_UNKNOWN = 0x00800000;
    public const uint PRINTER_STATUS_POWER_SAVE = 0x01000000;

    // Attributes Constants
    public const uint PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x00000400;

    public static string GetPrinterStatus(string printerName)
    {
        IntPtr hPrinter;
        if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
        {
            return "NOTFOUND";
        }

        try
        {
            int dwNeeded = 0;
            GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out dwNeeded);
            if (dwNeeded == 0) return "UNKNOWN";

            IntPtr pAddr = Marshal.AllocHGlobal(dwNeeded);
            try
            {
                if (GetPrinter(hPrinter, 2, pAddr, dwNeeded, out dwNeeded))
                {
                    PRINTER_INFO_2 info = (PRINTER_INFO_2)Marshal.PtrToStructure(pAddr, typeof(PRINTER_INFO_2));
                    return ParseStatusCode(info.Status, info.Attributes, info.cJobs);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pAddr);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }

        return "UNKNOWN";
    }

    public static string GetPrinterPort(string printerName)
    {
        IntPtr hPrinter;
        if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
        {
            return null;
        }

        try
        {
            int dwNeeded = 0;
            GetPrinter(hPrinter, 2, IntPtr.Zero, 0, out dwNeeded);
            if (dwNeeded == 0) return null;

            IntPtr pAddr = Marshal.AllocHGlobal(dwNeeded);
            try
            {
                if (GetPrinter(hPrinter, 2, pAddr, dwNeeded, out dwNeeded))
                {
                    PRINTER_INFO_2 info = (PRINTER_INFO_2)Marshal.PtrToStructure(pAddr, typeof(PRINTER_INFO_2));
                    return info.pPortName;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pAddr);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }

        return null;
    }

    private static string ParseStatusCode(uint status, uint attributes, uint jobs)
    {
        // DEBUG LOGGING
        UnityEngine.Debug.Log($"[NativePrinterHelper] Status: {status:X} | Attributes: {attributes:X} | Jobs: {jobs}");
        
        // Helper string for debug
        string debugInfo = $" (S:{status:X} A:{attributes:X})";

        if ((status & PRINTER_STATUS_PAPER_JAM) != 0) return "ERROR_PAPER_JAM" + debugInfo;
        if ((status & PRINTER_STATUS_PAPER_OUT) != 0) return "ERROR_PAPER_OUT" + debugInfo;
        if ((status & PRINTER_STATUS_PAPER_PROBLEM) != 0) return "ERROR_PAPER_PROBLEM" + debugInfo;
        
        // CHECK BOTH STATUS AND ATTRIBUTES FOR OFFLINE
        if ((status & PRINTER_STATUS_OFFLINE) != 0) return "ERROR_OFFLINE" + debugInfo;
        if ((attributes & PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0) return "ERROR_OFFLINE" + debugInfo;

        if ((status & PRINTER_STATUS_DOOR_OPEN) != 0) return "ERROR_DOOR_OPEN" + debugInfo;
        if ((status & PRINTER_STATUS_NO_TONER) != 0) return "ERROR_NO_TONER" + debugInfo;
        if ((status & PRINTER_STATUS_TONER_LOW) != 0) return "WARNING_TONER_LOW" + debugInfo;
        if ((status & PRINTER_STATUS_ERROR) != 0) return "ERROR_GENERIC" + debugInfo;
        if ((status & PRINTER_STATUS_USER_INTERVENTION) != 0) return "Wait_User_Intervention" + debugInfo;
        
        if ((status & PRINTER_STATUS_PRINTING) != 0) return "Status_Printing";
        if ((status & PRINTER_STATUS_BUSY) != 0) return "Status_Busy";
        
        if (jobs > 0) return "Status_Printing"; 

        return "Ready";
    }
}
//test