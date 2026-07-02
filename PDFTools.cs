using System.Runtime.InteropServices;

namespace Tideway.PdfTools;

/// <summary>
/// In-memory PDF operations backed by the native qpdf library
/// (https://qpdf.readthedocs.io/): page merging and password encryption.
/// Requires libqpdf at runtime — see the package README for per-platform setup.
/// </summary>
public static class PdfTools
{
    // qpdf error codes are a bitmask: bit 0 = warnings, bit 1 = errors.
    // Warnings (recoverable issues in slightly-off PDFs) are tolerated.
    private const int QpdfErrors = 2;

    /// <summary>
    /// Appends every page of <paramref name="second"/> after the pages of
    /// <paramref name="first"/> and returns the merged PDF.
    /// </summary>
    /// <exception cref="QpdfException">Either input can't be parsed (corrupt or password-protected).</exception>
    public static byte[] Merge(byte[] first, byte[] second)
    {
        var target = Qpdf.qpdf_init();
        var source = Qpdf.qpdf_init();
        try
        {
            Check(target, Qpdf.qpdf_read_memory(target, "first", first, (ulong)first.Length, IntPtr.Zero), "read first PDF");
            Check(source, Qpdf.qpdf_read_memory(source, "second", second, (ulong)second.Length, IntPtr.Zero), "read second PDF");
            Check(target, Qpdf.qpdf_init_write_memory(target), "init write");

            var pageCount = Qpdf.qpdf_get_num_pages(source);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var page = Qpdf.qpdf_get_page_n(source, pageIndex);
                Check(target, Qpdf.qpdf_add_page(target, source, page, 0), $"append page {pageIndex + 1}/{pageCount}");
            }

            Check(target, Qpdf.qpdf_write(target), "write merged PDF");
            return CopyOutputBuffer(target);
        }
        finally
        {
            Qpdf.qpdf_cleanup(ref target);
            Qpdf.qpdf_cleanup(ref source);
        }
    }

    /// <summary>
    /// Merges any number of PDFs into one, in order. Convenience fold over
    /// <see cref="Merge(byte[], byte[])"/>.
    /// </summary>
    public static byte[] Merge(IEnumerable<byte[]> documents)
    {
        byte[]? merged = null;
        foreach (var document in documents)
            merged = merged == null ? document : Merge(merged, document);
        return merged ?? throw new ArgumentException("No documents to merge.", nameof(documents));
    }

    /// <summary>
    /// Password-protects a PDF with AES-256 (R6) encryption. The same password is
    /// used for user and owner; all permissions including full-quality printing
    /// are allowed. (The original wrapper's RC4/R4 encryption was removed from
    /// qpdf 12, so R6 is used across all supported qpdf versions.)
    /// </summary>
    /// <exception cref="QpdfException">The input can't be parsed.</exception>
    public static byte[] Encrypt(byte[] content, string password)
    {
        var qpdfData = Qpdf.qpdf_init();
        try
        {
            Check(qpdfData, Qpdf.qpdf_read_memory(qpdfData, "input", content, (ulong)content.Length, IntPtr.Zero), "read PDF");
            Check(qpdfData, Qpdf.qpdf_init_write_memory(qpdfData), "init write");

            Check(qpdfData, Qpdf.qpdf_set_r6_encryption_parameters2(qpdfData,
                password,   // user_password
                password,   // owner_password
                1,          // allow_accessibility
                1,          // allow_extract
                1,          // allow_assemble
                1,          // allow_annotate_and_form
                1,          // allow_form_filling
                1,          // allow_modify_other
                0,          // print: qpdf_r3p_full — printing allowed
                1),         // encrypt_metadata
                "set encryption parameters");

            Check(qpdfData, Qpdf.qpdf_write(qpdfData), "write encrypted PDF");
            return CopyOutputBuffer(qpdfData);
        }
        finally
        {
            Qpdf.qpdf_cleanup(ref qpdfData);
        }
    }

    /// <summary>
    /// Removes password protection: reads the PDF with its password and writes it
    /// back unencrypted. Also accepts unencrypted input (returned re-written, not
    /// byte-identical).
    /// </summary>
    /// <exception cref="QpdfException">Wrong password or unparseable input.</exception>
    public static byte[] Decrypt(byte[] content, string password)
    {
        var qpdfData = Qpdf.qpdf_init();
        try
        {
            Check(qpdfData, Qpdf.qpdf_read_memory_with_password(qpdfData, "input", content, (ulong)content.Length, password), "read encrypted PDF");
            Check(qpdfData, Qpdf.qpdf_init_write_memory(qpdfData), "init write");
            Qpdf.qpdf_set_preserve_encryption(qpdfData, 0);
            Check(qpdfData, Qpdf.qpdf_write(qpdfData), "write decrypted PDF");
            return CopyOutputBuffer(qpdfData);
        }
        finally
        {
            Qpdf.qpdf_cleanup(ref qpdfData);
        }
    }

    /// <summary>
    /// Whether the PDF is encrypted — including password-protected files whose
    /// content can't be read without the password, and permission-restricted
    /// files that open without one.
    /// </summary>
    /// <exception cref="QpdfException">The input can't be parsed for a reason other than encryption.</exception>
    public static bool IsEncrypted(byte[] content)
    {
        var qpdfData = Qpdf.qpdf_init();
        try
        {
            var errorCode = Qpdf.qpdf_read_memory(qpdfData, "input", content, (ulong)content.Length, IntPtr.Zero);
            if ((errorCode & QpdfErrors) == 0)
                return Qpdf.qpdf_is_encrypted(qpdfData) != 0;

            // Read failed — a password error still answers the question.
            var message = ErrorText(qpdfData) ?? "";
            if (message.Contains("password", StringComparison.OrdinalIgnoreCase))
                return true;
            throw new QpdfException($"read PDF failed: {message}");
        }
        finally
        {
            Qpdf.qpdf_cleanup(ref qpdfData);
        }
    }

    /// <summary>
    /// Rewrites the PDF linearized ("fast web view"), so browser viewers can
    /// render the first page before the whole file has downloaded.
    /// </summary>
    /// <exception cref="QpdfException">The input can't be parsed.</exception>
    public static byte[] Linearize(byte[] content)
    {
        var qpdfData = Qpdf.qpdf_init();
        try
        {
            Check(qpdfData, Qpdf.qpdf_read_memory(qpdfData, "input", content, (ulong)content.Length, IntPtr.Zero), "read PDF");
            Check(qpdfData, Qpdf.qpdf_init_write_memory(qpdfData), "init write");
            Qpdf.qpdf_set_linearization(qpdfData, 1);
            Check(qpdfData, Qpdf.qpdf_write(qpdfData), "write linearized PDF");
            return CopyOutputBuffer(qpdfData);
        }
        finally
        {
            Qpdf.qpdf_cleanup(ref qpdfData);
        }
    }

    /// <summary>
    /// Returns a new PDF containing pages <paramref name="firstPage"/> through
    /// <paramref name="lastPage"/> (1-based, inclusive) of the input.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The page range is outside the document.</exception>
    /// <exception cref="QpdfException">The input can't be parsed.</exception>
    public static byte[] ExtractPages(byte[] content, int firstPage, int lastPage)
    {
        var source = Qpdf.qpdf_init();
        var target = Qpdf.qpdf_init();
        try
        {
            Check(source, Qpdf.qpdf_read_memory(source, "input", content, (ulong)content.Length, IntPtr.Zero), "read PDF");

            var pageCount = Qpdf.qpdf_get_num_pages(source);
            if (firstPage < 1 || lastPage > pageCount || firstPage > lastPage)
                throw new ArgumentOutOfRangeException(nameof(firstPage),
                    $"Page range {firstPage}-{lastPage} is invalid for a {pageCount}-page document.");

            Check(target, Qpdf.qpdf_empty_pdf(target), "create empty PDF");
            Check(target, Qpdf.qpdf_init_write_memory(target), "init write");
            for (var pageIndex = firstPage - 1; pageIndex < lastPage; pageIndex++)
            {
                var page = Qpdf.qpdf_get_page_n(source, pageIndex);
                Check(target, Qpdf.qpdf_add_page(target, source, page, 0), $"copy page {pageIndex + 1}");
            }

            Check(target, Qpdf.qpdf_write(target), "write extracted pages");
            return CopyOutputBuffer(target);
        }
        finally
        {
            Qpdf.qpdf_cleanup(ref target);
            Qpdf.qpdf_cleanup(ref source);
        }
    }

    /// <summary>Splits a PDF into one single-page PDF per page, in order.</summary>
    /// <exception cref="QpdfException">The input can't be parsed.</exception>
    public static List<byte[]> SplitPages(byte[] content)
    {
        var source = Qpdf.qpdf_init();
        try
        {
            Check(source, Qpdf.qpdf_read_memory(source, "input", content, (ulong)content.Length, IntPtr.Zero), "read PDF");

            var pageCount = Qpdf.qpdf_get_num_pages(source);
            var pages = new List<byte[]>(pageCount);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var target = Qpdf.qpdf_init();
                try
                {
                    Check(target, Qpdf.qpdf_empty_pdf(target), "create empty PDF");
                    Check(target, Qpdf.qpdf_init_write_memory(target), "init write");
                    var page = Qpdf.qpdf_get_page_n(source, pageIndex);
                    Check(target, Qpdf.qpdf_add_page(target, source, page, 0), $"copy page {pageIndex + 1}");
                    Check(target, Qpdf.qpdf_write(target), $"write page {pageIndex + 1}");
                    pages.Add(CopyOutputBuffer(target));
                }
                finally
                {
                    Qpdf.qpdf_cleanup(ref target);
                }
            }
            return pages;
        }
        finally
        {
            Qpdf.qpdf_cleanup(ref source);
        }
    }

    /// <summary>Number of pages in a PDF.</summary>
    /// <exception cref="QpdfException">The input can't be parsed.</exception>
    public static int PageCount(byte[] content)
    {
        var qpdfData = Qpdf.qpdf_init();
        try
        {
            Check(qpdfData, Qpdf.qpdf_read_memory(qpdfData, "input", content, (ulong)content.Length, IntPtr.Zero), "read PDF");
            return Qpdf.qpdf_get_num_pages(qpdfData);
        }
        finally
        {
            Qpdf.qpdf_cleanup(ref qpdfData);
        }
    }

    private static byte[] CopyOutputBuffer(IntPtr qpdfData)
    {
        var length = Qpdf.qpdf_get_buffer_length(qpdfData);
        var buffer = Qpdf.qpdf_get_buffer(qpdfData);
        var result = new byte[length];
        Marshal.Copy(buffer, result, 0, length);
        return result;
    }

    private static void Check(IntPtr qpdfData, int errorCode, string operation)
    {
        if ((errorCode & QpdfErrors) == 0)
            return;

        var fullText = ErrorText(qpdfData);
        throw new QpdfException(fullText == null ? $"{operation} failed" : $"{operation} failed: {fullText}");
    }

    private static string? ErrorText(IntPtr qpdfData)
    {
        if (Qpdf.qpdf_has_error(qpdfData) == 0)
            return null;
        var error = Qpdf.qpdf_get_error(qpdfData);
        return Marshal.PtrToStringUTF8(Qpdf.qpdf_get_error_full_text(qpdfData, error));
    }
}

/// <summary>Thrown when a qpdf operation reports an error (corrupt input, encrypted input, write failure).</summary>
public class QpdfException : Exception
{
    /// <summary>Creates the exception with qpdf's full error text.</summary>
    public QpdfException(string message) : base(message) { }
}
