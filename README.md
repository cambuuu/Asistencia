# Bot de Asistencia Discord (.NET 8)

Bot diseñado para marcar asistencia automáticamente (Entrada/Salida) mediante botones en Discord, integrado con URLs externas.

## Requisitos Previo
1.  Tener instalado **.NET 8 SDK**.
2.  Tener un **Bot de Discord** creado en el [Developer Portal](https://discord.com/developers/applications).
    -   Requiere permisos de Bot ("Message Content Intent" habilitado).

## Configuración

1.  Abre el archivo `appsettings.json`.
2.  Busca la línea `"Token": "PON_TU_TOKEN_AQUI"`.
3.  Reemplaza `"PON_TU_TOKEN_AQUI"` por el token real de tu bot.
4.  *(Opcional)* Verifica que el `TargetChannelId` sea correcto (`1455671434474160360`).

Las URLs de marcado ya están configuradas:
-   **Entrada**: `...sentido=1...`
-   **Salida**: `...sentido=0...`

## Ejecución

### Opción A: Desde consola (Desarrollo)
En la carpeta del proyecto:
```powershell
dotnet run
```
El bot iniciará, se conectará a Discord y quedará esperando los horarios (08:29 y 18:00).

### Opción B: Publicar (Uso real)
Para dejarlo corriendo sin tener la consola abierta, puedes publicarlo:
```powershell
dotnet publish -c Release -o ./publish
```
Luego ejecuta el archivo `.exe` en la carpeta `publish`.

## Funcionamiento
1.  **Entrada**: A las 08:29 AM enviará un mensaje con botón verde "MARCAR ENTRADA".
2.  **Salida**: A las 18:00 PM enviará un mensaje con botón azul "MARCAR SALIDA".
3.  **Feriados**: El bot consulta automáticamente la API de feriados de Chile. Si es feriado, **NO** enviará mensaje.
4.  **Botón**: Al presionar, el bot hace la petición web internamente. Si sale bien, responde "✅ Entrada marcada".

## Pruebas
Si quieres probar que los botones funcionan sin esperar a la hora:
1.  Puedes cambiar temporalmente la hora en `Worker.cs` o la hora de tu PC.
2.  O agregar un comando temporal en `DiscordService` para forzar el mensaje.
