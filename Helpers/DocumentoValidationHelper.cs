using Microsoft.AspNetCore.Http;

namespace TheBuryProject.Helpers
{
    public static class DocumentoValidationHelper
    {
        private static readonly HashSet<string> VALID_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx"
        };

        private const long MAX_FILE_SIZE = 5 * 1024 * 1024; // 5MB

        /// <summary>
        /// Nombre original del archivo para mostrar (columna de 200). El archivo se guarda con un nombre generado, así que
        /// un nombre muy largo se recorta conservando la extensión en lugar de fallar al guardar.
        /// </summary>
        public static string NombreParaMostrar(string? fileName, int maximo = 200)
        {
            // Ambos separadores a mano: Path.GetFileName en Linux no corta por '' (algunos clientes envían la ruta completa).
            var crudo = fileName ?? string.Empty;
            var nombre = crudo[(crudo.LastIndexOfAny(new[] { '/', '\\' }) + 1)..].Trim();
            if (nombre.Length <= maximo)
                return nombre;

            var ext = Path.GetExtension(nombre);
            if (ext.Length >= maximo)
                return nombre[..maximo];

            return nombre[..(maximo - ext.Length)] + ext;
        }

        /// <summary>Tipo MIME informado por el cliente (columna de 100): lo envía el navegador, no es confiable ni acotado.</summary>
        public static string? MimeAcotado(string? contentType, int maximo = 100)
            => string.IsNullOrWhiteSpace(contentType)
                ? null
                : (contentType.Length <= maximo ? contentType : contentType[..maximo]);

        public static (bool IsValid, string ErrorMessage) ValidateFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return (false, "Debe seleccionar un archivo v�lido.");

            if (file.Length > MAX_FILE_SIZE)
                return (false, $"El archivo excede el tama�o m�ximo permitido ({FormatFileSize(MAX_FILE_SIZE)}).");

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext) || !VALID_EXTENSIONS.Contains(ext))
                return (false, $"Extensi�n no permitida. Permitidas: {string.Join(", ", VALID_EXTENSIONS)}");

            if (!ValidateMagicBytes(file, ext))
                return (false, "El contenido del archivo no coincide con su extensi�n (magic bytes inv�lidos).");

            return (true, "");
        }

        public static (bool IsValid, string FullPath, string ErrorMessage) NormalizePath(string basePath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                return (false, string.Empty, "La ruta base no es v�lida.");

            if (string.IsNullOrWhiteSpace(fileName))
                return (false, string.Empty, "El nombre de archivo no es v�lido.");

            var normalizedBase = Path.GetFullPath(basePath);
            var combined = Path.Combine(normalizedBase, fileName);
            var normalizedFull = Path.GetFullPath(combined);

            // Evita falsos positivos tipo: C:\uploads vs C:\uploads2
            var baseWithSep = normalizedBase.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedBase
                : normalizedBase + Path.DirectorySeparatorChar;

            if (!normalizedFull.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase))
                return (false, string.Empty, "Ruta inv�lida (path traversal detectado).");

            return (true, normalizedFull, "");
        }

        private static bool ValidateMagicBytes(IFormFile file, string extension)
        {
            try
            {
                using var stream = file.OpenReadStream();
                return stream.CanRead && ContenidoCoincideConExtension(stream, extension);
            }
            catch
            {
                // Si no se puede leer el header, bloquear (más seguro)
                return false;
            }
        }

        /// <summary>
        /// Verifica que los primeros bytes del contenido correspondan a la extensión declarada (un ejecutable renombrado
        /// a .pdf no pasa). Extensiones sin firma conocida devuelven true; <c>.txt</c> se acepta solo si es texto (sin bytes nulos).
        /// </summary>
        public static bool ContenidoCoincideConExtension(Stream stream, string extension)
        {
            var buffer = new byte[4096];
            var leidos = 0;
            int n;
            while (leidos < buffer.Length && (n = stream.Read(buffer, leidos, buffer.Length - leidos)) > 0)
                leidos += n;

            bool Empieza(params byte[] firma) => leidos >= firma.Length && buffer.Take(firma.Length).SequenceEqual(firma);

            return extension.ToLowerInvariant() switch
            {
                ".pdf" => Empieza(0x25, 0x50, 0x44, 0x46), // %PDF
                ".png" => Empieza(0x89, 0x50, 0x4E, 0x47), // PNG
                ".jpg" or ".jpeg" => Empieza(0xFF, 0xD8, 0xFF),
                ".gif" => Empieza((byte)'G', (byte)'I', (byte)'F', (byte)'8'), // GIF87a / GIF89a
                ".webp" => leidos >= 12 && Empieza((byte)'R', (byte)'I', (byte)'F', (byte)'F')
                           && buffer[8] == (byte)'W' && buffer[9] == (byte)'E' && buffer[10] == (byte)'B' && buffer[11] == (byte)'P',
                ".doc" or ".xls" => Empieza(0xD0, 0xCF, 0x11, 0xE0), // OLE2
                ".docx" or ".xlsx" or ".zip" => Empieza(0x50, 0x4B), // ZIP (PK)
                ".txt" => leidos > 0 && !buffer.Take(leidos).Contains((byte)0),
                _ => true
            };
        }

        private static string FormatFileSize(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;

            if (bytes >= GB) return $"{bytes / (double)GB:0.##} GB";
            if (bytes >= MB) return $"{bytes / (double)MB:0.##} MB";
            if (bytes >= KB) return $"{bytes / (double)KB:0.##} KB";
            return $"{bytes} bytes";
        }
    }
}
