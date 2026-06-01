FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8083
ENV ASPNETCORE_URLS=http://+:8083

ENTRYPOINT ["dotnet", "OrderIntegrationService.dll"]
