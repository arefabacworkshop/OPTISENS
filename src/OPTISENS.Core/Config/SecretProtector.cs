using System.Runtime.InteropServices;
using System.Text;

namespace OPTISENS.Core.Config;

/// <summary>
/// Encrypts/decrypts sensitive strings (e.g., API keys) using Windows DPAPI
/// (<c>CryptProtectData</c> / <c>CryptUnprotectData</c>) without external dependencies.
/// </summary>
public static partial class SecretProtector
{
    private const string Prefix = "dpapi:";
    private const uint CryptProtectUiForbidden = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public uint CbData;
        public IntPtr PbData;
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        ref DataBlob pDataIn,
        IntPtr szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr hMem);

    public static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        if (!OperatingSystem.IsWindows()) return plain;
        if (plain.StartsWith(Prefix, StringComparison.Ordinal)) return plain;

        byte[] input = Encoding.UTF8.GetBytes(plain);
        unsafe
        {
            fixed (byte* pIn = input)
            {
                var inBlob = new DataBlob { CbData = (uint)input.Length, PbData = (IntPtr)pIn };
                if (!CryptProtectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outBlob))
                    return plain;
                try
                {
                    byte[] cipher = new byte[(int)outBlob.CbData];
                    Marshal.Copy(outBlob.PbData, cipher, 0, cipher.Length);
                    return Prefix + Convert.ToBase64String(cipher);
                }
                finally
                {
                    if (outBlob.PbData != IntPtr.Zero)
                        LocalFree(outBlob.PbData);
                }
            }
        }
    }

    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            byte[] cipher = Convert.FromBase64String(stored[Prefix.Length..]);
            unsafe
            {
                fixed (byte* pIn = cipher)
                {
                    var inBlob = new DataBlob { CbData = (uint)cipher.Length, PbData = (IntPtr)pIn };
                    if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outBlob))
                        return null;
                    try
                    {
                        byte[] plain = new byte[(int)outBlob.CbData];
                        Marshal.Copy(outBlob.PbData, plain, 0, plain.Length);
                        return Encoding.UTF8.GetString(plain);
                    }
                    finally
                    {
                        if (outBlob.PbData != IntPtr.Zero)
                            LocalFree(outBlob.PbData);
                    }
                }
            }
        }
        catch
        {
            return null;
        }
    }
}
