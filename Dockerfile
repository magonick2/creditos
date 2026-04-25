# 1. Etapa de compilación (SDK)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copiar archivos de proyecto y restaurar dependencias
COPY *.csproj .
RUN dotnet restore

# Copiar el resto del código y publicar
COPY . .
RUN dotnet publish -c Release -o /app

# 2. Etapa de ejecución (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copiar los archivos publicados desde la etapa anterior
COPY --from=build /app .

# Configurar variables de entorno para Render
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

# El nombre del .dll debe ser el mismo que tu proyecto
ENTRYPOINT ["dotnet", "PlataformaCreditos.dll"]