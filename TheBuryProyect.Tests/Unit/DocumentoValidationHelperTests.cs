using Microsoft.AspNetCore.Http;
using TheBuryProject.Helpers;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para DocumentoValidationHelper.
/// Función pura — no requiere base de datos ni infraestructura.
/// Cubre ValidateFile (tamaño, extensión, magic bytes) y NormalizePath (traversal).
/// </summary>
public class DocumentoValidationHelperTests
{
    // -------------------------------------------------------------------------
    // Fake IFormFile helper
    // -------------------------------------------------------------------------

    private static IFormFile MakeFakeFile(string fileName, byte[] content, string contentType = "application/octet-stream")
    {
        var stream = new MemoryStream(content);
        return new FakeFormFileHelper(fileName, contentType, stream);
    }

    // =========================================================================
    // ValidateFile — archivo nulo/vacío
    // =========================================================================

    [Fact]
    public void ValidateFile_ArchivoNulo_EsInvalido()
    {
        var (isValid, _) = DocumentoValidationHelper.ValidateFile(null!);
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateFile_ArchivoVacio_EsInvalido()
    {
        var file = MakeFakeFile("empty.pdf", Array.Empty<byte>());
        var (isValid, _) = DocumentoValidationHelper.ValidateFile(file);
        Assert.False(isValid);
    }

    // =========================================================================
    // ValidateFile — extensión
    // =========================================================================

    [Fact]
    public void ValidateFile_ExtensionExe_EsInvalido()
    {
        var file = MakeFakeFile("malware.exe", new byte[] { 0x4D, 0x5A, 0x00, 0x00 });
        var (isValid, error) = DocumentoValidationHelper.ValidateFile(file);
        Assert.False(isValid);
        Assert.Contains("Extensi", error); // "Extensión no permitida"
    }

    [Fact]
    public void ValidateFile_ExtensionTxt_EsInvalido()
    {
        var file = MakeFakeFile("doc.txt", new byte[10]);
        var (isValid, _) = DocumentoValidationHelper.ValidateFile(file);
        Assert.False(isValid);
    }

    // =========================================================================
    // ValidateFile — magic bytes
    // =========================================================================

    [Fact]
    public void ValidateFile_PdfConMagicBytesCorrectos_EsValido()
    {
        // %PDF magic bytes
        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 };
        var file = MakeFakeFile("documento.pdf", pdfContent, "application/pdf");

        var (isValid, error) = DocumentoValidationHelper.ValidateFile(file);

        Assert.True(isValid, $"Error: {error}");
    }

    [Fact]
    public void ValidateFile_PdfConMagicBytesIncorrectos_EsInvalido()
    {
        // .pdf extension but wrong bytes
        var wrongContent = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var file = MakeFakeFile("fake.pdf", wrongContent, "application/pdf");

        var (isValid, _) = DocumentoValidationHelper.ValidateFile(file);

        Assert.False(isValid);
    }

    [Fact]
    public void ValidateFile_PngConMagicBytesCorrectos_EsValido()
    {
        // PNG signature: 89 50 4E 47
        var pngContent = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var file = MakeFakeFile("imagen.png", pngContent, "image/png");

        var (isValid, _) = DocumentoValidationHelper.ValidateFile(file);

        Assert.True(isValid);
    }

    [Fact]
    public void ValidateFile_JpgConMagicBytesCorrectos_EsValido()
    {
        // JPEG signature: FF D8 FF
        var jpgContent = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
        var file = MakeFakeFile("foto.jpg", jpgContent, "image/jpeg");

        var (isValid, _) = DocumentoValidationHelper.ValidateFile(file);

        Assert.True(isValid);
    }

    [Fact]
    public void ValidateFile_DocxConMagicBytesCorrectos_EsValido()
    {
        // DOCX is a ZIP: PK signature 50 4B
        var docxContent = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var file = MakeFakeFile("archivo.docx", docxContent, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var (isValid, _) = DocumentoValidationHelper.ValidateFile(file);

        Assert.True(isValid);
    }

    // =========================================================================
    // ValidateFile — tamaño
    // =========================================================================

    [Fact]
    public void ValidateFile_ArchivoDemasiadoGrande_EsInvalido()
    {
        // 6MB > 5MB limit — use a stream that reports large length
        var bigContent = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // valid PDF bytes
        var file = new FakeLargeFormFile("big.pdf", bigContent, 6 * 1024 * 1024 + 1);

        var (isValid, error) = DocumentoValidationHelper.ValidateFile(file);

        Assert.False(isValid);
        Assert.Contains("tama", error); // "tamaño máximo"
    }

    // =========================================================================
    // NormalizePath — happy path
    // =========================================================================

    [Fact]
    public void NormalizePath_RutaValida_EsValida()
    {
        var basePath = Path.GetTempPath();
        var (isValid, fullPath, _) = DocumentoValidationHelper.NormalizePath(basePath, "test.pdf");

        Assert.True(isValid);
        Assert.True(fullPath.StartsWith(Path.GetFullPath(basePath), StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("test.pdf", fullPath);
    }

    // =========================================================================
    // NormalizePath — path traversal
    // =========================================================================

    [Fact]
    public void NormalizePath_PathTraversal_EsInvalido()
    {
        var basePath = Path.GetTempPath();
        var (isValid, _, error) = DocumentoValidationHelper.NormalizePath(basePath, "../../../etc/passwd");

        Assert.False(isValid);
        Assert.Contains("traversal", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizePath_NombreArchivoVacio_EsInvalido()
    {
        var (isValid, _, _) = DocumentoValidationHelper.NormalizePath(Path.GetTempPath(), "");
        Assert.False(isValid);
    }

    [Fact]
    public void NormalizePath_RutaBaseVacia_EsInvalido()
    {
        var (isValid, _, _) = DocumentoValidationHelper.NormalizePath("", "file.pdf");
        Assert.False(isValid);
    }
}

// ---------------------------------------------------------------------------
// Fake IFormFile implementations
// ---------------------------------------------------------------------------
file sealed class FakeFormFileHelper : IFormFile
{
    private readonly MemoryStream _stream;
    public FakeFormFileHelper(string fileName, string contentType, MemoryStream stream)
    {
        FileName = fileName;
        ContentType = contentType;
        _stream = stream;
    }

    public string ContentType { get; }
    public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{FileName}\"";
    public IHeaderDictionary Headers => new HeaderDictionary();
    public long Length => _stream.Length;
    public string Name => "file";
    public string FileName { get; }

    public void CopyTo(Stream target) { _stream.Position = 0; _stream.CopyTo(target); }
    public async Task CopyToAsync(Stream target, CancellationToken ct = default)
    {
        _stream.Position = 0;
        await _stream.CopyToAsync(target, ct);
    }
    public Stream OpenReadStream() { _stream.Position = 0; return _stream; }
}

file sealed class FakeLargeFormFile : IFormFile
{
    private readonly byte[] _headerBytes;
    private readonly long _reportedLength;

    public FakeLargeFormFile(string fileName, byte[] headerBytes, long reportedLength)
    {
        FileName = fileName;
        _headerBytes = headerBytes;
        _reportedLength = reportedLength;
    }

    public string ContentType => "application/pdf";
    public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{FileName}\"";
    public IHeaderDictionary Headers => new HeaderDictionary();
    public long Length => _reportedLength;
    public string Name => "file";
    public string FileName { get; }

    public void CopyTo(Stream target) => target.Write(_headerBytes, 0, _headerBytes.Length);
    public async Task CopyToAsync(Stream target, CancellationToken ct = default)
        => await target.WriteAsync(_headerBytes, 0, _headerBytes.Length, ct);
    public Stream OpenReadStream() => new MemoryStream(_headerBytes);
}
