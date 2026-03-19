using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public static (bool IsValid, string ErrorMessage) ValidateFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return (false, "Debe seleccionar un archivo válido.");

            if (file.Length > MAX_FILE_SIZE)
                return (false, $"El archivo excede el tamaño máximo permitido ({FormatFileSize(MAX_FILE_SIZE)}).");

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext) || !VALID_EXTENSIONS.Contains(ext))
                return (false, $"Extensión no permitida. Permitidas: {string.Join(", ", VALID_EXTENSIONS)}");

            if (!ValidateMagicBytes(file, ext))
                return (false, "El contenido del archivo no coincide con su extensión (magic bytes inválidos).");

            return (true, "");
        }

        public static (bool IsValid, string FullPath, string ErrorMessage) NormalizePath(string basePath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(basePath))
                return (false, string.Empty, "La ruta base no es válida.");

            if (string.IsNullOrWhiteSpace(fileName))
                return (false, string.Empty, "El nombre de archivo no es válido.");

            var normalizedBase = Path.GetFullPath(basePath);
            var combined = Path.Combine(normalizedBase, fileName);
            var normalizedFull = Path.GetFullPath(combined);

            // Evita falsos positivos tipo: C:\uploads vs C:\uploads2
            var baseWithSep = normalizedBase.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedBase
                : normalizedBase + Path.DirectorySeparatorChar;

            if (!normalizedFull.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase))
                return (false, string.Empty, "Ruta inválida (path traversal detectado).");

            return (true, normalizedFull, "");
        }

        private static bool ValidateMagicBytes(IFormFile file, string extension)
        {
            try
            {
                using var stream = file.OpenReadStream();
                if (!stream.CanRead) return false;

                var buffer = new byte[4];
                var bytesRead = stream.Read(buffer, 0, buffer.Length);

                return extension switch
                {
                    ".pdf" => bytesRead >= 4 && buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46, // %PDF
                    ".png" => bytesRead >= 4 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47, // PNG
                    ".jpg" or ".jpeg" => bytesRead >= 3 && buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF, // JPEG
                    ".doc" => bytesRead >= 4 && buffer[0] == 0xD0 && buffer[1] == 0xCF && buffer[2] == 0x11 && buffer[3] == 0xE0, // OLE
                    ".docx" => bytesRead >= 2 && buffer[0] == 0x50 && buffer[1] == 0x4B, // ZIP (PK)
                    _ => true
                };
            }
            catch
            {
                // Si no se puede leer el header, bloquear (más seguro)
                return false;
            }
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
