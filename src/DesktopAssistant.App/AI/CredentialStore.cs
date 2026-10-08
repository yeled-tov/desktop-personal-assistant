using System.Runtime.InteropServices;
using System.Text;

namespace DesktopAssistant.AI;

/// <summary>Stores provider secrets in Windows Credential Manager, never in app settings.</summary>
public static class CredentialStore
{
    private const string Target = "DesktopAssistant/Gemini";
    private const uint Generic = 1, LocalMachinePersistence = 2;

    public static void SaveApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("מפתח API ריק.");
        var bytes = Encoding.Unicode.GetBytes(apiKey);
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var credential = new Native.Credential { Type = Generic, TargetName = Target, CredentialBlobSize = (uint)bytes.Length, CredentialBlob = memory, Persist = LocalMachinePersistence, UserName = "API key" };
            if (!Native.CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "שמירת המפתח במנהל האישורים נכשלה.");
        }
        finally { Marshal.ZeroFreeHGlobal(memory); Array.Clear(bytes); }
    }

    public static bool TryGetApiKey(out string apiKey)
    {
        apiKey = "";
        if (!Native.CredRead(Target, Generic, 0, out var pointer)) return false;
        try
        {
            var credential = Marshal.PtrToStructure<Native.Credential>(pointer);
            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            apiKey = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            Array.Clear(bytes);
            return apiKey.Length > 0;
        }
        finally { Native.CredFree(pointer); }
    }

    public static void DeleteApiKey()
    {
        if (!Native.CredDelete(Target, Generic, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) throw new System.ComponentModel.Win32Exception(error, "מחיקת המפתח נכשלה.");
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Credential
        {
            public uint Flags; public uint Type; [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName; [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
            public long LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias; [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
        }
        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CredWrite(ref Credential credential, uint flags);
        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPointer);
        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("Advapi32.dll")] internal static extern void CredFree(IntPtr buffer);

    }
}
