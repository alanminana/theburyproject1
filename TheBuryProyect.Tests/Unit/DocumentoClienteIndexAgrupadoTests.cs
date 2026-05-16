using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public class DocumentoClienteIndexAgrupadoTests
{
    private static DocumentoClienteViewModel MakeDoc(
        int clienteId,
        string clienteNombre,
        EstadoDocumento estado = EstadoDocumento.Pendiente,
        DateTime? fechaSubida = null,
        string? numDoc = null)
    {
        var vm = new DocumentoClienteViewModel
        {
            Id = Random.Shared.Next(1, 99_999),
            ClienteId = clienteId,
            ClienteNombre = clienteNombre,
            Estado = estado,
            FechaSubida = fechaSubida ?? DateTime.UtcNow,
            TipoDocumento = TipoDocumentoCliente.DNI
        };
        vm.Cliente.NumeroDocumento = numDoc ?? string.Empty;
        return vm;
    }

    // -------------------------------------------------------------------------
    // Agrupación básica
    // -------------------------------------------------------------------------

    [Fact]
    public void FromDocumentos_AgrupaDocumentosMismoCliente()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana García"),
            MakeDoc(1, "Ana García"),
            MakeDoc(2, "Juan Pérez")
        };

        var grupos = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs);

        Assert.Equal(2, grupos.Count);
        var anaGrupo = grupos.Single(g => g.ClienteId == 1);
        Assert.Equal(2, anaGrupo.TotalDocumentos);
        Assert.Equal(2, anaGrupo.Documentos.Count);
    }

    [Fact]
    public void FromDocumentos_ListaVacia_RetornaVacio()
    {
        var grupos = DocumentoClienteClienteResumenViewModel.FromDocumentos(new List<DocumentoClienteViewModel>());
        Assert.Empty(grupos);
    }

    [Fact]
    public void FromDocumentos_UnSoloDocumento_UnGrupoConUnDoc()
    {
        var docs = new List<DocumentoClienteViewModel> { MakeDoc(5, "Carlos López") };

        var grupos = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs);

        Assert.Single(grupos);
        Assert.Equal(5, grupos[0].ClienteId);
        Assert.Single(grupos[0].Documentos);
    }

    // -------------------------------------------------------------------------
    // Totales por estado
    // -------------------------------------------------------------------------

    [Fact]
    public void FromDocumentos_CalculaTotalesPorEstado()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana", EstadoDocumento.Pendiente),
            MakeDoc(1, "Ana", EstadoDocumento.Verificado),
            MakeDoc(1, "Ana", EstadoDocumento.Rechazado),
            MakeDoc(1, "Ana", EstadoDocumento.Vencido)
        };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal(1, grupo.Pendientes);
        Assert.Equal(1, grupo.Verificados);
        Assert.Equal(1, grupo.Rechazados);
        Assert.Equal(1, grupo.Vencidos);
        Assert.Equal(4, grupo.TotalDocumentos);
    }

    [Fact]
    public void FromDocumentos_TodosVerificados_CorrectamenteContados()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana", EstadoDocumento.Verificado),
            MakeDoc(1, "Ana", EstadoDocumento.Verificado),
            MakeDoc(1, "Ana", EstadoDocumento.Verificado)
        };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal(3, grupo.Verificados);
        Assert.Equal(0, grupo.Pendientes);
        Assert.Equal(0, grupo.Rechazados);
        Assert.Equal(0, grupo.Vencidos);
    }

    // -------------------------------------------------------------------------
    // Documentos incluidos en el resumen
    // -------------------------------------------------------------------------

    [Fact]
    public void FromDocumentos_IncluyeDocumentosEnResumenCliente()
    {
        var doc1 = MakeDoc(1, "Ana");
        var doc2 = MakeDoc(1, "Ana");
        var docs = new List<DocumentoClienteViewModel> { doc1, doc2 };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal(2, grupo.Documentos.Count);
        Assert.Contains(grupo.Documentos, d => d.Id == doc1.Id);
        Assert.Contains(grupo.Documentos, d => d.Id == doc2.Id);
    }

    // -------------------------------------------------------------------------
    // EstadoResumen
    // -------------------------------------------------------------------------

    [Fact]
    public void EstadoResumen_ConVencidos_EsPrioritarioSobrePendiente()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana", EstadoDocumento.Vencido),
            MakeDoc(1, "Ana", EstadoDocumento.Pendiente)
        };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal("Con vencidos", grupo.EstadoResumen);
    }

    [Fact]
    public void EstadoResumen_ConRechazados_SinVencidos()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana", EstadoDocumento.Rechazado),
            MakeDoc(1, "Ana", EstadoDocumento.Verificado)
        };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal("Con rechazados", grupo.EstadoResumen);
    }

    [Fact]
    public void EstadoResumen_SoloPendientes_EsPendiente()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana", EstadoDocumento.Pendiente)
        };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal("Pendiente", grupo.EstadoResumen);
    }

    [Fact]
    public void EstadoResumen_TodosVerificados_EsCompleto()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana", EstadoDocumento.Verificado),
            MakeDoc(1, "Ana", EstadoDocumento.Verificado)
        };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal("Completo", grupo.EstadoResumen);
    }

    // -------------------------------------------------------------------------
    // Ordenamiento: clientes con problemas primero
    // -------------------------------------------------------------------------

    [Fact]
    public void FromDocumentos_ClienteConPendientesAparece_AntesDeCompleto()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Completo", EstadoDocumento.Verificado),
            MakeDoc(2, "Pendiente", EstadoDocumento.Pendiente)
        };

        var grupos = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs);

        Assert.Equal(2, grupos[0].ClienteId); // pendiente primero
        Assert.Equal(1, grupos[1].ClienteId);
    }

    [Fact]
    public void FromDocumentos_ClienteConVencidosAparece_AntesDeCompleto()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Completo", EstadoDocumento.Verificado),
            MakeDoc(2, "Vencido", EstadoDocumento.Vencido)
        };

        var grupos = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs);

        Assert.Equal(2, grupos[0].ClienteId); // vencido primero
    }

    [Fact]
    public void FromDocumentos_MismoOrdenPrioridad_OrdenAlfabetico()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(2, "Zeta", EstadoDocumento.Verificado),
            MakeDoc(1, "Alfa", EstadoDocumento.Verificado)
        };

        var grupos = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs);

        Assert.Equal("Alfa", grupos[0].ClienteNombre);
        Assert.Equal("Zeta", grupos[1].ClienteNombre);
    }

    // -------------------------------------------------------------------------
    // UltimaActualizacion
    // -------------------------------------------------------------------------

    [Fact]
    public void FromDocumentos_UltimaActualizacion_EsLaMasReciente()
    {
        var antigua = DateTime.UtcNow.AddDays(-10);
        var reciente = DateTime.UtcNow.AddDays(-1);

        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana", fechaSubida: antigua),
            MakeDoc(1, "Ana", fechaSubida: reciente)
        };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal(reciente, grupo.UltimaActualizacion);
    }

    // -------------------------------------------------------------------------
    // Campos de cliente (NumeroDocumento)
    // -------------------------------------------------------------------------

    [Fact]
    public void FromDocumentos_CopiaNumeroDocumentoDelPrimerDoc()
    {
        var docs = new List<DocumentoClienteViewModel>
        {
            MakeDoc(1, "Ana García", numDoc: "12345678")
        };

        var grupo = DocumentoClienteClienteResumenViewModel.FromDocumentos(docs).Single();

        Assert.Equal("12345678", grupo.DocumentoIdentidad);
    }
}
