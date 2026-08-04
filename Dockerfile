# syntax=docker/dockerfile:1

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore against a minimal layer first (better caching): central package files + every csproj.
COPY Directory.Build.props Directory.Packages.props ./

# Atria.Infrastructure project-references into external/tessera (a git submodule) and reaches
# Nethereum only through it, so the restore cannot resolve without these. external's own
# Directory.Packages.props opts that subtree out of central package management — copy it too, or
# the vendored projects inherit the root one and fail on their inline versions.
COPY external/Directory.Packages.props external/
COPY external/tessera/src/Tessera.Core/Tessera.Core.csproj                             external/tessera/src/Tessera.Core/
COPY external/tessera/src/Tessera.Chains.Abstractions/Tessera.Chains.Abstractions.csproj external/tessera/src/Tessera.Chains.Abstractions/
COPY external/tessera/src/Tessera.Chains.Evm/Tessera.Chains.Evm.csproj                 external/tessera/src/Tessera.Chains.Evm/

COPY src/Atria.Domain/Atria.Domain.csproj          src/Atria.Domain/
COPY src/Atria.Application/Atria.Application.csproj src/Atria.Application/
COPY src/Atria.Infrastructure/Atria.Infrastructure.csproj src/Atria.Infrastructure/
COPY src/Atria.Api/Atria.Api.csproj                src/Atria.Api/
RUN dotnet restore src/Atria.Api/Atria.Api.csproj

# Copy the rest of the source and publish a trimmed, framework-dependent output.
COPY external/ external/
COPY src/ src/
RUN dotnet publish src/Atria.Api/Atria.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Kestrel listens on 8080 (the non-root aspnet image default).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

# Run as the image's built-in non-root user.
USER $APP_UID

ENTRYPOINT ["dotnet", "Atria.Api.dll"]
