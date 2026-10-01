using TheBuryProject.Models.Entities;

namespace TheBuryProject.ViewModels
{
    /// <summary>
    /// Estado real del sistema de cajas, del que dependen las acciones que la UI ofrece
    /// (Ventas, Cajas y Abrir caja): nunca se ofrece "Abrir caja" si no hay nada que abrir.
    /// </summary>
    public enum EstadoCajasSistema
    {
        /// <summary>No existe ninguna caja (visible para el usuario).</summary>
        SinCajas,
        /// <summary>Hay cajas, pero ninguna se puede abrir ahora (inactivas, con turno abierto o fuera del padrón).</summary>
        SinDisponibles,
        /// <summary>Hay al menos una caja abrible y el usuario no tiene turno abierto.</summary>
        DisponibleSinTurno,
        /// <summary>El usuario ya tiene un turno abierto.</summary>
        ConTurno
    }

    public sealed record CajaDisponibilidad(
        EstadoCajasSistema Estado,
        int TotalCajas,
        int CajasActivas,
        int CajasConTurnoAbierto,
        int CajasDisponibles,
        bool UsuarioTieneTurno)
    {
        /// <summary>
        /// Criterio único de "caja disponible": activa, sin turno abierto y, si el usuario no es
        /// supervisor, dentro de su padrón (<paramref name="padronCajaIds"/> null = sin restricción).
        /// </summary>
        public static IReadOnlyList<Caja> Disponibles(
            IEnumerable<Caja> cajas,
            IEnumerable<AperturaCaja> aperturasAbiertas,
            IReadOnlyCollection<int>? padronCajaIds)
        {
            var conTurno = aperturasAbiertas.Select(a => a.CajaId).ToHashSet();
            var disponibles = cajas.Where(c => c.Activa && !conTurno.Contains(c.Id));
            if (padronCajaIds != null)
            {
                disponibles = disponibles.Where(c => padronCajaIds.Contains(c.Id));
            }
            return disponibles.ToList();
        }

        public static CajaDisponibilidad Resolver(
            IReadOnlyCollection<Caja> cajas,
            IReadOnlyCollection<AperturaCaja> aperturasAbiertas,
            IReadOnlyCollection<int>? padronCajaIds,
            bool usuarioTieneTurno)
        {
            var visibles = padronCajaIds == null
                ? cajas
                : cajas.Where(c => padronCajaIds.Contains(c.Id)).ToList();
            var disponibles = Disponibles(cajas, aperturasAbiertas, padronCajaIds);
            var conTurno = aperturasAbiertas.Select(a => a.CajaId).ToHashSet();

            var estado = usuarioTieneTurno ? EstadoCajasSistema.ConTurno
                : visibles.Count == 0 ? EstadoCajasSistema.SinCajas
                : disponibles.Count == 0 ? EstadoCajasSistema.SinDisponibles
                : EstadoCajasSistema.DisponibleSinTurno;

            return new CajaDisponibilidad(
                estado,
                visibles.Count,
                visibles.Count(c => c.Activa),
                visibles.Count(c => conTurno.Contains(c.Id)),
                disponibles.Count,
                usuarioTieneTurno);
        }
    }
}
