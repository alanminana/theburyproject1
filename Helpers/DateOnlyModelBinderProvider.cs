using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TheBuryProject.Helpers;

/// <summary>
/// Aplica <see cref="DateOnlyModelBinder"/> a todo DateOnly/DateOnly? bindeado desde
/// form/query/route. Los inputs type="date" siempre postean en ISO 8601 (yyyy-MM-dd)
/// sin importar la cultura del navegador; el binding por cultura del servidor (es-AR,
/// dd/MM/yyyy) rechazaba ese valor en silencio. Mismo patrón que
/// <see cref="DecimalModelBinderProvider"/>. No afecta cuerpos JSON (los maneja el
/// input formatter).
/// </summary>
public class DateOnlyModelBinderProvider : IModelBinderProvider
{
    private static readonly DateOnlyModelBinder Binder = new();

    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var type = context.Metadata.ModelType;
        return type == typeof(DateOnly) || type == typeof(DateOnly?) ? Binder : null;
    }
}
