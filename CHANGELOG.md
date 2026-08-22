# Changelog

Todos los cambios notables de MediaFy se documentan aquí.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/)
y el proyecto usa [Versionado Semántico](https://semver.org/lang/es/).

## [Sin publicar]

### Corregido
- **El fundido se cortaba con la app en segundo plano:** la envolvente de volumen dependía del
  temporizador de la interfaz, que Windows ralentiza cuando la ventana no está visible, así que
  al cambiar de canción el volumen podía quedarse a medias. Ahora corre por su cuenta,
  independiente de la ventana.

### Cambiado (rendimiento)
- **Se acabaron los tirones al usar la app.** El trabajo pesado ya no ocurre en el hilo de la
  interfaz: la configuración se guarda de forma agrupada y en segundo plano (antes escribía en
  disco en cada cambio, así que arrastrar un control provocaba decenas de escrituras), la
  Biblioteca recorre el disco en segundo plano (antes congelaba la ventana al entrar) y el
  diagnóstico de arranque ya no retrasa el inicio.
- **MediaFy deja de estorbar al resto del equipo.** yt-dlp, ffmpeg y el motor de separación por IA
  se ejecutan con prioridad reducida: siguen aprovechando toda la CPU libre, pero ceden el paso a
  la aplicación que estés usando.
- **Descargas más fluidas:** el progreso se refresca a un ritmo constante en vez de una vez por
  cada fragmento descargado, que saturaba la interfaz con varias descargas a la vez.
- Menos trabajo desperdiciado: el reproductor solo actualiza lo que está visible, las miniaturas
  se cargan al tamaño en que se muestran (mucha menos memoria) y el monitor de GPU consulta con
  menos frecuencia.

## [2.0.0] - 2026-07-30

### Añadido
- **Letras sincronizadas (karaoke) — vista inmersiva:** una pantalla dedicada con la carátula del
  álbum difuminada de fondo, la línea actual **rellenándose de izquierda a derecha al ritmo**
  (barrido karaoke), transiciones suaves entre líneas (escala + iluminación) y scroll centrado
  animado. Ajustes de **tamaño de fuente** y **alineación**. Para identificar la canción lee las
  **etiquetas ID3** del archivo (artista/título/álbum/duración) —así acierta incluso en álbumes con
  pistas numeradas "07…"— y usa el match exacto de lrclib (gratis), con fallback a búsqueda. Se
  refresca sola al cambiar de canción.
- **Estado del sistema (diagnóstico integrado):** un semáforo en la barra superior (siempre
  visible) y una tarjeta en Inicio comprueban dependencias (yt-dlp, ffmpeg, motor JS), cookies,
  conexión y carpeta de descargas — y hacen una **prueba real de extracción de YouTube** (1×/día
  y bajo demanda) que detecta rupturas como el cambio anti-bot antes de que fallen las descargas.
  Cada problema ofrece su acción de arreglo.
- **Vista de cola:** un panel en la barra de reproducción muestra la cola actual; la canción que
  suena queda resaltada, clic para saltar, ✕ para quitar y arrastrar para reordenar.

### Corregido
- **Comprobación de actualizaciones más robusta:** ya no se consulta GitHub en cada arranque
  (agotaba el límite de la API pública → error `403 rate limit exceeded`). Ahora se comprueba
  como máximo una vez cada 8 horas, los fallos del chequeo automático se silencian, y el botón
  manual "Buscar" muestra un mensaje claro con el tiempo de reintento cuando hay límite temporal.

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

[2.0.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v2.0.0
[1.9.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.9.0
[1.8.2]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.8.2
[1.8.1]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.8.1
[1.8.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.8.0
[1.7.0]: https://github.com/cgus392-cmd/MediaFy/releases/tag/v1.7.0
