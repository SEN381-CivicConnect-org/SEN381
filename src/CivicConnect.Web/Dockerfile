
# Stage 1: Build & Publish (Compilation)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Optimize layer caching
COPY ["src/CivicConnect.Web/CivicConnect.Web.csproj", "src/CivicConnect.Web/"]
COPY ["src/CivicConnect.Data/CivicConnect.Data.csproj", "src/CivicConnect.Data/"]
RUN dotnet restore "src/CivicConnect.Web/CivicConnect.Web.csproj"

# Copy remaining frontend and data source
COPY src/CivicConnect.Web/ src/CivicConnect.Web/
COPY src/CivicConnect.Data/ src/CivicConnect.Data/
WORKDIR "/src/src/CivicConnect.Web"
RUN dotnet publish "CivicConnect.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false


# Stage 2: Runtime Environment (Kestrel Web Host)

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install curl for container health check probe
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Run under a non-privileged system user (appuser)
RUN groupadd --system --gid 1001 appgroup && \
    useradd --system --uid 1001 --gid appgroup --no-create-home appuser

# Copy published application
COPY --from=build --chown=appuser:appgroup /app/publish .

# Enforce least privilege non-root execution
USER appuser

EXPOSE 8085
ENV ASPNETCORE_HTTP_PORTS=8085
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "CivicConnect.Web.dll"]
