FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY *.csproj ./
RUN dotnet restore

COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

RUN mkdir -p /app/data && chmod 777 /app/data

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OsposAdapter.dll"]
