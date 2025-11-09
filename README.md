<div align="center">

![Desktop](https://img.shields.io/badge/Desktop-brown?style=for-the-badge)
![Windows](https://img.shields.io/badge/Windows-Application-blue?style=for-the-badge)
![Productivity](https://img.shields.io/badge/Tool-Productivity-green?style=for-the-badge)
![Visual Basic](https://img.shields.io/badge/Language-Visual%20Basic-red?style=for-the-badge)

*Una suite de productividad y automatización para Windows*

</div>

# K-Desktop 4.0

## 🎯 Descripción
K-Desktop 4.0 es una suite de productividad y automatización para Windows desarrollada en Visual Basic .NET.

Diseñado como una herramienta centralizada, K-Desktop proporciona un conjunto completo de funcionalidades para mejorar la productividad del usuario, incluyendo captura de pantalla, grabación de audio/video, comandos rápidos personalizables, y un sistema avanzado de automatización mediante hotkeys.

## Características

- **Comandos Rápidos**: Sistema de comandos personalizables con hotkeys globales para ejecutar acciones de forma rápida.
- **Captura de Pantalla**: Herramienta avanzada para capturar y gestionar capturas de pantalla.
- **Grabador de Audio**: Grabación de audio con múltiples formatos de salida.
- **Grabador de Video**: Captura de video de pantalla con editor integrado.
- **Sistema de Logros**: Gamificación del uso del software con logros desbloqueables.
- **Teclados Virtuales (KBoard)**: Creación de layouts de teclado personalizados para diferentes aplicaciones.
- **Notificaciones**: Sistema de notificaciones personalizable.
- **Multi-Monitor**: Soporte completo para configuraciones de múltiples pantallas.
- **Automatización**: Ejecución automática de comandos al iniciar/salir del sistema.
- **Importación/Exportación**: Sistema de respaldo y sincronización de configuraciones.

## Funcionalidades Principales

### Comandos Rápidos
- Creación de comandos personalizados con hotkeys
- Ejecución de aplicaciones, scripts y acciones del sistema
- Soporte para ejecución secuencial de múltiples acciones
- Bloqueo de pulsaciones para evitar ejecuciones duplicadas
- Comandos ocultos y ejecutables en segundo plano

### Captura de Pantalla
- Captura de pantalla completa o áreas específicas
- Selector de monitor en sistemas multi-pantalla
- Identificador de pantallas para facilitar la selección
- Múltiples formatos de salida

### Grabación de Audio y Video
- Grabación de audio con selección de dispositivo
- Grabación de video de pantalla completa o áreas específicas
- Editor de video integrado para post-procesamiento
- Soporte para múltiples códecs y formatos

### Sistema de Teclados Virtuales (KBoard)
- Creación de layouts de teclado personalizados
- Activación automática según proceso activo
- Reasignación de teclas por aplicación
- Soporte para combinaciones de teclas complejas

### Sistema de Logros
- Desbloqueo de logros según uso de la aplicación
- Seguimiento de estadísticas de uso
- Sistema de progreso y recompensas

### Automatización
- Ejecución de comandos al iniciar Windows
- Ejecución de comandos al cerrar sesión
- Ejecución de comandos basada en procesos activos
- Detección de ventanas y títulos de aplicaciones

## Herramientas Incluidas

### Gestión de Sistema
- `FMenu` - Menú principal y centro de control.
- `FComandoRapido` - Configurador de comandos rápidos.
- `FDebug` - Herramientas de depuración y diagnóstico.
- `FFirstRun` - Asistente de configuración inicial.

### Captura y Grabación
- `FCapturarPantalla` - Capturador de pantalla avanzado.
- `FAudioRecorder` - Grabador de audio.
- `FVideoRecorder` - Grabador de video.
- `FVideoRecorderEditor` - Editor de video integrado.

### Configuración
- `FSelectDisplay` - Selector de pantalla para multi-monitor.
- `FIdentificarPantalla` - Identificador de pantallas.
- `FIconDialog` - Selector de iconos para personalización.
- `FNombreIcono` - Renombrador de iconos.
- `FRenombrar` - Herramienta de renombrado.

### Otros
- `FLogros` - Visualizador de logros.
- `FAyuda` - Sistema de ayuda integrado.
- `FImportar` - Importador/Exportador de configuraciones.
- `FKLink` - Gestor de enlaces rápidos.
- `FSerial` - Sistema de activación.
- `FMsgBox` - Cuadros de diálogo personalizados.
- `FrmNotification` - Sistema de notificaciones.

## Requisitos del Sistema

- **Sistema Operativo**: Windows 7 o superior (recomendado Windows 10/11)
- **.NET Framework**: 4.7.2 o superior
- **Arquitectura**: x64
- **Memoria RAM**: 2 GB mínimo (4 GB recomendado)
- **Espacio en disco**: 50 MB para la aplicación + espacio para grabaciones

## Instalación y Uso

Para compilar el proyecto:

```cmd
msbuild KDesktop.sln /p:Configuration=Release
```

O abrir el archivo `KDesktop.sln` en Visual Studio y compilar desde allí.

Para ejecutar:

```cmd
.\bin\Release\KDesktop.exe
```

## Configuración

K-Desktop almacena su configuración en formato XML, permitiendo respaldos y sincronización fácil entre diferentes equipos.

### Archivos de Configuración
- Comandos rápidos y hotkeys
- Configuración de teclados virtuales (KBoard)
- Preferencias de grabación
- Sistema de logros y estadísticas

## Tecnologías Utilizadas

- **Visual Basic .NET** - Lenguaje principal
- **Windows Forms** - Framework de interfaz gráfica
- **NAudio** - Procesamiento y grabación de audio
- **Windows API** - Integración profunda con el sistema operativo

## Futuras Mejoras

- Soporte para temas personalizables
- Integración con servicios en la nube
- Modo portable sin instalación
- Soporte para plugins de terceros
- Expansión del sistema de logros

---

K-Desktop 4.0 es una herramienta de productividad en constante evolución, diseñada para simplificar y automatizar tareas cotidianas en Windows.

## 📄 Licencia

Este proyecto está desarrollado por Kobayashi Corp.

Copyright © 2021

---

<div align="center">

**🖥️ Desarrollado por Kobayashi Corp. 🖥️**

*"Automatiza tu escritorio, potencia tu productividad"*

</div>
