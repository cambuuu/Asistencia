# Bot de Asistencia Discord (.NET 8)

Bot diseñado para marcar asistencia (Entrada/Salida) mediante botones en Discord, integrado con el portal de **Buk** (`https://silva.buk.cl`).

Al presionar el botón, el bot inicia sesión en Buk (login en dos pasos: correo → contraseña), lee el formulario `#web-marking-form` del portal y envía el mismo POST que dispara el botón "Entrada"/"Salida" de la página:

```
POST /employee_portal/web_marking/marcaje?sentido=ENTRADA|SALIDA
```

La sesión se reutiliza entre marcajes y se renueva automáticamente si caduca.

## Requisitos Previo
1.  Tener instalado **.NET 8 SDK**.
2.  Tener un **Bot de Discord** creado en el [Developer Portal](https://discord.com/developers/applications).
    -   Requiere permisos de Bot ("Message Content Intent" habilitado).

## Configuración

Copia `appsettings.template.json` a `appsettings.json` y complétalo:

```json
{
  "Discord": { "Token": "...", "TargetChannelId": 1455671434474160360 },
  "Buk": {
    "BaseUrl": "https://silva.buk.cl",
    "Email": "tu.correo@silva.cl",
    "Password": "...",
    "Latitude": "",
    "Longitude": ""
  }
}
```

`appsettings.json` está en `.gitignore` y `.dockerignore`: **nunca** se commitea ni entra a la imagen Docker.

`Latitude`/`Longitude` son opcionales — el portal permite marcar sin geolocalización.

### Verificar la configuración sin marcar

```powershell
dotnet run -- --test-buk
```

Hace login y busca el formulario de marcaje, **sin** registrar entrada ni salida.

### Despliegue en Fly.io

Las credenciales van como secrets (.NET los lee con `__` como separador de sección):

```bash
fly secrets set Discord__Token=... Buk__Email=tu.correo@silva.cl Buk__Password=...
```

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
4.  **Botón**: Al presionar, el bot inicia sesión en Buk y envía el marcaje. El resultado llega por Mensaje Directo con la respuesta del portal.

## Pruebas
Si quieres probar que los botones funcionan sin esperar a la hora:
1.  Puedes cambiar temporalmente la hora en `Worker.cs` o la hora de tu PC.
2.  O agregar un comando temporal en `DiscordService` para forzar el mensaje.
