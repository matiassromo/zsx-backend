# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first (for layer caching)
COPY Backend-ZS.sln .
COPY Backend-ZS.API/Backend-ZS.API.csproj Backend-ZS.API/
COPY Backend-ZS.API.Tests/Backend-ZS.API.Tests.csproj Backend-ZS.API.Tests/

# Restore dependencies
RUN dotnet restore "Backend-ZS.API/Backend-ZS.API.csproj"

# Copy everything else and publish
COPY . .
RUN dotnet publish "Backend-ZS.API/Backend-ZS.API.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

# Render injects PORT at runtime; fall back to 8080 locally
CMD dotnet Backend-ZS.API.dll --urls "http://+:${PORT:-8080}"
