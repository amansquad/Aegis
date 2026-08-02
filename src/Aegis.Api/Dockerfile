# syntax=docker/dockerfile:1

# Multi-stage build. The runtime image carries no SDK, no source and no build tooling, which cuts
# the published image from roughly 900 MB to under 250 MB and removes a compiler from production.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Project and props files are copied before the source so that the restore layer is cached against
# dependency changes alone. Editing a .cs file then rebuilds without re-downloading every package.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Aegis.Domain/Aegis.Domain.csproj src/Aegis.Domain/
COPY src/Aegis.Application/Aegis.Application.csproj src/Aegis.Application/
COPY src/Aegis.Infrastructure/Aegis.Infrastructure.csproj src/Aegis.Infrastructure/
COPY src/Aegis.Api/Aegis.Api.csproj src/Aegis.Api/

RUN dotnet restore src/Aegis.Api/Aegis.Api.csproj

COPY src/ src/

RUN dotnet publish src/Aegis.Api/Aegis.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# curl is required by the compose healthcheck. Installed explicitly rather than assumed, because
# the aspnet image is deliberately minimal and does not ship it.
RUN apt-get update \
    && apt-get install --no-install-recommends --yes curl \
    && rm -rf /var/lib/apt/lists/*

# Runs as a non-root user. A container process running as root that is compromised gives an
# attacker root inside the container, and root inside is a considerably shorter path to root
# outside than an unprivileged account is.
RUN groupadd --gid 64198 aegis \
    && useradd --uid 64198 --gid aegis --no-create-home --shell /usr/sbin/nologin aegis

COPY --from=build --chown=aegis:aegis /app/publish .

# Serilog's file sink writes here; it must be writable by the non-root user.
RUN mkdir -p /app/logs && chown -R aegis:aegis /app/logs

USER aegis

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_gcServer=1

ENTRYPOINT ["dotnet", "Aegis.Api.dll"]
