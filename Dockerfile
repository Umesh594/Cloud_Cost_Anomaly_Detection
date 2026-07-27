FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CloudCost.AnomalyDetection.sln ./
COPY src/CloudCost.Anomaly.Core/CloudCost.Anomaly.Core.csproj src/CloudCost.Anomaly.Core/
COPY src/CloudCost.Anomaly.Worker/CloudCost.Anomaly.Worker.csproj src/CloudCost.Anomaly.Worker/
COPY tests/CloudCost.Anomaly.Tests/CloudCost.Anomaly.Tests.csproj tests/CloudCost.Anomaly.Tests/
RUN dotnet restore CloudCost.AnomalyDetection.sln

COPY . .
RUN dotnet test CloudCost.AnomalyDetection.sln --configuration Release --no-restore --logger trx
RUN dotnet publish src/CloudCost.Anomaly.Worker/CloudCost.Anomaly.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "CloudCost.Anomaly.Worker.dll"]
