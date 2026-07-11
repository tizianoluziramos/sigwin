// Sigwin FFI console test (Phase 2).
//
// Proves the C ABI + callback bridge: calls sigwin_start on the Rust core and
// prints the provisioning URL (if unlinked) and every incoming event (JSON)
// that arrives via the unmanaged callbacks — from the Rust background thread.
//
// Usage:
//   dotnet run --project console-test -- <db-path> [passphrase]
// Point <db-path> at the store the harness already linked to skip the QR:
//   dotnet run --project console-test -- C:\...\core\sigwin.db devpass

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Sigwin.Interop;

namespace SigwinConsoleTest;

internal static unsafe class Program
{
    private static void Main(string[] args)
    {
        string dbPath = args.Length > 0 ? args[0] : "sigwin.db";
        string? passphrase = args.Length > 1
            ? args[1]
            : Environment.GetEnvironmentVariable("SIGWIN_PASSPHRASE");
        const string deviceName = "Sigwin-Console";

        Console.WriteLine("Sigwin FFI console test");
        Console.WriteLine($"  core ABI version: {NativeMethods.sigwin_abi_version()}");
        Console.WriteLine($"  db={dbPath}  encrypted={passphrase is not null}");
        Console.WriteLine();

        byte* pDb = Utf8(dbPath);
        byte* pPass = passphrase is null ? null : Utf8(passphrase);
        byte* pDev = Utf8(deviceName);

        SigwinHandle* handle = NativeMethods.sigwin_start(
            pDb, pPass, pDev, &OnUrl, &OnEvent, null);

        if (handle == null)
        {
            Console.Error.WriteLine("sigwin_start returned null (invalid arguments).");
            FreeAll(pDb, pPass, pDev);
            return;
        }

        Console.WriteLine("Running — incoming events will print below. Press Enter to stop.");
        Console.ReadLine();

        NativeMethods.sigwin_stop(handle);
        FreeAll(pDb, pPass, pDev);
    }

    // Called from the Rust background thread with the provisioning URI. In a real
    // UI this is where we'd render a QR; here we just print it. Only fires if the
    // store isn't already linked.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnUrl(byte* url, void* user)
    {
        string s = Marshal.PtrToStringUTF8((IntPtr)url) ?? "";
        Console.WriteLine("\n[LINK] Not linked yet — provisioning URL (render as QR to scan):");
        Console.WriteLine(s + "\n");
    }

    // Called from the Rust background thread for each incoming event (JSON).
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnEvent(byte* json, void* user)
    {
        string s = Marshal.PtrToStringUTF8((IntPtr)json) ?? "";
        Console.WriteLine("[EVENT] " + s);
    }

    // Marshal a string to a NUL-terminated UTF-8 buffer for the C ABI.
    private static byte* Utf8(string s)
    {
        int len = Encoding.UTF8.GetByteCount(s);
        var p = (byte*)Marshal.AllocHGlobal(len + 1);
        fixed (char* c = s)
        {
            Encoding.UTF8.GetBytes(c, s.Length, p, len);
        }
        p[len] = 0;
        return p;
    }

    private static void FreeAll(byte* a, byte* b, byte* c)
    {
        if (a != null) Marshal.FreeHGlobal((IntPtr)a);
        if (b != null) Marshal.FreeHGlobal((IntPtr)b);
        if (c != null) Marshal.FreeHGlobal((IntPtr)c);
    }
}
