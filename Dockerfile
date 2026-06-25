# syntax=docker/dockerfile:1

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore against a minimal layer first (better caching): central package files + every csproj.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Atria.Domain/Atria.Domain.csproj          src/Atria.Domain/
COPY src/Atria.Application/Atria.Application.csproj src/Atria.Application/
COPY src/Atria.Infrastructure/Atria.Infrastructure.csproj src/Atria.Infrastructure/
COPY src/Atria.Api/Atria.Api.csproj                src/Atria.Api/
RUN dotnet restore src/Atria.Api/Atria.Api.csproj

# Copy the rest of the source and publish a trimmed, framework-dependent output.
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
