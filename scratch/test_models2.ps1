Add-Type -TypeDefinition @"
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

public class WsTest3 {
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
    public static string GetKey(string target) {
        IntPtr ptr;
        if (CredRead(target, 1, 0, out ptr)) {
            var c = (CREDENTIAL)Marshal.PtrToStructure(ptr, typeof(CREDENTIAL));
            return Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize / 2);
        }
        return null;
    }

    public static async Task TestEndpoint(string key, string apiVersion, string model) {
        using (var ws = new ClientWebSocket())
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var uri = new Uri("wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage." + apiVersion + ".GenerativeService.BidiGenerateContent?key=" + key);
            try {
                await ws.ConnectAsync(uri, cts.Token);
                var setup = "{\"setup\":{\"model\":\"" + model + "\",\"generationConfig\":{\"responseModalities\":[\"AUDIO\"]}}}";
                var bytes = Encoding.UTF8.GetBytes(setup);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
                
                var buffer = new byte[8192];
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) {
                    Console.WriteLine("[" + apiVersion + "] " + model + " => Closed: " + ws.CloseStatusDescription);
                } else {
                    var resp = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine(">>> SUCCESS! [" + apiVersion + "] " + model + " => " + resp.Substring(0, Math.Min(resp.Length, 100)));
                }
            } catch (Exception ex) {
                Console.WriteLine("[" + apiVersion + "] " + model + " => Ex: " + ex.Message);
            }
        }
    }
}
"@

$k = [WsTest3]::GetKey("google-api-key.com.bubu.desktop")
if (-not $k) { $k = [WsTest3]::GetKey("gemini-api-key.com.sahrulrd.bubu") }

$models = @(
    "models/gemini-2.0-flash-native-audio-preview",
    "models/gemini-3.8-live",
    "models/gemini-2.5-flash-native-audio-preview",
    "models/gemini-2.0-flash-live",
    "models/gemini-2.0-flash-realtime-exp",
    "models/gemini-2.5-flash-preview-tts",
    "models/gemini-2.0-flash-exp",
    "models/gemini-2.0-flash"
)

foreach ($ep in @("v1alpha", "v1beta")) {
    foreach ($m in $models) {
        [WsTest3]::TestEndpoint($k, $ep, $m).GetAwaiter().GetResult()
    }
}
