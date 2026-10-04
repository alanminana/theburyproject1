namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Documentos que el motor emitió durante la petición actual. Lo consume <c>DocumentosEmitidosFilter</c> para abrir el PDF
    /// en el navegador después de cualquier acción (cobro, confirmación, entrega, reintento…) sin que cada flujo lo repita.
    /// </summary>
    public interface IDocumentosEmitidosTracker
    {
        IReadOnlyList<int> Ids { get; }
        void Registrar(IEnumerable<int> ids);
        void Limpiar();
    }

    public sealed class DocumentosEmitidosTracker : IDocumentosEmitidosTracker
    {
        private readonly List<int> _ids = new();

        public IReadOnlyList<int> Ids => _ids;

        public void Registrar(IEnumerable<int> ids)
        {
            foreach (var id in ids)
                if (id > 0 && !_ids.Contains(id))
                    _ids.Add(id);
        }

        public void Limpiar() => _ids.Clear();
    }
}
