# Usar la imagen base de .NET 8 SDK para compilar
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar csproj y restaurar dependencias
COPY ["DiscordAsistenciaBot.csproj", "./"]
RUN dotnet restore "DiscordAsistenciaBot.csproj"

# Copiar el resto del código y compilar versión Release
COPY . .
RUN dotnet publish "DiscordAsistenciaBot.csproj" -c Release -o /app/publish

# Usar imagen de Runtime para ejecutar (más liviana)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Iniciar el bot
ENTRYPOINT ["dotnet", "DiscordAsistenciaBot.dll"]
