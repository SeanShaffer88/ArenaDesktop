using System.Runtime.InteropServices;
using ArenaDesktop.Core;

namespace ArenaDesktop;

/// <summary>
/// DPAPI (current user) implementation of Core's ISecretProtector,
/// via direct crypt32 P/Invoke — no new package. Blobs are bound to
/// this Windows user on this machine, the same protection Windows
/// gives every app's secrets; they do not decrypt for another user
/// or on another PC, which the BYOK UI states plainly.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    public string Name => "dpapi";

    private static readonly byte[] Entropy = "ArenaDesktop.ApiKeys.v1"u8.ToArray();

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int CbData;
        public IntPtr PbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn, string? szDataDescr, ref DataBlob pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn, IntPtr ppszDataDescr, ref DataBlob pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const int CryptProtectUiForbidden = 0x1;

    public byte[] Protect(byte[] plain) => Run(plain, protect: true);

    public byte[] Unprotect(byte[] blob) => Run(blob, protect: false);

    private static byte[] Run(byte[] input, bool protect)
    {
        var inPtr = Marshal.AllocHGlobal(input.Length);
        var entropyPtr = Marshal.AllocHGlobal(Entropy.Length);
        try
        {
            Marshal.Copy(input, 0, inPtr, input.Length);
            Marshal.Copy(Entropy, 0, entropyPtr, Entropy.Length);
            var inBlob = new DataBlob { CbData = input.Length, PbData = inPtr };
            var entropy = new DataBlob { CbData = Entropy.Length, PbData = entropyPtr };
            var ok = protect
                ? CryptProtectData(ref inBlob, "ArenaDesktop API key", ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out outBlob);
            if (!ok) throw new InvalidOperationException($"DPAPI {(protect ? "protect" : "unprotect")} failed (error {Marshal.GetLastWin32Error()}).");
            try
            {
                var result = new byte[outBlob.CbData];
                Marshal.Copy(outBlob.PbData, result, 0, outBlob.CbData);
                return result;
            }
            finally { LocalFree(outBlob.PbData); }
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
            Marshal.FreeHGlobal(entropyPtr);
        }
    }
}
