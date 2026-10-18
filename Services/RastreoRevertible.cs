using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TheBuryProject.Data;

namespace TheBuryProject.Services
{
    /// <summary>
    /// FIN-FACT-HARDENING-P0. Devuelve el change tracker de un <see cref="AppDbContext"/> al estado coherente con la base
    /// DESPUÉS de que una operación multi-paso (varios <c>SaveChanges</c>) fue deshecha en la base (rollback de la
    /// transacción propia o rollback a un savepoint). Sin esto, lo que la operación dejó rastreado (una factura nueva,
    /// una venta con estado Facturada) ya no coincide con la base y puede reaparecer en un <c>SaveChanges</c> posterior
    /// del mismo contexto (p. ej. por un grafo de navegación) o ser leído como verdadero.
    ///
    /// Qué hace, sin <c>ChangeTracker.Clear()</c> y sin descartar cambios pendientes del llamador:
    ///   1. Entidades CREADAS por la operación (rastreadas como Added, no provenientes de una consulta): se desacoplan
    ///      y se quitan de las colecciones/referencias de navegación de las entidades que siguen rastreadas.
    ///   2. Entidades que el llamador ya tenía con cambios pendientes (Modified/Deleted) al empezar: se restauran a sus
    ///      valores y estado de entonces (sus cambios siguen pendientes, tal como los dejó el llamador).
    ///   3. Resto de las entidades rastreadas (ya persistidas, que la operación pudo haber mutado y guardado): se
    ///      recargan de la base, que tras el rollback es la fuente de verdad.
    /// Las entidades que el llamador tenía en Added no se tocan.
    /// </summary>
    internal sealed class RastreoRevertible : IDisposable
    {
        private readonly AppDbContext _context;
        private readonly HashSet<object> _previas;
        private readonly Dictionary<object, Snapshot> _pendientes = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> _agregadasPrevias = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _creadas = new();

        private sealed record Snapshot(EntityState Estado, PropertyValues Actual, PropertyValues Original);

        public RastreoRevertible(AppDbContext context)
        {
            _context = context;
            _previas = new HashSet<object>(ReferenceEqualityComparer.Instance);

            foreach (var entry in context.ChangeTracker.Entries())
            {
                _previas.Add(entry.Entity);
                switch (entry.State)
                {
                    case EntityState.Modified:
                    case EntityState.Deleted:
                        _pendientes[entry.Entity] = new Snapshot(entry.State, entry.CurrentValues.Clone(), entry.OriginalValues.Clone());
                        break;
                    case EntityState.Added:
                        _agregadasPrevias.Add(entry.Entity);
                        break;
                }
            }

            context.ChangeTracker.Tracked += OnTracked;
        }

        private void OnTracked(object? sender, EntityTrackedEventArgs e)
        {
            if (!e.FromQuery && e.Entry.State == EntityState.Added && !_previas.Contains(e.Entry.Entity))
                _creadas.Add(e.Entry.Entity);
        }

        public void Dispose() => _context.ChangeTracker.Tracked -= OnTracked;

        /// <summary>Llamar SOLO después de que la base ya fue revertida (la recarga lee el estado posterior al rollback).</summary>
        public async Task RestaurarAsync()
        {
            // 1) Creadas por la operación: fuera del tracker y fuera de las navegaciones de las que siguen.
            var creadas = new HashSet<object>(_creadas, ReferenceEqualityComparer.Instance);
            foreach (var entity in _creadas)
            {
                var entry = _context.Entry(entity);
                if (entry.State != EntityState.Detached)
                    entry.State = EntityState.Detached;
            }

            if (creadas.Count > 0)
            {
                foreach (var entry in _context.ChangeTracker.Entries().ToList())
                {
                    foreach (var coleccion in entry.Collections)
                    {
                        if (coleccion.CurrentValue is not IList lista)
                            continue;
                        foreach (var item in lista.Cast<object>().Where(creadas.Contains).ToList())
                            lista.Remove(item);
                    }

                    foreach (var referencia in entry.References)
                    {
                        if (referencia.CurrentValue is { } destino && creadas.Contains(destino))
                            referencia.CurrentValue = null;
                    }
                }
            }

            // 2) y 3) Pendientes del llamador: se restauran; el resto persistido: se recarga de la base.
            foreach (var entry in _context.ChangeTracker.Entries().ToList())
            {
                if (entry.State == EntityState.Detached || _agregadasPrevias.Contains(entry.Entity))
                    continue;

                if (_pendientes.TryGetValue(entry.Entity, out var snap))
                {
                    entry.OriginalValues.SetValues(snap.Original);
                    entry.CurrentValues.SetValues(snap.Actual);
                    entry.State = snap.Estado;
                    if (snap.Estado == EntityState.Modified)
                    {
                        // Solo las propiedades que el llamador había cambiado: no se amplía el UPDATE a toda la fila.
                        foreach (var propiedad in entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()))
                            propiedad.IsModified = !Equals(propiedad.CurrentValue, propiedad.OriginalValue);
                    }
                    continue;
                }

                await entry.ReloadAsync();
            }
        }
    }
}
