using TheBuryProject.Models.Entities;

namespace TheBuryProject.Helpers
{
    public static class ClienteFormatting
    {
        public static string ToDisplayName(this Cliente cliente)
        {
            return $"{cliente.Apellido}, {cliente.Nombre} - DNI: {cliente.NumeroDocumento}";
        }

        public static string ToDisplayName(this Garante garante)
        {
            // Si el garante está vinculado a un cliente en el sistema, preferir los datos del cliente asociado.
            if (garante.GaranteCliente != null)
            {
                var c = garante.GaranteCliente;
                return $"{c.Apellido}, {c.Nombre} - DNI: {c.NumeroDocumento}";
            }

            return $"{garante.Apellido}, {garante.Nombre} - DNI: {garante.NumeroDocumento}";
        }
    }
}