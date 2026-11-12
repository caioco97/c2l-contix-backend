# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Contix.slnx Directory.Build.props ./
COPY src/Contix.Domain/Contix.Domain.csproj src/Contix.Domain/
COPY src/Contix.Application/Contix.Application.csproj src/Contix.Application/
COPY src/Contix.Infrastructure/Contix.Infrastructure.csproj src/Contix.Infrastructure/
COPY src/Contix.Api/Contix.Api.csproj src/Contix.Api/
RUN dotnet restore src/Contix.Api/Contix.Api.csproj

COPY src/ src/
RUN dotnet publish src/Contix.Api/Contix.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    FileStorage__LocalPath=/app/storage

RUN mkdir -p /app/storage && chown -R app:app /app/storage
COPY --from=build --chown=app:app /app/publish .

USER app
EXPOSE 8080
VOLUME ["/app/storage"]
ENTRYPOINT ["dotnet", "Contix.Api.dll"]
