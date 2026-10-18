using System.Runtime.InteropServices;

namespace DesktopAssistant.Settings;

/// <summary>API keys live in Windows Credential Manager (per user), never in the settings file.</summary>
public static class SecretStore
{
    private const uint Generic = 1, LocalMachinePersistence = 2;

    // "DesktopAssistant/Gemini" is the target the previous version used, so an existing key keeps working.
    private static string Target(string providerId) => providerId == "gemini" ? "DesktopAssistant/Gemini" : $"DesktopAssistant/{providerId}";

    public static void Save(string providerId, string apiKey)
    {
        apiKey = apiKey.Trim();
        if (apiKey.Length == 0) { Delete(providerId); return; }
        var bytes = Encoding.Unicode.GetBytes(apiKey);
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var credential = new Credential { Type = Generic, TargetName = Target(providerId), CredentialBlobSize = (uint)bytes.Length, CredentialBlob = memory, Persist = LocalMachinePersistence, UserName = "API key" };
            if (!CredWrite(ref credential, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "שמירת המפתח במנהל האישורים של Windows נכשלה.");
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, memory, bytes.Length);
            Marshal.FreeHGlobal(memory);
            Array.Clear(bytes);
        }
    }

    public static string? Get(string providerId)
    {
        try
        {
            if (!CredRead(Target(providerId), Generic, 0, out var pointer)) return null;
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                if (credential.CredentialBlobSize == 0) return null;
                var bytes = new byte[checked((int)credential.CredentialBlobSize)];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                var value = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                Array.Clear(bytes);
                return value.Length > 0 ? value : null;
            }
            finally { CredFree(pointer); }
        }
        catch { return null; }
    }

    public static bool Has(string providerId) => Get(providerId) is not null;

    public static void Delete(string providerId)
    {
        if (!CredDelete(Target(providerId), Generic, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) throw new System.ComponentModel.Win32Exception(error, "מחיקת המפתח נכשלה.");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags; public uint Type; [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName; [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten; public uint CredentialBlobSize; public nint CredentialBlob; public uint Persist; public uint AttributeCount; public nint Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias; [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out nint credentialPointer);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint buffer);
}
