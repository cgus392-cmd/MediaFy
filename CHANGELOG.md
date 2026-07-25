# Changelog

Todos los cambios notables de MediaFy se documentan aquí.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y el proyecto usa [Versionado Semántico](https://semver.org/lang/es/).

## [1.8.0] - 2026-07-25

### Añadido
- **Reproductor en vivo (streaming):** ahora puedes escuchar cualquier resultado de
  búsqueda directamente desde YouTube, sin descargarlo. Botón de reproducción en cada
  resultado de la pestaña Descargas; suena al instante en el reproductor global, con
  controles del sistema (SMTC) y mini-reproductor.
- **Autenticación de YouTube (cookies + Node.js):** nueva sección en Ajustes → "Cuenta de
  YouTube" para importar un `cookies.txt` de tu navegador. Detecta automáticamente Node.js
  como motor de JavaScript. Incluye guía paso a paso para exportar las cookies.
- **Anuncio de novedades por versión:** al abrir la app tras actualizar, un aviso resume
  los cambios de la versión. En esta versión explica el requisito de YouTube y muestra el
  estado real de autenticación (cookies y Node) del equipo.

### Corregido
- **Descargas y reproducción de YouTube rotas por el anti-bot (2025+):** YouTube empezó a
  exigir sesión iniciada ("confirma que no eres un robot") y un runtime de JavaScript para
  la mayoría de videos, lo que hacía fallar ~90% de las descargas. Se resuelve pasando las
  cookies del usuario y usando Node.js para resolver el reto `nsig`. Aplica a todas las
  operaciones: información, búsqueda, descarga, streaming y descargas vía Spotify.

### Notas
- La autenticación requiere **Node.js instalado** en el equipo. Para distribución a otros
  usuarios se evaluará empaquetar un runtime de JavaScript (deno) en el instalador.
- Las cookies de YouTube caducan periódicamente; vuelve a importarlas si aparecen errores
  de sesión. Se guardan únicamente en tu equipo.

## [1.7.0] - 2026-07

### Añadido
- Motor de separación de pistas por IA (stems) validado y funcionando: voz, instrumental,
  batería y bajo, con modelos MDX-Net / Demucs / BS-Roformer en la GPU local.
- Mezclador de stems, monitor de CPU/GPU en vivo, progreso en línea y desinstalación del motor.

[1.8.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.8.0
[1.7.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.7.0
