FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY nuget.config ./
COPY Permissions.Domain/Permissions.Domain.csproj Permissions.Domain/
COPY Permissions.Application/Permissions.Application.csproj Permissions.Application/
COPY Permissions.Infrastructure/Permissions.Infrastructure.csproj Permissions.Infrastructure/
COPY Permissions.Api/Permissions.Api.csproj Permissions.Api/
COPY Permissions.Migrator/Permissions.Migrator.csproj Permissions.Migrator/

RUN dotnet restore Permissions.Api/Permissions.Api.csproj --configfile nuget.config \
    && dotnet restore Permissions.Migrator/Permissions.Migrator.csproj --configfile nuget.config

COPY Permissions.Domain/ Permissions.Domain/
COPY Permissions.Application/ Permissions.Application/
COPY Permissions.Infrastructure/ Permissions.Infrastructure/
COPY Permissions.Api/ Permissions.Api/
COPY Permissions.Migrator/ Permissions.Migrator/
COPY docker/appsettings.json Permissions.Api/appsettings.json

RUN dotnet publish Permissions.Api/Permissions.Api.csproj -c Release --no-restore -p:DebugType=None -p:DebugSymbols=false -o /out/api \
    && dotnet publish Permissions.Migrator/Permissions.Migrator.csproj -c Release --no-restore -p:DebugType=None -p:DebugSymbols=false -o /out/migrator

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app/api
COPY --from=build /out/api/ ./
COPY --from=build /out/migrator/ /app/migrator/
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Permissions.Api.dll"]
