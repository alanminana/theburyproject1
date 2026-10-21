namespace TheBuryProject.Services.Exceptions;

/// <summary>
/// El usuario actual intentó crear, aumentar o modificar un descuento comercial sin el permiso
/// independiente <c>ventas.discount</c>. Hereda de <see cref="InvalidOperationException"/> para que
/// los controllers existentes lo traten como regla de negocio (mensaje ya redactado para el
/// operador), pero permite distinguirlo para registrarlo como evento de seguridad.
/// </summary>
public sealed class DescuentoNoAutorizadoException : InvalidOperationException
{
    public DescuentoNoAutorizadoException(string message)
        : base(message)
    {
    }
}
