# Changelog

Todos los cambios notables de MediaFy se documentan aquí.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y el proyecto usa [Versionado Semántico](https://semver.org/lang/es/).

## [1.9.0] - 2026-07-26

### Añadido
- **Mini-reproductor rediseñado (liquid glass):** tarjeta de vidrio esmerilado con la
  carátula del álbum difuminada de fondo, y **barra de progreso/timeline de la canción**
  (clic o arrastre para saltar) con tiempos transcurrido/total. Reemplaza el medidor de
  volumen (VU) que antes ocupaba ese espacio.
- **Cola de reproducción + auto-siguiente:** al reproducir una canción de la Biblioteca se
  arma la cola con toda la carpeta y la reproducción avanza sola a la siguiente. Botones
  anterior/siguiente en la barra de reproducción y en las teclas multimedia (SMTC).
- **Fundido entre canciones (crossfade tipo transición):** la canción que sale baja el
  volumen y la que entra sube ("entrada épica"), sin superponer audio. Configurable en
  Ajustes → "Fundido entre canciones" (0–12 s; 0 = desactivado).

## [1.8.2] - 2026-07-26

> **Actualización obligatoria.** Corrige un fallo del actualizador integrado que hacía
> crashear la app y el instalador al aplicar una actualización.

### Corregido
- **Crash del actualizador integrado:** al pulsar "Instalar ahora", la app y el instalador
  se cerraban abruptamente. Causa: el instalador se lanzaba como proceso hijo de MediaFy y
  el cierre por árbol (`taskkill /T` y `Kill(entireProcessTree)`) lo mataba a sí mismo.
  - **App:** el instalador ahora se lanza desacoplado del árbol de procesos de MediaFy.
  - **Instalador:** cierra la app y sus auxiliares (yt-dlp, ffmpeg, deno) **por nombre**,
    nunca por árbol, para no matarse a sí mismo — así incluso las instalaciones lanzadas
    por versiones anteriores (1.8.1) se actualizan sin crashear.
- El instalador ahora **relanza MediaFy automáticamente** tras una actualización silenciosa.

## [1.8.1] - 2026-07-25

> **Actualización obligatoria.** Incluye el motor necesario para que YouTube funcione sin
> instalar nada más; reemplaza a la 1.8.0, que dependía de tener Node.js en el equipo.

### Añadido
- **Motor de YouTube incluido (deno):** MediaFy ahora empaqueta su propio runtime de
  JavaScript. Las descargas y la reproducción funcionan de fábrica, sin exigir Node.js
  instalado en el equipo (la 1.8.0 lo requería).
- **Actualizaciones obligatorias:** una versión marcada como obligatoria (marcador
  `[obligatoria]` en las notas del release) se muestra como un aviso que no se puede
  descartar, para asegurar que todos los usuarios reciban correcciones críticas.

### Cambiado
- La sección Ajustes → "Cuenta de YouTube" y el aviso de novedades ahora indican que el
  motor viene incluido, en lugar de pedir instalar Node.js.

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

[1.9.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.9.0
[1.8.2]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.8.2
[1.8.1]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.8.1
[1.8.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.8.0
[1.7.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.7.0
