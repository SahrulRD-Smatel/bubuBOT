Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class CredHelper3 {
    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL {
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
    public static string Get(string target) {
        IntPtr ptr;
        if (CredRead(target, 1, 0, out ptr)) {
            var c = (CREDENTIAL)Marshal.PtrToStructure(ptr, typeof(CREDENTIAL));
            return Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize / 2);
        }
        return null;
    }
}
"@

$k = [CredHelper3]::Get("google-api-key.com.bubu.desktop")
if (-not $k) { $k = [CredHelper3]::Get("gemini-api-key.com.sahrulrd.bubu") }

$res = Invoke-RestMethod -Uri "https://generativelanguage.googleapis.com/v1alpha/models?key=$k"
foreach ($m in $res.models) {
    $methods = $m.supportedGenerationMethods -join ", "
    Write-Host "$($m.name) => $methods"
}
