
# Stage 1: Build & Publish (Compilation)

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Optimize layer caching: copy .csproj files first
COPY ["src/CivicConnect.Api/CivicConnect.Api.csproj", "src/CivicConnect.Api/"]
COPY ["src/CivicConnect.Data/CivicConnect.Data.csproj", "src/CivicConnect.Data/"]

# Restore dependencies
RUN dotnet restore "src/CivicConnect.Api/CivicConnect.Api.csproj"

# Copy remaining source files
COPY src/CivicConnect.Api/ src/CivicConnect.Api/
COPY src/CivicConnect.Data/ src/CivicConnect.Data/

# Publish release binaries
WORKDIR "/src/src/CivicConnect.Api"
RUN dotnet publish "CivicConnect.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime Environment

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
WORKDIR /app

# Install curl for container health check probe
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Run under a non-privileged system user (appuser)
RUN groupadd --system --gid 1001 appgroup && \
    useradd --system --uid 1001 --gid appgroup --no-create-home appuser

# Copy published artifacts and set ownership
COPY --from=build --chown=appuser:appgroup /app/publish .

# Enforce least privilege non-root execution
USER appuser

EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "CivicConnect.Api.dll"]
