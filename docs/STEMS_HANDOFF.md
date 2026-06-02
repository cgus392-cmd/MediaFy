# 🤝 Handoff para Gemini — Función de Stems (separación de pistas con IA)

> **Para Gemini:** Lee TODO este documento antes de tocar código. Contiene el contexto completo del proyecto, el estado actual, el plan exacto de las fases que faltan y los "gotchas" que te ahorrarán horas. El usuario (Camilo, "CG") está muy entusiasmado con esta función. Trabaja con honestidad técnica, por fases, y haz commits frecuentes.

---

## 0) Quién eres y cómo trabajar

- Eres un asistente de programación continuando el desarrollo de **MediaFy by CG**.
- El usuario habla por voz (puede haber typos, la intención siempre es clara). Llámalo **"bro"**.
- Le gusta: trabajo por **fases** ("paso 1, paso 2"), **commits/checkpoints frecuentes**, honestidad técnica (di la verdad sobre lo que funciona y lo que no), y estética **WinUI 3 / Fluent / Windows 11** (nunca WPF genérico).
- Confirma con él antes de pasos grandes o irreversibles (publicar releases, etc.).

---

## 1) Resumen del proyecto

**MediaFy by CG** — app de escritorio Windows (gestor/descargador de medios multiplataforma) bajo la marca personal **CG LABS** (dev: Camilo G., cgus392@gmail.com).

- **Repo:** https://github.com/cgus392-cmd/MediaFy (público, rama `main`)
- **Ruta local:** `C:\Users\camil\Documents\New project\YTDownloader\`
- **Proyecto activo:** `YTDownloaderWinUI/` (WinUI 3). El `AssemblyName` es **MediaFy** (exe = `MediaFy.exe`).
- **Stack:** C# .NET 8, WinUI 3 (Windows App SDK 1.6), CommunityToolkit.Mvvm, Newtonsoft.Json. Self-contained (`SelfContained=true` + `WindowsAppSDKSelfContained=true`, RID `win-x64`).
- **Herramientas bundled** en `Assets/`: `yt-dlp.exe`, `ffmpeg.exe`.
- **Versión actual: 1.6.0** (último release publicado).

### ⚙️ Compilar (CRÍTICO)
**Usa VS MSBuild, NO `dotnet build`** (WinUI necesita las tareas AppxPackage de VS):
```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
& $msbuild "C:\Users\camil\Documents\New project\YTDownloader\YTDownloaderWinUI\YTDownloaderWinUI.csproj" /t:Build /p:Configuration=Debug /p:Platform=x64 /v:minimal /nologo
```
- Mata `MediaFy.exe` antes de compilar (bloquea el .exe): `Get-Process -Name MediaFy -ErrorAction SilentlyContinue | Stop-Process -Force`
- Tras limpiar `obj/`, el PRIMER build puede fallar con CS0103 (codegen XAML) → compila otra vez.
- exe de salida: `YTDownloaderWinUI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\MediaFy.exe`

### 📦 Release (cuando el usuario lo pida)
1. Sube versión en 3 sitios: `YTDownloaderWinUI.csproj` (`<Version>`,`<FileVersion>`,`<AssemblyVersion>`), `installer\MediaFy.iss` (`#define MyAppVersion`), `Views\AboutPage.xaml` (texto "Versión X").
2. Compila en **Release** (`/p:Configuration=Release`). El instalador empaqueta `bin\x64\Release\...\win-x64`.
3. Instalador: `& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" "installer\MediaFy.iss"` → sale en `installer\dist\MediaFy-Setup-<ver>.exe`.
4. Portable: `Compress-Archive "bin\x64\Release\...\win-x64\*" "installer\dist\MediaFy-<ver>-win-x64.zip"`.
5. Release GitHub: `& "C:\Program Files\GitHub CLI\gh.exe" release create v<ver> <setup.exe> <portable.zip> --repo cgus392-cmd/MediaFy --title "MediaFy by CG v<ver>" --notes-file <notas.md> --latest`
   - `gh` ya está autenticado como cgus392-cmd. El push a stderr de git/gh es normal (no es error).
- El instalador (`MediaFy.iss`) ya tiene un `[Code] PrepareToInstall` que hace `taskkill /F /T /IM MediaFy.exe` antes de copiar (arregla updates fallidos).

---

## 2) GOTCHAS críticos (te ahorran horas)

- **Glifos:** las herramientas de edición ESTRIPEAN caracteres glifo pegados. Usa `char.ConvertFromUtf32(0xXXXX)` en C# y `&#xXXXX;` en XAML. NUNCA pegues el glifo literal.
- **`x:Bind` en `DataTemplate` dentro de una `Window` (no Page):** da error CS1503 "MainWindow → FrameworkElement", sobre todo con `Converter={StaticResource ...}`. Solución usada: usar `{Binding}` clásico y exponer propiedades ya-listas (p. ej. `AppTask.BarVisibility` devuelve `Visibility`). Mira `MainWindow.xaml` (flyout de notificaciones).
- **`Path` ambiguo** (`Microsoft.UI.Xaml.Shapes.Path` vs `System.IO.Path`): añade `using Path = System.IO.Path;`.
- **XAML codegen NO soporta propiedades `init`** → usa `{ get; set; }`.
- **Procesos:** al separar/instalar lanza subprocesos; al salir, `MainWindow.ExitApp()` ya hace `Process.GetCurrentProcess().Kill(entireProcessTree:true)` para no dejar fantasmas. Asegúrate de cancelar/matar subprocesos hijos en cancelación.
- **Sandbox de PowerShell:** a veces marca falsos positivos ("Remove-Item on system path") en scripts con divisiones (`/2`). Reintenta, pasa.
- **NavigationCacheMode.Required** en páginas: restaura selección de ComboBox en `Loaded`, no en el constructor.
- **No bloquees el hilo de UI** con subprocesos (`Process.Start`/`WaitForExit`): usa `Task.Run`. (Ya optimizamos esto en Settings con `yt-dlp --version`.)

---

## 3) Estado actual de la función Stems (v1.6.0 = FASE 1 hecha)

La función vive en una sección **"Experimental"** (beta) de la barra de navegación (icono de matraz).

**Archivos ya creados (Fase 1):**
- `Views/ExperimentalPage.xaml` / `.cs` — UI: badge BETA, tarjeta de hardware, selector de archivo, `RadioButtons x:Name="StemsMode"` (índice 0 = 2 stems, 1 = 4 stems), tarjeta de expectativas, e `InfoBar x:Name="EngineBar"` con botón "Ver detalles e instalar".
  - `BtnPickFile_Click` → guarda `_file` (ruta).
  - `BtnInstallEngine_Click` → muestra ContentDialog con expectativas (size/licencia según GPU/CPU). **AQUÍ va a engancharse la instalación real.**
  - `BtnSeparate_Click` → hoy solo registra una tarea fallida "motor no instalado". **AQUÍ va a engancharse la separación real.**
- `Core/HardwareInfo.cs` — `HasNvidiaGpu` (existe `System32\nvidia-smi.exe`), `NvidiaName()`, `TotalRamGb()` (GlobalMemoryStatusEx), `Recommend()` → `(bool useGpu, string label, string detail)`.
- `Core/NotificationCenter.cs` — centro de notificaciones global:
  - `AppTask` (ObservableObject): `Title`, `Status`, `Progress` (0-100), `Indeterminate`, `State` (Running/Done/Error), `Glyph`, `BarVisibility`. Métodos: `Report(pct, status?)`, `Done(status)`, `Fail(status)`.
  - `NotificationCenter.Start(title, glyph)` → crea y registra una `AppTask` (llamar desde hilo UI). `Tasks` (ObservableCollection), `ActiveCount`, `Changed` event, `Clear()`.
  - UI: campana + `muxc:InfoBadge` en `MainWindow.xaml` (AppTitleBar) con flyout que lista `Tasks`. `MainWindow` ya hace `NotifList.ItemsSource = NotificationCenter.Tasks` y `RefreshNotifBadge`.

**Cómo reportar progreso a la campana (patrón a usar en Fase 2):**
```csharp
var task = Core.NotificationCenter.Start("Separando: cancion.mp3", char.ConvertFromUtf32(0xEC4F));
// ... durante el proceso, en el hilo UI (DispatcherQueue.TryEnqueue):
task.Report(45, "Procesando voz… 45%");
// al terminar:
task.Done("Listo: 4 pistas en la Biblioteca");
// si falla:
task.Fail("Error al separar");
```

---

## 4) Decisión de motor (ya investigada y elegida)

**Motor recomendado: `audio-separator`** (paquete `python-audio-separator` de nomadkaraoke, el "UVR headless").
- `pip install "audio-separator[gpu]"` (CUDA) o `"[cpu]"`.
- Auto-descarga los modelos en el primer uso.
- **CLI** (subproceso, igual que yt-dlp): `audio-separator "ruta/cancion.mp3" --model_filename <modelo> --output_dir <carpeta>`.
- **2 stems** (voz/instrumental) → modelo MDX-Net (rápido, onnxruntime). **4 stems** (voz/batería/bajo/otros) → modelo **Demucs** (`htdemucs`).
- GPU: CUDA / DirectML / CPU fallback. Modelos open-source (MIT) → encaja con el descargo de responsabilidad de la app.
- Repo: https://github.com/nomadkaraoke/python-audio-separator · PyPI: https://pypi.org/project/audio-separator/

**El reto honesto:** necesita Python + PyTorch. Como no podemos empaquetar ~1-3 GB, se hace **descarga bajo demanda**: bajar un **Python portable** (recomendado: `python-build-standalone`, ~30 MB) y `pip install audio-separator` dentro de ese entorno. La instalación es **pesada (~1 GB CPU / 2-3 GB GPU) y delicada** (pip, ruedas de torch, etc.) — probablemente requiera **2-3 rondas de ajuste**. El usuario YA lo sabe y aceptó. No prometas que funciona a la primera; prueba de verdad end-to-end.

> Alternativa futura (v2, NO ahora): ONNX Runtime + DirectML nativo en .NET (sin Python, modelo MDX ~60 MB) pero implica bastante DSP (STFT/iSTFT) a mano.

---

## 5) FASE 2 — el motor real (lo que sigue YA)

Crear **`Core/StemService.cs`** con:

1. **`bool IsEngineInstalled()`** — comprueba que existe el entorno (p. ej. carpeta `%LOCALAPPDATA%\MediaFy\stem-engine\python\python.exe` + marcador de que `audio-separator` está instalado).

2. **`Task InstallEngineAsync(IProgress<(double pct,string msg)>, CancellationToken)`**:
   - Carpeta destino: `%LOCALAPPDATA%\MediaFy\stem-engine\`.
   - Descarga **python-build-standalone** (CPython portable para Windows x64) → extrae.
   - `pip install` (con el python portable) `audio-separator[gpu]` si `HardwareInfo.HasNvidiaGpu`, si no `[cpu]`. (El `[gpu]` instala torch+CUDA; valida que el equipo tenga CUDA; si falla, cae a `[cpu]`.)
   - Reporta progreso a `NotificationCenter` (descarga, extracción, pip install).
   - Maneja errores con mensajes claros (sin red, pip falló, etc.).

3. **`Task<List<string>> SeparateAsync(string inputFile, int stems /*2 o 4*/, IProgress<...>, CancellationToken)`**:
   - Ejecuta el `audio-separator` del entorno portable como **subproceso** con `--output_dir` a una carpeta temporal o directo a la carpeta de la Biblioteca.
   - 2 stems → modelo MDX (p. ej. `UVR-MDX-NET-Inst_HQ_3` o el recomendado por defecto). 4 stems → `htdemucs`.
   - Parsea la salida para progreso (o muestra indeterminado si no hay %).
   - **Salida → Biblioteca como ÁLBUM**: crea carpeta `"<OutputFolder>\Stems de <nombre cancion>\"` con los stems (`Voz.wav`, `Musica.wav`, o `Voz/Bateria/Bajo/Otros`). ¡Esto reusa la feature de álbumes acordeón que ya existe en `LibraryPage`! Así el usuario ve y reproduce los stems en la Biblioteca.
   - Cancelación: matar el árbol de procesos del subproceso.

4. **Enganchar la UI** en `Views/ExperimentalPage.xaml.cs`:
   - `BtnInstallEngine_Click` → si `!IsEngineInstalled()`, lanzar `InstallEngineAsync` con una `AppTask` en el `NotificationCenter`. Al terminar, ocultar el `InfoBar EngineBar` y habilitar separar.
   - `BtnSeparate_Click` → si motor instalado y hay `_file`: leer `StemsMode.SelectedIndex` (0→2 stems, 1→4 stems), lanzar `SeparateAsync` con `AppTask` de progreso; al terminar, refrescar la Biblioteca / avisar.
   - Mostrar estado del motor (instalado / no) al entrar a la página (`Loaded`).

**Sugerencia de UX:** todo el trabajo pesado en background (`Task.Run`), progreso siempre vía `NotificationCenter` + `DispatcherQueue.TryEnqueue` para tocar UI.

---

## 6) FASE 3 — resultado pro (después de que la separación funcione)

- Mostrar cada stem con su **forma de onda** + control de **volumen** + **solo/silenciar** + **exportar**.
- El usuario quiere las ondas **estilo Grabadora de Windows** (barras verticales finas y simétricas, limpias). Si es muy costoso, usar el estilo que ya genera el editor (`Core/FfmpegService.cs` tiene `showwavespic` de ffmpeg). Hay un `EditorPage` con onda ya implementada que puedes mirar de referencia.
- Reproducción por stem: reusar `App.Playback` (PlaybackService, basado en MediaPlayer) o un mezclador simple.

---

## 7) Estilo/identidad y descargo

- Marca **CG LABS**. Logo de marca en `Assets/brand/` (cglabs_black/white). Icono de carpeta-álbum 3D en `Assets/brand/folder_album.png`.
- Hay un **descargo de responsabilidad** (Términos) bilingüe: `Core/LegalText.cs` + `TermsDialog.cs`, aceptación en primer arranque (`AppSettings.TermsAccepted`). La separación es uso responsable/personal — encaja.
- Roadmap pendiente además de Stems: buscador en la Biblioteca, cola de descargas persistente, más iconografía 3D.

---

## 8) Checklist de arranque para Gemini

1. Lee este doc completo. ✅
2. Abre la solución, compila en Debug (verifica que arranca, ve a la sección **Experimental**).
3. Crea `Core/StemService.cs` (Fase 2, sección 5).
4. Engancha `ExperimentalPage` (install + separate) con progreso en `NotificationCenter`.
5. **Prueba de verdad** la instalación y una separación real (acepta que puede requerir ajustes).
6. Cuando funcione, salida de stems a la Biblioteca como álbum.
7. Commit frecuente. Cuando el usuario lo pida, sube versión y publica release (sección 1).

¡Suerte! El usuario está muy ilusionado con esto. Hazlo sólido. 🎚️🔥

— Dejado por el asistente anterior (Claude), v1.6.0.
