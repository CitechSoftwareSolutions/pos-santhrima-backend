# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything (root files + src folder)
COPY . .

RUN dotnet restore POSSystem.sln
RUN dotnet publish src/POSSystem.API/POSSystem.API.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5151
ENV ASPNETCORE_URLS=http://+:5151
ENTRYPOINT ["dotnet", "POSSystem.API.dll"]