using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

        public static IList<CloudCredential> ReadAll(CloudProvider provider)
        {
            List<CloudCredential> result = new List<CloudCredential>();
            IntPtr pointers; int count;
            if (!CredEnumerate(Target(provider) + "*", 0, out count, out pointers))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 1168) return result;
                throw new InvalidOperationException("无法读取 Windows 凭据，错误码：" + error);
            }
            try
            {
                Dictionary<string, CloudCredential> accounts = new Dictionary<string, CloudCredential>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    NativeCredential native = (NativeCredential)Marshal.PtrToStructure(Marshal.ReadIntPtr(pointers, i * IntPtr.Size), typeof(NativeCredential));
                    if (native.Type != Generic) continue;
                    string text = native.CredentialBlobSize == 0 ? "" : Marshal.PtrToStringUni(native.CredentialBlob, native.CredentialBlobSize / 2);
                    int split = text.IndexOf('\n'); if (split <= 0) continue;
                    CloudCredential value = new CloudCredential { Provider = provider, Id = text.Substring(0, split), Secret = text.Substring(split + 1) };
                    string target = Marshal.PtrToStringUni(native.TargetName);
                    if (target == Target(value)) accounts[value.Id] = value;
                    else if (target == Target(provider) && !accounts.ContainsKey(value.Id)) accounts.Add(value.Id, value);
                }
                result.AddRange(accounts.Values);
                result.Sort(delegate(CloudCredential a, CloudCredential b) { return String.Compare(a.Id, b.Id, StringComparison.Ordinal); });
                return result;
            }
            finally { CredFree(pointers); }
        }

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
            IntPtr target = Marshal.StringToCoTaskMemUni(Target(value));
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

        public static void Delete(CloudCredential value)
        {
            DeleteTarget(Target(value));
            CloudCredential legacy = Read(value.Provider);
            if (legacy != null && legacy.Id == value.Id) DeleteTarget(Target(value.Provider));
        }
        private static void DeleteTarget(string target)
        {
            if (!CredDelete(target, Generic, 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1168) throw new InvalidOperationException("无法删除 Windows 凭据，错误码：" + error);
            }
        }
        public static string Mask(string id) { if (String.IsNullOrEmpty(id) || id.Length < 5) return "***"; return id.Substring(0, 3) + "***" + id.Substring(id.Length - 2); }
        private static string Target(CloudProvider p) { return Prefix + p.ToString().ToLowerInvariant(); }
        private static string Target(CloudCredential value)
        {
            using (SHA256 hash = SHA256.Create())
                return Target(value.Provider) + "-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value.Id))).Replace("-", "");
        }
    }
}
