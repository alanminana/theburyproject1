# syntax=docker/dockerfile:1

# ---------- Runtime base ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS base
WORKDIR /app

# libfontconfig1 + fonts-liberation: requeridos por QuestPDF (PDFs de cotizaciones, contratos y reportes) en Linux.
# curl: usado por el HEALTHCHECK.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-liberation curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DataProtection__KeysPath=/keys

# Directorios con estado. Se crean con el dueño correcto para que los named volumes
# (que heredan permisos de la imagen en el primer montaje) sean escribibles sin root.
RUN mkdir -p /keys /app/App_Data /app/wwwroot/uploads \
    && chown -R $APP_UID:$APP_UID /keys /app/App_Data /app/wwwroot/uploads

USER $APP_UID
EXPOSE 8080

# ---------- Build ----------
# global.json (SDK 10) participa del build: el SDK de la imagen debe coincidir con el pin (rollForward latestPatch).
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /src

# Solo el .csproj primero: la capa de restore se cachea mientras no cambien las dependencias.
COPY global.json TheBuryProyect.csproj ./
RUN dotnet restore TheBuryProyect.csproj

COPY . .
RUN dotnet publish TheBuryProyect.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Final ----------
FROM base AS final
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

# Readiness (incluye SQL Server). La app no ejecuta migraciones (las aplica el servicio `migrate` antes de arrancar), asi que
# los fallos dentro de start-period no cuentan; pasado ese plazo, 3 fallos seguidos (90 s) => unhealthy.
HEALTHCHECK --interval=30s --timeout=5s --start-period=90s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "TheBuryProyect.dll"]
