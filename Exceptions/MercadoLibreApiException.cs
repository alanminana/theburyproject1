using System.Net;

namespace TheBuryProject.Services.Exceptions
{
    /// <summary>
    /// Error de la API de Mercado Libre. El mensaje nunca incluye tokens.
    /// </summary>
    public class MercadoLibreApiException : Exception
    {
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// Extracto del body de error devuelto por la API (recortado, sin datos sensibles).
        /// </summary>
        public string? ResponseExcerpt { get; }

        public MercadoLibreApiException(string message, HttpStatusCode? statusCode = null, string? responseExcerpt = null, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ResponseExcerpt = responseExcerpt;
        }
    }
}
