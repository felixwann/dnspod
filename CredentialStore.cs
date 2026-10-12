using System;
using System.Runtime.InteropServices;
using System.Text;

namespace dnspod
{
    internal static class CredentialStore
    {
        private const int Generic = 1;
        private const string Prefix = "dnspod-dns-";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public int Flags; public int Type; public IntPtr TargetName; public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize; public IntPtr CredentialBlob; public int Persist;
            public int AttributeCount; public IntPtr Attributes; public IntPtr TargetAlias; public IntPtr UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reserved, out IntPtr credential);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern void CredFree(IntPtr credential);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref NativeCredential credential, int flags);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        public static CloudCredential Read(CloudProvider provider)
        {
            IntPtr ptr; if (!CredRead(Target(provider), Generic, 0, out ptr)) return null;
            try
            {
                NativeCredential c = (NativeCredential)Marshal.PtrToStructure(ptr, typeof(NativeCredential));
                string text = c.CredentialBlobSize == 0 ? "" : Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize / 2);
                int split = text.IndexOf('\n'); if (split <= 0) return null;
                return new CloudCredential { Provider = provider, Id = text.Substring(0, split), Secret = text.Substring(split + 1) };
            }
            finally { CredFree(ptr); }
        }

        public static void Write(CloudCredential value)
        {
            byte[] blob = Encoding.Unicode.GetBytes(value.Id + "\n" + value.Secret);
            IntPtr target = Marshal.StringToCoTaskMemUni(Target(value.Provider));
            IntPtr user = Marshal.StringToCoTaskMemUni(value.Id);
            IntPtr data = Marshal.AllocCoTaskMem(blob.Length); Marshal.Copy(blob, 0, data, blob.Length);
            try
            {
                NativeCredential c = new NativeCredential { Type = Generic, TargetName = target, UserName = user,
                    CredentialBlob = data, CredentialBlobSize = blob.Length, Persist = 2 };
                if (!CredWrite(ref c, 0)) throw new InvalidOperationException("无法写入 Windows 凭据，错误码：" + Marshal.GetLastWin32Error());
            }
            finally { Marshal.FreeCoTaskMem(target); Marshal.FreeCoTaskMem(user); Marshal.FreeCoTaskMem(data); }
        }

        public static void Delete(CloudProvider provider) { CredDelete(Target(provider), Generic, 0); }
        public static string Mask(string id) { if (String.IsNullOrEmpty(id) || id.Length < 5) return "***"; return id.Substring(0, 3) + "***" + id.Substring(id.Length - 2); }
        private static string Target(CloudProvider p) { return Prefix + p.ToString().ToLowerInvariant(); }
    }
}
