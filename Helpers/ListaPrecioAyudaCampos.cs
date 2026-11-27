namespace TheBuryProject.Helpers;

/// <summary>Qué hace cada campo de una lista de precios con el ERP.</summary>
public enum EfectoCampoListaPrecio
{
    /// <summary>Interviene en el cálculo del precio sugerido (no cambia precios ya cargados).</summary>
    CalculaSugerido,
    /// <summary>Cambia qué precio se usa o qué se permite guardar.</summary>
    Funcional,
    /// <summary>Identifica u ordena la lista; no interviene en ningún importe.</summary>
    Organizacion,
    /// <summary>Solo queda guardado como referencia.</summary>
    Informativo
}

public sealed record CampoListaPrecioAyuda(
    string Clave,
    string Nombre,
    EfectoCampoListaPrecio Efecto,
    string Breve,
    string Significa,
    string SirvePara,
    string SeAplicaA,
    string EfectoEnErp,
    string? Ejemplo = null);

/// <summary>
/// Textos de ayuda de los campos de una lista de precios: única fuente para el modal «¿Cómo funciona?» y para los
/// iconos de información de los formularios. Describen el comportamiento implementado (PrecioService y
/// PrecioVigenteResolver); si cambia una regla, se actualiza acá.
/// </summary>
public static class ListaPrecioAyudaCampos
{
    public static readonly IReadOnlyList<CampoListaPrecioAyuda> Todos = new CampoListaPrecioAyuda[]
    {
        new("nombre", "Nombre", EfectoCampoListaPrecio.Organizacion,
            "Cómo se llama la lista en las pantallas. No cambia ningún precio.",
            "El nombre visible de la lista.",
            "Reconocerla rápido.",
            "A la lista, no a importes.",
            "Se ve en Listas de precios, en el filtro de lista de Inventario y junto al precio que sale de ella. Obligatorio, hasta 100 caracteres.",
            "«Contado», «Mayorista»."),
        new("codigo", "Código", EfectoCampoListaPrecio.Organizacion,
            "Identificador corto e irrepetible. No cambia ningún precio.",
            "Una abreviatura única de la lista.",
            "Distinguir listas con nombres parecidos.",
            "A la lista, no a importes.",
            "Se guarda en mayúsculas y no puede repetirse entre listas vigentes. Obligatorio, hasta 20 caracteres.",
            "«contado» se guarda como CONTADO."),
        new("tipo", "Tipo de lista", EfectoCampoListaPrecio.Informativo,
            "Solo es una etiqueta. Elegir «Tarjeta» o «Mayorista» no conecta la lista con pagos ni con clientes.",
            "Una categoría: Contado, Tarjeta, Mayorista o Especial.",
            "Clasificar las listas a simple vista.",
            "A nada: es una etiqueta.",
            "Se muestra en el listado. No aplica la lista a pagos con tarjeta ni a clientes mayoristas.",
            null),
        new("margen", "Margen sobre costo (%)", EfectoCampoListaPrecio.CalculaSugerido,
            "Porcentaje que se suma al costo del producto para calcular el precio sugerido.",
            "Cuánto se agrega por encima del costo.",
            "Calcular el precio sugerido.",
            "Al costo real del producto (el costo cargado en Inventario).",
            "Solo cambia el sugerido. No modifica precios ya fijados ni el precio base, y no se cobra hasta que fijes un precio. Vacío o 0 = no suma nada.",
            "Costo $100.000 y margen 30 %: se suman $30.000."),
        new("margen-minimo", "Margen mínimo (%)", EfectoCampoListaPrecio.Funcional,
            "Piso al fijar un precio a mano: si queda por debajo, el ERP no lo guarda.",
            "La menor diferencia sobre el costo que se acepta.",
            "Evitar fijar por error un precio demasiado bajo.",
            "Al precio final que fijás a mano, comparado con el costo actual (el recargo ya cuenta dentro de ese precio).",
            "Solo se controla en «Fijar precio». No actúa sobre el precio base de Inventario, los ajustes masivos, los cambios por lotes ni los descuentos de una venta. Si el producto no tiene costo, no se controla. No puede superar al margen.",
            "Costo $100.000 y mínimo 20 %: $110.000 se rechaza (margen 10 %); desde $120.000 se acepta."),
        new("recargo", "Recargo (%)", EfectoCampoListaPrecio.CalculaSugerido,
            "Se suma sobre costo + margen al calcular el sugerido. No es el recargo del medio de pago.",
            "Un segundo porcentaje, después del margen.",
            "Que el sugerido de la lista ya salga encarecido.",
            "Al subtotal (costo + margen), no solo al costo.",
            "Solo cambia el sugerido. Es independiente del recargo por medio de pago: si fijás el precio con recargo y además el medio de pago tiene el suyo, se cobran los dos.",
            "$130.000 de subtotal y recargo 10 %: se suman $13.000."),
        new("cuotas", "Cantidad de cuotas", EfectoCampoListaPrecio.Informativo,
            "Solo se muestra en el listado. No crea cuotas ni divide el precio.",
            "Un dato de referencia, de 1 a 60.",
            "Recordar para cuántas cuotas se pensó la lista.",
            "A nada.",
            "Se muestra junto al código en el listado. No cambia las cuotas de ventas, tarjetas ni créditos: eso se define en la venta y en la configuración de pagos.",
            "«6 cuotas» no divide $143.000 en 6."),
        new("redondeo", "Redondeo", EfectoCampoListaPrecio.CalculaSugerido,
            "Redondea el precio sugerido. Sin definir = a la centena. No toca los precios que escribís a mano.",
            "Cómo se redondea el sugerido: sin redondeo, a la unidad, a la decena o a la centena.",
            "Obtener sugeridos con importes cómodos.",
            "Al resultado final de costo + margen + recargo.",
            "Va al valor más cercano (puede subir o bajar). Si queda justo en la mitad, va al múltiplo par: $143.050 → $143.000 y $143.150 → $143.200. El precio fijado se guarda tal como lo escribís.",
            "$119.362,10 a la centena: $119.400."),
        new("orden", "Orden", EfectoCampoListaPrecio.Organizacion,
            "Posición de la lista en pantalla (menor primero). No da prioridad de precio.",
            "Un número de posición, de 0 a 9999.",
            "Poner primero las listas más usadas.",
            "A la presentación de las listas.",
            "Ordena el listado y el filtro de lista de Inventario; con igual número, por nombre. No decide qué precio se usa.",
            null),
        new("descripcion", "Descripción", EfectoCampoListaPrecio.Informativo,
            "Texto de referencia. No cambia nada.",
            "Una explicación breve de la lista (hasta 500 caracteres).",
            "Dejar anotado para qué se creó.",
            "A nada.",
            "Queda guardada y se ve al editar la lista. No aparece en ventas ni cotizaciones.",
            "«Precios para pago en efectivo»."),
        new("notas", "Notas", EfectoCampoListaPrecio.Informativo,
            "Comentarios internos. No cambian nada.",
            "Comentarios internos de la lista (hasta 1000 caracteres).",
            "Dejar aclaraciones para el equipo.",
            "A nada.",
            "Quedan guardadas y se ven al editar la lista. Son distintas de las notas de un precio fijado, que quedan en su historial.",
            "«Revisar cuando cambie el proveedor»."),
        new("activa", "Activa", EfectoCampoListaPrecio.Funcional,
            "Una lista inactiva no aporta precios. Sus datos se conservan.",
            "Si la lista está habilitada.",
            "Pausar una lista sin darla de baja.",
            "A todos los precios de la lista.",
            "Inactiva: no aparece en el filtro de Inventario y sus precios no se usan (si Mercado Libre la tiene elegida, usa el precio base). La predeterminada no se puede desactivar.",
            null),
        new("predeterminada", "Lista predeterminada", EfectoCampoListaPrecio.Funcional,
            "La lista que usan Venta y Cotización. Solo puede haber una.",
            "La lista principal del negocio.",
            "Decidir de qué lista salen los precios que se cobran.",
            "A los productos que tengan un precio fijado en ella. Los demás usan su precio base.",
            "Al guardar, las operaciones nuevas usan sus precios de inmediato. La marca anterior se quita sola. No se puede desmarcar ni dar de baja: primero marcá otra.",
            "Heladera fijada en $145.000: Venta usa $145.000. Lavarropas sin precio fijado: Venta usa su precio base.")
    };

    public static CampoListaPrecioAyuda Buscar(string clave) => Todos.First(c => c.Clave == clave);

    public static (string Texto, string Clase) Etiqueta(EfectoCampoListaPrecio efecto) => efecto switch
    {
        EfectoCampoListaPrecio.CalculaSugerido => ("Calcula el sugerido", "badge-erp badge-erp-primary"),
        EfectoCampoListaPrecio.Funcional => ("Afecta qué se usa o se permite", "badge-erp badge-erp-warning"),
        EfectoCampoListaPrecio.Organizacion => ("Identifica u ordena", "badge-erp badge-erp-neutral"),
        _ => ("Solo informativo", "badge-erp badge-erp-neutral")
    };
}
