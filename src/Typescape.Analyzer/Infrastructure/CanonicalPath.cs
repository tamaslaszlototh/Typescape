using System.Runtime.InteropServices;

namespace Typescape.Analyzer.Infrastructure;

/// <summary>The one spelling the operating system reports for a path.</summary>
internal static class CanonicalPath
{
    /// <summary>
    /// The full path of <paramref name="path"/>; off Windows, with every symbolic link resolved and, on a file system
    /// that ignores case, the case on disk. Unchanged but for being made full when the path does not exist.
    /// </summary>
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

    [DllImport("libc", EntryPoint = "realpath", SetLastError = false)]
    private static extern IntPtr RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);
}
