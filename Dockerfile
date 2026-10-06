# syntax=docker/dockerfile:1.7

# Build stages run on the builder's own platform (no CPU emulation: Vite
# crashes under amd64 emulation on Apple Silicon). The web output is static
# files and .NET cross-publishes for the target architecture.
FROM --platform=$BUILDPLATFORM node:24-bookworm-slim AS web-build
WORKDIR /source/src/PurchaseMemory.Web
COPY src/PurchaseMemory.Web/package.json src/PurchaseMemory.Web/package-lock.json ./
RUN npm ci
COPY src/PurchaseMemory.Web/ ./
RUN npm run build

# The SDK tag follows global.json's feature band (10.0.3xx, latestPatch): the
# floating "10.0" tag already moved to 10.0.4xx, which global.json refuses.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302 AS api-build
ARG TARGETARCH
WORKDIR /source
COPY Directory.Build.props Directory.Packages.props global.json PurchaseMemory.slnx ./
COPY src/ ./src/
COPY --from=web-build /source/src/PurchaseMemory.Api/wwwroot/ ./src/PurchaseMemory.Api/wwwroot/
# ReadyToRun precompiles the app's assemblies: on 0.1 CPU the JIT dominates a
# cold start (Render free sleeps after 15 minutes idle). Measured at 0.1 CPU:
# ~18 s -> ~12.5 s to healthy, same memory, +48 MB image (РЕЗУЛЬТАТЫ_ХОСТИНГ.md).
RUN dotnet restore src/PurchaseMemory.Api/PurchaseMemory.Api.csproj -a "$TARGETARCH" \
        -p:PublishReadyToRun=true \
    && dotnet publish src/PurchaseMemory.Api/PurchaseMemory.Api.csproj \
        -a "$TARGETARCH" \
        --self-contained false \
        -p:PublishReadyToRun=true \
        --no-restore \
        --configuration Release \
        --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# PORT is read by the app (HostingSetup.UseHostingPort); hosting platforms such
# as Render override it. ASPNETCORE_HTTP_PORTS from the base image is cleared so
# only one address source exists.
#
# Recognition settings come from appsettings.json (OpenAI pipeline); the local
# Ollama address for compose lives in compose.yaml, not in the image.
#
# Workstation GC (the web SDK defaults to server GC). Measured at 512 MB
# (РЕЗУЛЬТАТЫ_ХОСТИНГ.md): with a 0.1 CPU quota the runtime sees one CPU and
# both modes are equal; when a host exposes more CPUs, server GC costs a few
# MB more for nothing a two-person app needs. Pinned so it never depends on
# how the host reports CPUs.
ENV PORT=5099 \
    ASPNETCORE_HTTP_PORTS= \
    Paths__DataDirectory=/app/data \
    Paths__LogDirectory=/app/logs \
    DOTNET_gcServer=0
EXPOSE 5099
COPY --from=api-build /app/publish/ ./
# Runs as the image's unprivileged "app" user; only data and logs are writable.
RUN mkdir -p /app/data /app/logs && chown "$APP_UID" /app/data /app/logs
USER $APP_UID
ENTRYPOINT ["dotnet", "PurchaseMemory.Api.dll"]
