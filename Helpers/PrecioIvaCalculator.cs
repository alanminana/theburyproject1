namespace TheBuryProject.Helpers
{
    public static class PrecioIvaCalculator
    {
        public static decimal AplicarIVA(decimal precio, decimal porcentajeIVA)
            => Math.Round(precio * (1 + porcentajeIVA / 100m), 2, MidpointRounding.AwayFromZero);

        public static decimal QuitarIVA(decimal precio, decimal porcentajeIVA)
            => porcentajeIVA > 0 ? Math.Round(precio / (1 + porcentajeIVA / 100m), 2) : precio;
    }
}
