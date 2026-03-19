using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Helper para obtener nombres legibles de valores enum con DisplayAttribute
    /// </summary>
    public static class EnumHelper
    {
        // Caché para evitar reflection repetida
        private static readonly ConcurrentDictionary<Enum, string> _displayNameCache = new();

        /// <summary>
        /// Obtiene el nombre mostrable de un valor enum desde su atributo [Display(Name="...")]
        /// </summary>
        public static string GetDisplayName(this Enum value)
        {
            return _displayNameCache.GetOrAdd(value, v =>
            {
                var field = v.GetType().GetField(v.ToString());
                if (field == null)
                    return v.ToString();

                var displayAttribute = field.GetCustomAttribute<DisplayAttribute>();
                return displayAttribute?.Name ?? v.ToString();
            });
        }

        /// <summary>
        /// Obtiene la descripción de un valor enum desde su atributo [Display(Description="...")]
        /// </summary>
        [Obsolete("Sin usos actuales. Mantener para uso futuro si se necesitan descripciones.")]
        public static string GetDisplayDescription(this Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            if (field == null)
                return string.Empty;

            var displayAttribute = field.GetCustomAttribute<DisplayAttribute>();
            return displayAttribute?.Description ?? string.Empty;
        }

        /// <summary>
        /// Genera una lista de SelectListItem para un enum (útil para dropdowns)
        /// </summary>
        /// <typeparam name="TEnum">Tipo del enum</typeparam>
        /// <param name="selected">Valor seleccionado (opcional)</param>
        /// <returns>Lista de SelectListItem con Value=int, Text=DisplayName</returns>
        public static IEnumerable<SelectListItem> GetSelectList<TEnum>(TEnum? selected = null)
            where TEnum : struct, Enum
        {
            // Usar GetNames para obtener todos los nombres, luego filtrar obsoletos
            // y eliminar duplicados por valor (para manejar alias como CreditoPersonal/CreditoPersonall)
            var seenValues = new HashSet<int>();
            
            foreach (var name in Enum.GetNames<TEnum>())
            {
                var field = typeof(TEnum).GetField(name);
                if (field == null) continue;
                
                // Excluir valores obsoletos
                if (field.GetCustomAttribute<ObsoleteAttribute>() != null) continue;
                
                var value = (TEnum)Enum.Parse(typeof(TEnum), name);
                var intValue = Convert.ToInt32(value);
                
                // Evitar duplicados por valor
                if (!seenValues.Add(intValue)) continue;
                
                yield return new SelectListItem
                {
                    Value = intValue.ToString(),
                    Text = value.GetDisplayName(),
                    Selected = selected.HasValue && EqualityComparer<TEnum>.Default.Equals(value, selected.Value)
                };
            }
        }

        /// <summary>
        /// Verifica si un valor de enum está marcado con [Obsolete]
        /// </summary>
        private static bool IsObsolete<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            var field = typeof(TEnum).GetField(value.ToString());
            return field?.GetCustomAttribute<ObsoleteAttribute>() != null;
        }
    }
}