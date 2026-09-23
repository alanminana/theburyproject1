using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.E2ESeeding
{
    /// <summary>
    /// Siembra clientes y productos "genéricos" que la mayoría del suite E2E necesita para que el
    /// autocomplete de <c>#input-buscar-cliente</c>/<c>#input-buscar-producto</c> (helpers.js:
    /// <c>searchAndSelectClient</c>/<c>addProduct</c>/<c>addTwoDistinctProducts</c>) encuentre algo
    /// al buscar por subcadenas cortas ('an','el','or','is','ar','ro','al').
    ///
    /// A diferencia de <see cref="ClienteAptitudPunitorioE2ESeeder"/> (escenarios de crédito con
    /// IDs específicos), esto es solo "hay al menos un cliente/producto buscable" — sin lo cual la
    /// mayoría de los specs de Cotización/Venta hacen <c>test.skip</c> por falta de datos, no por
    /// ningún bug real de la app, contra una base CI recién creada.
    ///
    /// Idempotente: no duplica si ya existe un cliente/producto con el mismo <c>Codigo</c>/documento
    /// marcado con <see cref="Marcador"/>. Nunca debe apuntarse a la base de desarrollo — ver
    /// <see cref="GenericE2ESeedRunner"/>.
    /// </summary>
    public static class GenericE2ESeeder
    {
        public const string Marcador = "E2EGENERIC";

        private static readonly (string Nombre, string Apellido, string Documento)[] Clientes =
        {
            ("Ana", "Martinez", "90000001"),
            ("Isabel", "Roldan", "90000002"),
            ("Carlos", "Orozco", "90000003"),
            ("Valeria", "Aliaga", "90000004"),
        };

        private static readonly (string Codigo, string Nombre)[] Productos =
        {
            ("E2EGENERIC-01", "Ventana Aluminio 80x100"),
            ("E2EGENERIC-02", "Motor Electrico 1/2 HP"),
            ("E2EGENERIC-03", "Aros de Piston Set"),
            ("E2EGENERIC-04", "Cortina Enrollable Roller"),
            ("E2EGENERIC-05", "Calefactor Electrico"),
        };

        public static async Task SembrarAsync(AppDbContext db)
        {
            var ahora = DateTime.UtcNow;

            foreach (var (nombre, apellido, documento) in Clientes)
            {
                var existe = await db.Clientes.AnyAsync(c => c.NumeroDocumento == documento);
                if (existe)
                    continue;

                db.Clientes.Add(new Cliente
                {
                    Nombre = nombre,
                    Apellido = apellido,
                    TipoDocumento = "DNI",
                    NumeroDocumento = documento,
                    CuilCuit = $"20{documento}5",
                    Telefono = "1122334455",
                    Domicilio = "Calle Falsa 123",
                    Localidad = "CABA",
                    Provincia = "Buenos Aires",
                    Email = $"{Marcador.ToLowerInvariant()}.{documento}@example.invalid",
                    Activo = true,
                    PuntajeCliente = 3,
                    // BCRA "OK": evita que estos clientes genéricos queden NoApto por falta de
                    // consulta BCRA (ver ClienteAptitudService.ConstruirBcraDetalle) y contaminen
                    // specs que no están probando ese flujo.
                    SituacionCrediticiaBcra = 1,
                    SituacionCrediticiaConsultaOk = true,
                    SituacionCrediticiaUltimaConsultaUtc = ahora.AddDays(-1),
                    SituacionCrediticiaBcraUltimoExito = 1,
                    SituacionCrediticiaUltimoExitoUtc = ahora.AddDays(-1),
                    CreatedAt = ahora.AddDays(-30)
                });
            }
            await db.SaveChangesAsync();

            var categoria = await db.Categorias.FirstOrDefaultAsync(c => c.Codigo == Marcador)
                ?? new Categoria { Codigo = Marcador, Nombre = "Seed E2E genérico", Activo = true };
            if (categoria.Id == 0)
                db.Categorias.Add(categoria);

            var marca = await db.Marcas.FirstOrDefaultAsync(m => m.Codigo == Marcador)
                ?? new Marca { Codigo = Marcador, Nombre = "Seed E2E genérico", Activo = true };
            if (marca.Id == 0)
                db.Marcas.Add(marca);

            await db.SaveChangesAsync();

            foreach (var (codigo, nombre) in Productos)
            {
                var existe = await db.Productos.AnyAsync(p => p.Codigo == codigo);
                if (existe)
                    continue;

                db.Productos.Add(new Producto
                {
                    Codigo = codigo,
                    Nombre = nombre,
                    CategoriaId = categoria.Id,
                    MarcaId = marca.Id,
                    PrecioCompra = 1000m,
                    PrecioVenta = 2000m,
                    PorcentajeIVA = 21m,
                    StockActual = 100m,
                    UnidadMedida = "UN",
                    Activo = true
                });
            }
            await db.SaveChangesAsync();
        }

        /// <summary>Borra únicamente lo que este seeder creó (clientes/productos marcados).</summary>
        public static async Task LimpiarAsync(AppDbContext db)
        {
            var documentos = Clientes.Select(c => c.Documento).ToArray();
            var clientes = await db.Clientes.Where(c => documentos.Contains(c.NumeroDocumento)).ToListAsync();
            db.Clientes.RemoveRange(clientes);

            var codigos = Productos.Select(p => p.Codigo).ToArray();
            var productos = await db.Productos.Where(p => codigos.Contains(p.Codigo)).ToListAsync();
            db.Productos.RemoveRange(productos);

            await db.SaveChangesAsync();

            var categoria = await db.Categorias.FirstOrDefaultAsync(c => c.Codigo == Marcador);
            if (categoria != null)
                db.Categorias.Remove(categoria);

            var marca = await db.Marcas.FirstOrDefaultAsync(m => m.Codigo == Marcador);
            if (marca != null)
                db.Marcas.Remove(marca);

            await db.SaveChangesAsync();
        }
    }
}
