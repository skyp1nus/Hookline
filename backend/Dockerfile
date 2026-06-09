# ── build ──────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cache layer) — copy only the project graph + central config.
COPY global.json Directory.Build.props Directory.Packages.props Hookline.slnx ./
COPY src/Hookline.Host/Hookline.Host.csproj src/Hookline.Host/
COPY src/Hookline.SharedKernel/Hookline.SharedKernel.csproj src/Hookline.SharedKernel/
COPY src/Hookline.Infrastructure/Hookline.Infrastructure.csproj src/Hookline.Infrastructure/
COPY src/Modules/Hookline.Modules.Sample/Hookline.Modules.Sample.csproj src/Modules/Hookline.Modules.Sample/
COPY tests/Hookline.ArchitectureTests/Hookline.ArchitectureTests.csproj tests/Hookline.ArchitectureTests/
RUN dotnet restore src/Hookline.Host/Hookline.Host.csproj

COPY . .
RUN dotnet publish src/Hookline.Host/Hookline.Host.csproj -c Release -o /app --no-restore

# ── runtime ────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Kerberos/GSSAPI lib so Npgsql doesn't log a (harmless) probe error on startup.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Hookline.Host.dll"]
