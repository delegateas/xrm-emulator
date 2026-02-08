# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for better layer caching
COPY XrmEmulator.sln ./
COPY src/XrmEmulator/XrmEmulator.csproj src/XrmEmulator/
COPY external/XrmMockup/src/XrmMockup365/XrmMockup365.csproj external/XrmMockup/src/XrmMockup365/
COPY external/XrmMockup/src/MetadataSkeleton/ external/XrmMockup/src/MetadataSkeleton/

# Restore dependencies
RUN dotnet restore src/XrmEmulator/XrmEmulator.csproj

# Copy remaining source
COPY src/XrmEmulator/ src/XrmEmulator/
COPY external/XrmMockup/src/ external/XrmMockup/src/

# Publish
RUN dotnet publish src/XrmEmulator/XrmEmulator.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create directories for metadata and data persistence
RUN mkdir -p /app/Metadata /data

COPY --from=build /app/publish .

# Configure environment
ENV ASPNETCORE_URLS=http://+:8080
ENV XrmMockup__MetadataDirectoryPath=/app/Metadata
ENV Snapshot__FilePath=/data/xrm-emulator-snapshot.zip

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "XrmEmulator.dll"]
