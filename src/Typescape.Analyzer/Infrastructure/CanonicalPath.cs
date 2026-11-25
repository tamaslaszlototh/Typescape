using System.Runtime.InteropServices;

namespace Typescape.Analyzer.Infrastructure;

/// <summary>The one spelling the operating system reports for a path.</summary>
internal static class CanonicalPath
{
    /// <summary>The full path; off Windows, also with symlinks resolved and the case on disk, when it exists.</summary>
    public static string Of(string path)
    {
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            return full;
        }

        var resolved = RealPath(full, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
        {
            return full;
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved) ?? full;
        }
        finally
        {
            // Off Windows, FreeHGlobal is free(), which releases realpath's malloc'd buffer.
            Marshal.FreeHGlobal(resolved);
        }
    }

    [DllImport("libc", EntryPoint = "realpath")]
    private static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);
}
