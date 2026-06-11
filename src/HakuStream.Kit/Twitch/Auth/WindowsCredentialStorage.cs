using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace HakuStream.Kit.Twitch.Auth;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStorage : ISecureTokenStorage
{
    private const string CredentialTarget = "HakuStream:Twitch:OAuth";
    private const int CredentialTypeGeneric = 1;
    private const int CredentialPersistLocalMachine = 2;

    public TokenData? Load()
    {
        if (!CredRead(CredentialTarget, CredentialTypeGeneric, 0, out var credentialPtr)) return null;

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlobSize == 0) return null;

            var blob = Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
            if (string.IsNullOrEmpty(blob)) return null;

            var parts = blob.Split('\n', 2);
            return parts.Length != 2 ? null : new TokenData(parts[0], parts[1]);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public void Save(string accessToken, string refreshToken)
    {
        var blob = $"{accessToken}\n{refreshToken}";
        var blobBytes = Encoding.Unicode.GetBytes(blob);

        var credential = new CREDENTIAL
        {
            Type = CredentialTypeGeneric,
            TargetName = CredentialTarget,
            CredentialBlobSize = blobBytes.Length,
            CredentialBlob = Marshal.StringToCoTaskMemUni(blob),
            Persist = CredentialPersistLocalMachine,
            UserName = "HakuStream"
        };

        try
        {
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException(
                    $"Failed to save credentials. Error: {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            if (credential.CredentialBlob != IntPtr.Zero) Marshal.FreeCoTaskMem(credential.CredentialBlob);
        }
    }

    public void Clear()
    {
        CredDelete(CredentialTarget, CredentialTypeGeneric, 0);
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
