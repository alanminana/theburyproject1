using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TheBuryProject.Helpers;

/// <summary>
/// Model binder que parsea DateOnly/DateOnly? primero en ISO 8601 (yyyy-MM-dd) —el único
/// formato que un &lt;input type="date"&gt; nativo envía, sin importar la cultura del
/// navegador— antes de intentar la cultura del servidor. Se aplica globalmente vía
/// <see cref="DateOnlyModelBinderProvider"/>. Mismo problema y mismo patrón que
/// <see cref="DecimalModelBinder"/>: bajo cultura es-AR (dd/MM/yyyy) un valor ISO como
/// "2028-08-04" se rechazaba en silencio (PUN-ML8: "Crear nueva version" de punitorios).
/// Vacío bindea null en DateOnly? y agrega error en DateOnly no-nullable.
/// </summary>
public class DateOnlyModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);

        var raw = valueResult.FirstValue;
        var esNullable = bindingContext.ModelMetadata.ModelType == typeof(DateOnly?);

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (esNullable)
                bindingContext.Result = ModelBindingResult.Success(null);
            else
                bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Ingresá una fecha válida.");

            return Task.CompletedTask;
        }

        if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoResult))
        {
            bindingContext.Result = ModelBindingResult.Success(isoResult);
            return Task.CompletedTask;
        }

        // Fallback defensivo: alguna fuente distinta de un <input type="date"> nativo
        // (ej. un valor tipeado a mano en otro contexto) podría postear en formato de
        // la cultura del servidor.
        if (DateOnly.TryParse(raw, valueResult.Culture, DateTimeStyles.None, out var cultureResult))
        {
            bindingContext.Result = ModelBindingResult.Success(cultureResult);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Ingresá una fecha válida.");
        return Task.CompletedTask;
    }
}
