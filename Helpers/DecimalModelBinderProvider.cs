using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TheBuryProject.Helpers;

/// <summary>
/// Aplica <see cref="DecimalModelBinder"/> a todo decimal/decimal? bindeado desde
/// form/query/route. Los inputs type="number" postean con punto decimal y el binding
/// por cultura del servidor (es-AR) lo interpretaba como separador de miles (x100);
/// este provider elimina esa clase de bug en todos los ViewModels sin atributos
/// por propiedad. No afecta cuerpos JSON (los maneja el input formatter).
/// </summary>
public class DecimalModelBinderProvider : IModelBinderProvider
{
    private static readonly DecimalModelBinder Binder = new();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var type = context.Metadata.ModelType;
        return type == typeof(decimal) || type == typeof(decimal?) ? Binder : null;
    }
}
