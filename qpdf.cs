using System.Reflection;
using System.Runtime.InteropServices;

namespace Tideway.PdfTools;

/// <summary>
/// Raw P/Invoke surface of the qpdf C API (https://qpdf.readthedocs.io/), for
/// callers that need qpdf functionality beyond what <see cref="PdfTools"/> wraps.
/// </summary>
public static class Qpdf
{
    private const string LibraryName = "qpdf";

    /// <summary>
    /// Environment variable that, when set, must contain the full path of the
    /// qpdf shared library to load — it overrides all default probing.
    /// </summary>
    public const string LibraryPathEnvironmentVariable = "QPDF_LIBRARY_PATH";

    static Qpdf()
    {
        NativeLibrary.SetDllImportResolver(typeof(Qpdf).Assembly, ResolveLibrary);
    }

    /// <summary>
    /// Loads the right qpdf shared library for the current platform. NuGet's
    /// runtimes/{rid}/native assets are probed first (the default TryLoad
    /// behaviour), then system-installed copies under their usual versioned
    /// sonames and locations (apt's libqpdf.so.29+, brew's /opt/homebrew/lib,
    /// qpdf's Windows release qpdf30.dll).
    /// </summary>
    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LibraryName)
            return IntPtr.Zero;

        var overridePath = Environment.GetEnvironmentVariable(LibraryPathEnvironmentVariable);
        if (!string.IsNullOrEmpty(overridePath))
            return NativeLibrary.Load(overridePath);

        foreach (var candidate in CandidateNames())
        {
            if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                return handle;
            if (NativeLibrary.TryLoad(candidate, out handle))
                return handle;
        }

        return IntPtr.Zero;   // fall through to the runtime's default probing (and its error)
    }

    private static IEnumerable<string> CandidateNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return "qpdf.dll";
            for (var soname = 30; soname >= 28; soname--)
                yield return $"qpdf{soname}.dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "libqpdf.dylib";
            for (var soname = 30; soname >= 28; soname--)
                yield return $"libqpdf.{soname}.dylib";
            // Homebrew/MacPorts locations are not on dlopen's default search path.
            yield return "/opt/homebrew/lib/libqpdf.dylib";
            yield return "/usr/local/lib/libqpdf.dylib";
            yield return "/opt/local/lib/libqpdf.dylib";
        }
        else
        {
            // Plain .so only exists with the -dev package; distro runtime
            // packages ship versioned sonames.
            yield return "libqpdf.so";
            for (var soname = 30; soname >= 28; soname--)
                yield return $"libqpdf.so.{soname}";
        }
    }

#pragma warning disable CS1591 // signatures mirror the documented qpdf C API
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr qpdf_init();

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qpdf_cleanup(ref IntPtr qpdfData);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_read(IntPtr qpdfdata, [MarshalAs(UnmanagedType.LPStr)] string fileName, IntPtr password);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_read_memory(IntPtr qpdfdata, [MarshalAs(UnmanagedType.LPStr)] string description,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] buffer,
        ulong size, IntPtr password);

    [DllImport(LibraryName, EntryPoint = "qpdf_read_memory", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_read_memory_with_password(IntPtr qpdfdata, [MarshalAs(UnmanagedType.LPStr)] string description,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] buffer,
        ulong size, [MarshalAs(UnmanagedType.LPStr)] string password);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_empty_pdf(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_is_encrypted(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qpdf_set_linearization(IntPtr qpdf, int value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qpdf_set_preserve_encryption(IntPtr qpdf, int value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_remove_page(IntPtr qpdf, int page);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_has_error(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr qpdf_get_error(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr qpdf_get_error_full_text(IntPtr qpdf, IntPtr error);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qpdf_set_object_stream_mode(IntPtr qpdf, int mode);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern void qpdf_set_qdf_mode(IntPtr qpdf, int value);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_init_write(IntPtr qpdf, [MarshalAs(UnmanagedType.LPStr)] string fileName);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_init_write_memory(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_get_buffer_length(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr qpdf_get_buffer(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_write(IntPtr qpdf);

    // R2/R3/R4 encryption writers were renamed *_insecure in qpdf 12 and are not
    // declared here — Encrypt uses R6 (AES-256), present since qpdf 8.4.
    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_set_r6_encryption_parameters2(IntPtr qpdf,
        [MarshalAs(UnmanagedType.LPStr)] string user_password,
        [MarshalAs(UnmanagedType.LPStr)] string owner_password,
        int allow_accessibility,
        int allow_extract,
        int allow_assemble,
        int allow_annotate_and_form,
        int allow_form_filling,
        int allow_modify_other,
        int print,
        int encrypt_metadata);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_add_page(
        IntPtr qpdf,
        IntPtr newpage_qpdf,
        int newpage,   // qpdf_oh
        int first);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_get_root(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_get_num_pages(IntPtr qpdf);

    [DllImport(LibraryName, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern int qpdf_get_page_n(IntPtr qpdf, int zero_based_index);
#pragma warning restore CS1591
}
