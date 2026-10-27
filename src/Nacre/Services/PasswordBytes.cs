using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace Nacre.Services;

public static class PasswordBytes
{
    public static unsafe byte[] FromSecureString(SecureString password)
    {
        var pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            var chars = new ReadOnlySpan<char>((void*)pointer, password.Length);
            var bytes = new byte[Encoding.UTF8.GetByteCount(chars)];
            Encoding.UTF8.GetBytes(chars, bytes); return bytes;
        }
        finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
    }
}
