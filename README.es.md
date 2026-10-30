# DeskIsleBar

[English](README.md) | **Español**

Una pequeña isla flotante para Windows que te muestra lo importante sin estorbar: la música que suena, tus notificaciones, el calendario, el clima y el estado de la PC, en una píldora negra que se expande cuando pasa algo y se achica cuando termina.

**Sitio:** [facughub.github.io/DeskIsleBar](https://facughub.github.io/DeskIsleBar/)

<p align="center"><img src="docs/capturas/demo.gif" alt="DeskIsleBar: de la píldora en reposo a la música, el calendario, el clima y la PC, y llega una notificación" width="560"></p>

| | |
|---|---|
| ![Música](docs/capturas/musica.png) | ![Inicio](docs/capturas/inicio.png) |
| ![Calendario](docs/capturas/calendario.png) | ![PC](docs/capturas/pc.png) |
| ![Una notificación](docs/capturas/aviso-notificacion.png) | ![Un recordatorio](docs/capturas/aviso-recordatorio.png) |

*Las capturas están en inglés, pero la app también está en español.*

## Qué hace

- **Música**: portada, título, controles y progreso de la app que esté sonando (Tidal, Spotify, YouTube Music, Apple Music o cualquiera que use los controles multimedia de Windows). La rueda de volumen mueve el volumen propio de la app, no el de todo el sistema (si la música suena en el navegador, como YouTube Music, mueve el general).
- **Notificaciones de apps**: las notificaciones de tus apps aparecen en la isla, con su ícono. Un clic abre la app (o el chat, si la app lo permite, como WhatsApp Web). Con *No molestar* prendido en Windows, la isla puede reemplazar los carteles de Windows (o respetar el *No molestar*, como prefieras).
- **Calendario**: próximos eventos de tus calendarios de Google u Outlook (links iCal privados), con vista de mes navegable.
- **Clima**: el clima actual y la máxima y mínima del día en tu ciudad (Open-Meteo).
- **PC**: uso de CPU, GPU y RAM, tiempo encendida, y una herramienta para "liberar espacio" de temporales viejos y la papelera.
- **Notas rápidas**, **temporizadores** y **Pomodoro**, con un modo foco opcional: mientras estudiás, las notificaciones esperan al descanso.
- **Avisos**: cada uno se prende o apaga en *Opciones → Avisos*:
  - batería baja de controles y dispositivos Bluetooth (de cualquier marca que la informe), con el nombre del dispositivo;
  - controles, auriculares/parlantes y pendrives conectados o desconectados;
  - recordatorio de eventos 10 minutos antes;
  - cámara o micrófono en uso, y qué app los usa;
  - batería de la notebook (cargando, con batería, baja);
  - Bloq Mayús activado/desactivado, internet caída / volvió, imagen copiada (Win+Shift+S).
- **Indicadores de privacidad**: mientras una app usa la cámara o el micrófono, la isla muestra un punto verde o naranja junto a la hora (y Bloq Mayús, una teclita a la izquierda); el borde destella cuando empiezan. En el primer panel, la línea *En uso* dice qué app usa cada cosa. Se apagan en *Opciones → Avisos*.
- **No molesta en los juegos**: se oculta mientras jugás en pantalla completa; con un video a pantalla completa solo aparece para las notificaciones. Los avisos críticos (como el joystick a punto de quedarse sin batería) aparecen igual.
- **Liviana**: unos 60 MB de RAM y muy por debajo del 1% de CPU.
- **En español e inglés**: sigue el idioma de Windows automáticamente, o elegilo en las opciones.

## Cómo se usa

- **La primera vez** te muestra una bienvenida corta que enseña todo esto practicando. La volvés a ver con el **?** de *Opciones*.
- **Abrirla:** pasá el mouse por encima. Se abre en la música (si algo suena) o en Inicio, y se cierra cuando sacás el mouse.
- **Cambiar de panel:** arrastrá hacia la izquierda o la derecha sobre la isla (también funciona encima de los botones). Los puntitos de abajo muestran dónde estás (Música/Inicio → Calendario → Clima → PC → Notas → Accesos) y con un clic vas directo a ese panel. También aparecen flechitas ‹ › al acercar el mouse a un costado.
- **Moverla:** arrastrá la isla a cualquier parte de la pantalla. Recuerda el lugar y crece hacia el lado libre (desde abajo crece hacia arriba, desde un costado hacia el centro). *Centrar*, en las opciones, la devuelve arriba.
- **Volumen:** girá la rueda del mouse sobre la isla. Si suena música cambia el volumen de esa app; si no, el del sistema.
- **Música:** clic en la portada para traer la app al frente; arrastrá la barra de progreso para saltar a otra parte del tema.
- **Temporizadores:** en *Accesos*: Pomodoro (25 + 5), 45 + 10, 5 y 15 minutos, o *A medida* (elegís los minutos con los botones o la rueda). La cuenta regresiva queda en la isla mientras corre.
- **Calendario:** clic en un día para ver sus eventos; clic en un evento para abrir ese día en Google Calendar u Outlook.
- **Opciones:** el engranaje en *Accesos*. La flecha de arriba a la izquierda vuelve atrás.

## Requisitos

- Windows 10 versión 2004 (compilación 19041) o posterior, o Windows 11, de 64 bits. Está probada en Windows 11.
- Nada más: el instalador trae todo lo que necesita (.NET incluido).

## Instalación

<!-- TODO: link de la Microsoft Store, cuando esté publicada -->

Descargá el instalador desde el [sitio](https://facughub.github.io/DeskIsleBar/) o desde [Releases](../../releases) (`DeskIsleBar-Setup-<versión>.exe`) y abrilo. Se instala solo para tu usuario (sin permisos de administrador), agrega DeskIsleBar al menú Inicio y, si querés, la hace arrancar con Windows. Eso lo podés cambiar después en las opciones de la isla.

Para actualizar no hace falta bajar nada: cuando hay una versión nueva, la isla te avisa y se actualiza con un clic, conservando tu configuración (también la podés buscar con el botón de *Opciones*).

## Preguntas frecuentes

**¿DeskIsleBar recolecta mis datos?**
No. No tiene telemetría, ni estadísticas de uso, ni cuentas, ni un servidor propio. DeskIsleBar solo se conecta a internet para tres cosas:
- **Clima:** manda el nombre de la ciudad que escribiste (y después sus coordenadas) a [Open-Meteo](https://open-meteo.com/), un servicio de clima gratuito, sin ningún dato que te identifique.
- **Calendario:** descarga los links iCal que agregás *vos*, directamente de Google o de Outlook.
- **Actualizaciones:** al iniciar, una vez por día y cuando la buscás desde *Opciones*, le pregunta a GitHub cuál es la última versión publicada (sin mandar datos tuyos).

Nada más sale de tu PC. Más detalle en la [política de privacidad](https://facughub.github.io/DeskIsleBar/privacidad.html).

**¿Lee mis notificaciones?**
Sí, para mostrarlas en la isla: localmente y en solo lectura. Nunca salen de tu PC y su contenido no se escribe en ningún registro. Lo podés apagar en las opciones cuando quieras.

**¿Qué guarda y dónde?**
Tu configuración, las notas y la posición de la isla quedan en `%LOCALAPPDATA%\DeskIsleBar`. Los links de calendario funcionan como contraseñas, así que se guardan cifrados con DPAPI de Windows: solo tu usuario de Windows los puede leer. Un pequeño registro técnico (`deskislebar.log`, en esa misma carpeta) anota eventos y errores, como qué app mandó una notificación. Nunca anota el contenido de las notificaciones ni de los mensajes que mandan tus scripts con `--aviso`, ni tus links de calendario.

**¿Necesita permisos de administrador?**
No. Todo funciona con tu usuario, incluido *Arrancar con Windows*.

**¿Me va a molestar mientras juego?**
No. Se oculta en los juegos a pantalla completa y guarda las notificaciones comunes en una cola hasta que salís. Solo los avisos críticos (batería del joystick a punto de agotarse, fin del temporizador, errores de hardware) aparecen encima de los juegos en ventana sin bordes. Los juegos en pantalla completa *exclusiva* no permiten que nada se dibuje encima.

**¿Qué borra "Liberar espacio"?**
Solo los archivos de más de un día en tu carpeta de temporales (`%TEMP%`), salteando lo que esté en uso, y vacía la papelera. Siempre te muestra lo que encontró y te pregunta antes de borrar.

**¿Por qué Windows SmartScreen me avisa al abrirla?**
La descarga de GitHub todavía no está firmada digitalmente, así que Windows no la reconoce. Podés elegir *Más información → Ejecutar de todas formas*. La versión de la Microsoft Store no muestra este aviso.

**¿Se le pueden mandar mensajes desde scripts?**
Sí: `DeskIsleBar.exe --aviso "Título" "Detalle" [--icono info|alerta|candado|joystick|campana|red] [--color naranja|rojo|verde|azul] [--fijo]` muestra un mensaje en la isla que ya está abierta (`--fijo` lo deja hasta que lo toques).

**¿Cómo la desinstalo?**
Como cualquier programa: *Configuración → Aplicaciones → Aplicaciones instaladas → DeskIsleBar → Desinstalar*. Cierra la isla si está abierta, saca *Arrancar con Windows* y te pregunta si querés borrar también tu configuración, notas y calendarios vinculados (`%LOCALAPPDATA%\DeskIsleBar`).

## ¿Encontraste un error? ¿Tenés una idea?

- **Errores y sugerencias:** abrí un [issue](../../issues) contando qué pasó y qué esperabas. Ayuda mucho adjuntar el informe de diagnóstico: corré `DeskIsleBar.exe --diagnostico` y se abre un archivo de texto con qué funciones de Windows pudo leer la isla en tu PC (no manda nada a ningún lado).
- **Preguntas e ideas:** [Discussions](../../discussions).
- **Temas privados** (por ejemplo, un problema de seguridad): escribí a [facughub.apps@gmail.com](mailto:facughub.apps@gmail.com).
- **Soporte para más dispositivos:** la batería de marcas que solo la informan por su propio software (Corsair, Logitech, Razer, SteelSeries…) se puede sumar como integración opcional. Si tenés uno de esos dispositivos y querés ayudar, abrí un issue o un pull request: la integración tiene que activarse solo si está instalado el software de la marca, y necesita a alguien con el equipo para probarla.

## Apoyar el proyecto

DeskIsleBar es gratis. Si te resulta útil y querés apoyar su desarrollo:

- ☕ [Invitame un cafecito](https://cafecito.app/facughub) (en pesos, con Mercado Pago o transferencia)

## Compilar desde el código

```
dotnet build DeskIsleBar.csproj -c Release
```

Necesita el SDK de .NET 10. El ejecutable queda en `bin\Release\net10.0-windows10.0.19041.0\`.

## Licencia

[GPL-3.0](LICENSE). Podés usar, estudiar, modificar y compartir DeskIsleBar; las versiones modificadas que distribuyas tienen que seguir siendo de código abierto, con la misma licencia.
