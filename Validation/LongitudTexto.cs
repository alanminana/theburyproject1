namespace TheBuryProject.Validation
{
    /// <summary>
    /// Tope de longitud para textos libres que llegan como parámetros sueltos (motivos, observaciones) y no pasan
    /// por un ViewModel con [StringLength]. Se alinea con la columna de destino: sin tope, SQL Server falla al
    /// guardar por truncamiento (SQLite, el de los tests, no lo hace).
    /// </summary>
    public static class LongitudTexto
    {
        public static void Validar(string? valor, int maximo, string campo)
        {
            if (valor != null && valor.Trim().Length > maximo)
            {
                throw new InvalidOperationException($"{campo} no puede superar los {maximo} caracteres.");
            }
        }
    }
}
