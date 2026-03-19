using System;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Contiene m�todos de validaci�n reutilizables para Cliente
    /// </summary>
    public static class ClienteValidationHelper
    {
        /// <summary>
        /// Valida que un cliente exista
        /// </summary>
        public static bool ClienteExiste(Cliente? cliente)
        {
            return cliente != null;
        }

        /// <summary>
        /// Valida que un cliente exista y retorna resultado con mensaje
        /// </summary>
        public static (bool existe, string mensaje) ValidarClienteConMensaje(Cliente? cliente)
        {
            if (cliente != null)
                return (true, string.Empty);

            return (false, "Cliente no encontrado");
        }
    }
}