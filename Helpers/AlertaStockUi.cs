using System.Text.Json;
using TheBuryProject.Models.Enums;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Helpers;

/// <summary>Presentación de las alertas de stock (texto, estilo y confirmaciones).</summary>
public static class AlertaStockUi
{
    public static (string Clase, string Texto) Tipo(TipoAlertaStock tipo) => tipo switch
    {
        TipoAlertaStock.StockAgotado => ("inv-pill inv-pill--danger", "Stock agotado"),
        TipoAlertaStock.StockCritico => ("inv-pill inv-pill--warning", "Stock crítico"),
        TipoAlertaStock.StockBajo => ("inv-pill", "Stock bajo"),
        TipoAlertaStock.ProductoSinMovimiento => ("inv-pill inv-pill--muted", "Sin movimiento"),
        _ => ("inv-pill", tipo.ToString())
    };

    public static (string Clase, string Texto) Prioridad(PrioridadAlerta prioridad) => prioridad switch
    {
        PrioridadAlerta.Critica => ("inv-pill inv-pill--danger", "Crítica"),
        PrioridadAlerta.Alta => ("inv-pill inv-pill--warning", "Alta"),
        PrioridadAlerta.Media => ("inv-pill", "Media"),
        _ => ("inv-pill inv-pill--muted", prioridad.ToString())
    };

    public static (string Clase, string Texto) Estado(EstadoAlerta estado) => estado switch
    {
        EstadoAlerta.Pendiente => ("inv-pill inv-pill--warning", "Pendiente"),
        EstadoAlerta.EnProceso => ("inv-pill inv-pill--accent", "En proceso"),
        EstadoAlerta.Resuelta => ("inv-pill inv-pill--ok", "Resuelta"),
        EstadoAlerta.Ignorada => ("inv-pill inv-pill--muted", "Ignorada"),
        _ => ("inv-pill", estado.ToString())
    };

    public static bool EstaActiva(EstadoAlerta estado) => estado is EstadoAlerta.Pendiente or EstadoAlerta.EnProceso;

    /// <summary>Confirmación de «Resolver» desde un listado: permite dejar observaciones.</summary>
    public static string ConfirmarResolver(AlertaStockViewModel alerta) => JsonSerializer.Serialize(new
    {
        titulo = "¿Marcar la alerta como resuelta?",
        datos = new[] { new[] { "Producto", alerta.ProductoNombre } },
        consecuencias = new[] { "La alerta sale de las pendientes. El stock no cambia." },
        motivo = new { nombre = "observaciones", etiqueta = "Observaciones (opcional)", requerido = false, maximo = 1000 },
        etiquetaConfirmar = "Resolver",
        tono = "primario"
    });

    public static string ConfirmarIgnorar(AlertaStockViewModel alerta) => JsonSerializer.Serialize(new
    {
        titulo = "¿Ignorar esta alerta?",
        datos = new[] { new[] { "Producto", alerta.ProductoNombre } },
        consecuencias = new[] { "La alerta se cierra sin resolver el faltante. El stock no cambia." },
        etiquetaConfirmar = "Ignorar alerta"
    });
}
