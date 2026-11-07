using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para FiltrarDevoluciones, FiltrarGarantias,
/// GenerarCsvDevoluciones y GenerarCsvGarantias.
/// </summary>
public class DevolucionServiceFilterExportTests
{
    private readonly DevolucionService _sut;

    public DevolucionServiceFilterExportTests()
    {
        _sut = new DevolucionService(null!, null!, null!, NullLogger<TheBuryProject.Services.DevolucionService>.Instance, null);
    }

    // ---------------------------------------------------------------
    // FiltrarDevoluciones
    // ---------------------------------------------------------------

    private static List<Devolucion> CrearDevolucionesPrueba() => new()
    {
        new Devolucion
        {
            Id = 1,
            NumeroDevolucion = "DEV-001",
            Estado = EstadoDevolucion.Pendiente,
            TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero,
            FechaDevolucion = new DateTime(2026, 3, 20),
            Descripcion = "Producto defectuoso",
            Cliente = new Cliente { Nombre = "Juan", Apellido = "Perez", NumeroDocumento = "12345678" }
        },
        new Devolucion
        {
            Id = 2,
            NumeroDevolucion = "DEV-002",
            Estado = EstadoDevolucion.Aprobada,
            TipoResolucion = TipoResolucionDevolucion.NotaCredito,
            FechaDevolucion = new DateTime(2026, 3, 21),
            Descripcion = "No era lo que esperaba",
            Cliente = new Cliente { Nombre = "Maria", Apellido = "Lopez", NumeroDocumento = "87654321" }
        },
        new Devolucion
        {
            Id = 3,
            NumeroDevolucion = "DEV-003",
            Estado = EstadoDevolucion.Pendiente,
            TipoResolucion = TipoResolucionDevolucion.CambioMismoProducto,
            FechaDevolucion = new DateTime(2026, 3, 19),
            Descripcion = "Talle incorrecto",
            Cliente = new Cliente { Nombre = "Carlos", Apellido = "Garcia", NumeroDocumento = "11223344" }
        }
    };

    [Fact]
    public void FiltrarDevoluciones_SinFiltros_DevuelveTodas_OrdenadasPorFechaDesc()
    {
        var devoluciones = CrearDevolucionesPrueba();

        var resultado = _sut.FiltrarDevoluciones(devoluciones, "", null, null);

        Assert.Equal(3, resultado.Count);
        Assert.Equal("DEV-002", resultado[0].NumeroDevolucion);
        Assert.Equal("DEV-001", resultado[1].NumeroDevolucion);
        Assert.Equal("DEV-003", resultado[2].NumeroDevolucion);
    }

    [Fact]
    public void FiltrarDevoluciones_PorTexto_FiltraPorNumero()
    {
        var devoluciones = CrearDevolucionesPrueba();

        var resultado = _sut.FiltrarDevoluciones(devoluciones, "DEV-001", null, null);

        Assert.Single(resultado);
        Assert.Equal("DEV-001", resultado[0].NumeroDevolucion);
    }

    [Fact]
    public void FiltrarDevoluciones_PorTexto_FiltraPorCliente()
    {
        var devoluciones = CrearDevolucionesPrueba();

        var resultado = _sut.FiltrarDevoluciones(devoluciones, "Lopez", null, null);

        Assert.Single(resultado);
        Assert.Equal("DEV-002", resultado[0].NumeroDevolucion);
    }

    [Fact]
    public void FiltrarDevoluciones_PorTexto_FiltraPorDocumento()
    {
        var devoluciones = CrearDevolucionesPrueba();

        var resultado = _sut.FiltrarDevoluciones(devoluciones, "11223344", null, null);

        Assert.Single(resultado);
        Assert.Equal("DEV-003", resultado[0].NumeroDevolucion);
    }

    [Fact]
    public void FiltrarDevoluciones_PorEstado_FiltraPendientes()
    {
        var devoluciones = CrearDevolucionesPrueba();

        var resultado = _sut.FiltrarDevoluciones(devoluciones, "", "Pendiente", null);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, d => Assert.Equal(EstadoDevolucion.Pendiente, d.Estado));
    }

    [Fact]
    public void FiltrarDevoluciones_PorResolucion_FiltraNotaCredito()
    {
        var devoluciones = CrearDevolucionesPrueba();

        var resultado = _sut.FiltrarDevoluciones(devoluciones, "", null, "NotaCredito");

        Assert.Single(resultado);
        Assert.Equal(TipoResolucionDevolucion.NotaCredito, resultado[0].TipoResolucion);
    }

    [Fact]
    public void FiltrarDevoluciones_EstadoInvalido_DevuelveTodas()
    {
        var devoluciones = CrearDevolucionesPrueba();

        var resultado = _sut.FiltrarDevoluciones(devoluciones, "", "EstadoInexistente", null);

        Assert.Equal(3, resultado.Count);
    }

    [Fact]
    public void FiltrarDevoluciones_BusquedaCaseInsensitive()
    {
        var devoluciones = CrearDevolucionesPrueba();

        var resultado = _sut.FiltrarDevoluciones(devoluciones, "perez", null, null);

        Assert.Single(resultado);
        Assert.Equal("DEV-001", resultado[0].NumeroDevolucion);
    }

    // ---------------------------------------------------------------
    // FiltrarGarantias
    // ---------------------------------------------------------------

    private static List<Garantia> CrearGarantiasPrueba()
    {
        var hoy = DateTime.UtcNow.Date;
        return new()
        {
            new Garantia
            {
                Id = 1,
                NumeroGarantia = "GAR-001",
                Estado = EstadoGarantia.Vigente,
                FechaInicio = hoy.AddMonths(-6),
                FechaVencimiento = hoy.AddDays(15),
                MesesGarantia = 12,
                GarantiaExtendida = false,
                Cliente = new Cliente { Nombre = "Juan", Apellido = "Perez", NumeroDocumento = "12345678" },
                Producto = new Producto { Nombre = "Monitor 24", Codigo = "MON-24" }
            },
            new Garantia
            {
                Id = 2,
                NumeroGarantia = "GAR-002",
                Estado = EstadoGarantia.Vencida,
                FechaInicio = hoy.AddMonths(-14),
                FechaVencimiento = hoy.AddDays(-30),
                MesesGarantia = 12,
                GarantiaExtendida = false,
                Cliente = new Cliente { Nombre = "Maria", Apellido = "Lopez", NumeroDocumento = "87654321" },
                Producto = new Producto { Nombre = "Teclado Mecanico", Codigo = "TEC-MEC" }
            },
            new Garantia
            {
                Id = 3,
                NumeroGarantia = "GAR-003",
                Estado = EstadoGarantia.EnUso,
                FechaInicio = hoy.AddMonths(-3),
                FechaVencimiento = hoy.AddMonths(9),
                MesesGarantia = 24,
                GarantiaExtendida = true,
                Cliente = new Cliente { Nombre = "Carlos", Apellido = "Garcia", NumeroDocumento = "11223344" },
                Producto = new Producto { Nombre = "Mouse Gamer", Codigo = "MOU-GAM" }
            }
        };
    }

    [Fact]
    public void FiltrarGarantias_SinFiltros_DevuelveTodas_OrdenadasPorVencimiento()
    {
        var garantias = CrearGarantiasPrueba();

        var resultado = _sut.FiltrarGarantias(garantias, "", null, null);

        Assert.Equal(3, resultado.Count);
        Assert.Equal("GAR-002", resultado[0].NumeroGarantia); // vence primero (pasado)
    }

    [Fact]
    public void FiltrarGarantias_PorTexto_FiltraPorProducto()
    {
        var garantias = CrearGarantiasPrueba();

        var resultado = _sut.FiltrarGarantias(garantias, "Monitor", null, null);

        Assert.Single(resultado);
        Assert.Equal("GAR-001", resultado[0].NumeroGarantia);
    }

    [Fact]
    public void FiltrarGarantias_PorTexto_FiltraPorCodigoProducto()
    {
        var garantias = CrearGarantiasPrueba();

        var resultado = _sut.FiltrarGarantias(garantias, "TEC-MEC", null, null);

        Assert.Single(resultado);
        Assert.Equal("GAR-002", resultado[0].NumeroGarantia);
    }

    [Fact]
    public void FiltrarGarantias_PorEstado_FiltraVigentes()
    {
        var garantias = CrearGarantiasPrueba();

        var resultado = _sut.FiltrarGarantias(garantias, "", "Vigente", null);

        Assert.Single(resultado);
        Assert.Equal(EstadoGarantia.Vigente, resultado[0].Estado);
    }

    [Fact]
    public void FiltrarGarantias_VentanaProximas_FiltraProximasVencer()
    {
        var garantias = CrearGarantiasPrueba();

        var resultado = _sut.FiltrarGarantias(garantias, "", null, "proximas");

        Assert.Single(resultado);
        Assert.Equal("GAR-001", resultado[0].NumeroGarantia);
    }

    [Fact]
    public void FiltrarGarantias_VentanaVencidas_FiltraVencidas()
    {
        var garantias = CrearGarantiasPrueba();

        var resultado = _sut.FiltrarGarantias(garantias, "", null, "vencidas");

        Assert.Single(resultado);
        Assert.Equal("GAR-002", resultado[0].NumeroGarantia);
    }

    [Fact]
    public void FiltrarGarantias_VentanaEnUso_FiltraEnUso()
    {
        var garantias = CrearGarantiasPrueba();

        var resultado = _sut.FiltrarGarantias(garantias, "", null, "enuso");

        Assert.Single(resultado);
        Assert.Equal("GAR-003", resultado[0].NumeroGarantia);
    }

    [Fact]
    public void FiltrarGarantias_VentanaExtendidas_FiltraExtendidas()
    {
        var garantias = CrearGarantiasPrueba();

        var resultado = _sut.FiltrarGarantias(garantias, "", null, "extendidas");

        Assert.Single(resultado);
        Assert.True(resultado[0].GarantiaExtendida);
    }

    // ---------------------------------------------------------------
    // GenerarCsvDevoluciones
    // ---------------------------------------------------------------

    [Fact]
    public void GenerarCsvDevoluciones_ContieneHeader()
    {
        var csv = System.Text.Encoding.UTF8.GetString(
            _sut.GenerarCsvDevoluciones(Array.Empty<Devolucion>())).TrimStart('﻿');

        Assert.StartsWith("Id;Cliente;Documento;Venta;Motivo;Resolucion;Impacto;Estado;Fecha;Monto", csv);
    }

    [Fact]
    public void GenerarCsvDevoluciones_ConDatos_GeneraLineas()
    {
        var devoluciones = new List<Devolucion>
        {
            new()
            {
                NumeroDevolucion = "DEV-001",
                TipoResolucion = TipoResolucionDevolucion.ReembolsoDinero,
                RegistrarEgresoCaja = true,
                Motivo = MotivoDevolucion.DefectoFabrica,
                Estado = EstadoDevolucion.Pendiente,
                FechaDevolucion = new DateTime(2026, 3, 20, 14, 30, 0),
                TotalDevolucion = 1500.50m,
                Cliente = new Cliente { Nombre = "Juan", Apellido = "Perez", NumeroDocumento = "12345678" }
            }
        };

        var csv = System.Text.Encoding.UTF8.GetString(
            _sut.GenerarCsvDevoluciones(devoluciones));

        Assert.Contains("DEV-001", csv);
        Assert.Contains("Perez", csv);
        Assert.Contains("Reembolso por caja", csv);
        Assert.Contains("1500.50", csv);
    }

    // QA exportaciones: el CSV se abre en Excel. Sin BOM Excel lo lee como ANSI y rompe tildes/eñes; y el cliente
    // salía como "Apellido, Nombre - DNI: n" con el documento repetido en la columna siguiente.
    [Fact]
    public void GenerarCsv_EmpiezaConBomUtf8_YConservaTildesYEnies()
    {
        var devolucion = new Devolucion
        {
            NumeroDevolucion = "DEV-ÑU",
            Motivo = MotivoDevolucion.DefectoFabrica,
            Estado = EstadoDevolucion.Pendiente,
            FechaDevolucion = new DateTime(2026, 3, 20),
            Cliente = new Cliente { Nombre = "José", Apellido = "Muñoz", NumeroDocumento = "12345678" }
        };
        var garantia = new Garantia
        {
            NumeroGarantia = "GAR-1",
            FechaInicio = new DateTime(2026, 1, 1),
            FechaVencimiento = new DateTime(2027, 1, 1),
            Cliente = new Cliente { Nombre = "José", Apellido = "Muñoz", NumeroDocumento = "12345678" },
            Producto = new Producto { Codigo = "P-1", Nombre = "Cámara" }
        };

        foreach (var bytes in new[] { _sut.GenerarCsvDevoluciones(new[] { devolucion }), _sut.GenerarCsvGarantias(new[] { garantia }) })
        {
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            var texto = new System.Text.UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            Assert.Contains("Muñoz, José", texto);
        }
    }

    [Fact]
    public void GenerarCsv_ClienteSinDniRepetido_ElDocumentoVaSoloEnSuColumna()
    {
        var cliente = new Cliente { Nombre = "Juan", Apellido = "Perez", NumeroDocumento = "12345678" };
        var csvDev = System.Text.Encoding.UTF8.GetString(_sut.GenerarCsvDevoluciones(new[]
        {
            new Devolucion { NumeroDevolucion = "DEV-1", FechaDevolucion = new DateTime(2026, 3, 20), Cliente = cliente }
        }));
        var csvGar = System.Text.Encoding.UTF8.GetString(_sut.GenerarCsvGarantias(new[]
        {
            new Garantia { NumeroGarantia = "GAR-1", FechaInicio = new DateTime(2026, 1, 1), FechaVencimiento = new DateTime(2027, 1, 1), Cliente = cliente }
        }));

        foreach (var csv in new[] { csvDev, csvGar })
        {
            Assert.DoesNotContain("DNI:", csv);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(csv, "12345678").Count);
            Assert.Contains("\"Perez, Juan\";\"12345678\"", csv);
        }
    }

    // ---------------------------------------------------------------
    // GenerarCsvGarantias
    // ---------------------------------------------------------------

    [Fact]
    public void GenerarCsvGarantias_ContieneHeader()
    {
        var csv = System.Text.Encoding.UTF8.GetString(
            _sut.GenerarCsvGarantias(Array.Empty<Garantia>())).TrimStart('﻿');

        Assert.StartsWith("Garantia;Cliente;Documento;Producto;Codigo;Estado;Inicio;Vencimiento;CoberturaMeses;Extendida;Observacion", csv);
    }

    [Fact]
    public void GenerarCsvGarantias_ConDatos_GeneraLineas()
    {
        var garantias = new List<Garantia>
        {
            new()
            {
                NumeroGarantia = "GAR-001",
                Estado = EstadoGarantia.Vigente,
                FechaInicio = new DateTime(2026, 1, 1),
                FechaVencimiento = new DateTime(2027, 1, 1),
                MesesGarantia = 12,
                GarantiaExtendida = true,
                ObservacionesActivacion = "Test obs",
                Cliente = new Cliente { Nombre = "Juan", Apellido = "Perez", NumeroDocumento = "12345678" },
                Producto = new Producto { Nombre = "Monitor", Codigo = "MON-01" }
            }
        };

        var csv = System.Text.Encoding.UTF8.GetString(
            _sut.GenerarCsvGarantias(garantias));

        Assert.Contains("GAR-001", csv);
        Assert.Contains("Monitor", csv);
        Assert.Contains("MON-01", csv);
        Assert.Contains("Si", csv); // GarantiaExtendida = true
        Assert.Contains("Test obs", csv);
    }

    // ---------------------------------------------------------------
    // Inyección de fórmulas en CSV (CWE-1236): el texto libre (nombre de producto, observaciones) no puede
    // abrirse como fórmula en Excel/LibreOffice.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("=HYPERLINK('http://evil.example','x')")]
    [InlineData("+cmd|'/c calc'!A1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1+1)")]
    public void GenerarCsvGarantias_TextoLibreConPrefijoDeFormula_SeNeutraliza(string textoMalicioso)
    {
        var garantias = new List<Garantia>
        {
            new()
            {
                NumeroGarantia = "GAR-X",
                Estado = EstadoGarantia.Vigente,
                FechaInicio = new DateTime(2026, 1, 1),
                FechaVencimiento = new DateTime(2027, 1, 1),
                MesesGarantia = 12,
                ObservacionesActivacion = textoMalicioso,
                Producto = new Producto { Codigo = "P-1", Nombre = textoMalicioso },
                Cliente = new Cliente { Nombre = "Juan", Apellido = "Perez", NumeroDocumento = "12345678" }
            }
        };

        var csv = System.Text.Encoding.UTF8.GetString(_sut.GenerarCsvGarantias(garantias));
        var fila = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1].TrimEnd();
        var celdas = fila.Split(';').Select(c => c.Trim('"')).ToList();

        var prefijosDeFormula = new[] { '=', '+', '-', '@' };
        Assert.All(celdas, celda =>
            Assert.False(celda.Length > 0 && prefijosDeFormula.Contains(celda[0]),
                $"La celda '{celda}' se interpretaría como fórmula."));
        // El contenido original se conserva (solo se antepone un apóstrofo).
        Assert.Contains(textoMalicioso.Replace("\"", "\"\""), csv);
    }

    [Fact]
    public void GenerarCsvDevoluciones_ImporteNegativoLegitimo_NoSeAltera()
    {
        var devoluciones = new List<Devolucion>
        {
            new()
            {
                NumeroDevolucion = "DEV-N",
                Estado = EstadoDevolucion.Pendiente,
                FechaDevolucion = new DateTime(2026, 3, 20),
                TotalDevolucion = -15.50m,
                Cliente = new Cliente { Nombre = "Juan", Apellido = "Perez", NumeroDocumento = "12345678" }
            }
        };

        var csv = System.Text.Encoding.UTF8.GetString(_sut.GenerarCsvDevoluciones(devoluciones));

        Assert.Contains("\"-15.50\"", csv);
    }
}
