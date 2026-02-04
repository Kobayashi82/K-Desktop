<div align="center">

![Desktop](https://img.shields.io/badge/Desktop-brown?style=for-the-badge)
![Windows](https://img.shields.io/badge/Windows-Application-blue?style=for-the-badge)
![Automation](https://img.shields.io/badge/Tool-Automation-green?style=for-the-badge)
![VB.NET](https://img.shields.io/badge/Language-VB.NET-red?style=for-the-badge)

*A productivity and automation suite for Windows*

</div>

<div align="center">
  <img src="/images/K-Desktop.png">
</div>


# K-Desktop

[README en Español](README_es.md)

## 🎯 Description

**K-Desktop** is a productivity and automation suite for Windows built in Visual Basic .NET.

Designed as a centralized tool, K-Desktop provides a complete set of features to boost user productivity, including screenshot capture, audio/video recording, customizable quick commands with global hotkeys, and the powerful **QuickMenu** system for instant access to apps and actions.

## ✨ Key Features

- **Quick Commands**: Customizable commands with global hotkeys for instant actions.
- **QuickMenu**: Custom context menus with quick access to apps, folders, web pages, and system actions.
- **Screenshot Capture**: Advanced screenshot tool with an integrated editor.
- **Audio Recorder**: Audio recording with multiple output formats and device selection.
- **Video Recorder**: Screen video capture with integrated post-processing editor.
- **Achievements System**: Gamified usage with unlockable achievements and usage stats.
- **Virtual Keyboards (K-Board)**: Create custom keyboard layouts for different apps.
- **Notifications**: Customizable notification system with visual and sound alerts.
- **Multi-Monitor**: Full support for multi-display setups.
- **Automation**: Auto-run commands on system start/exit or based on active processes.
- **Import/Export**: Backup and sync configuration system.

## 🚀 Core Features

### Quick Commands
- Create custom commands with global hotkeys
- Execute apps, BAT scripts, and system actions
- Sequential execution of multiple actions
- Keystroke lock to avoid duplicate execution
- Hidden commands and background execution
- CPU priority and window mode configuration (normal, minimized, maximized, hidden)
- Custom sounds on command execution
- Run as administrator
- Custom parameters and startup paths

### QuickMenu

**QuickMenu** is one of the most useful components of K-Desktop. It allows creating custom context menus that appear with global hotkeys, providing instant access to:

- **Applications**: Launch your favorite programs with one click
- **Folders**: Quick access to system directories
- **Web pages**: Open websites directly from the menu
- **System commands**: Run predefined actions
- **Nested menus**: Organize items into submenus
- **Custom icons**: Personalize each item’s appearance
- **Visual separators**: Group items for readability
- **Visual editor**: Create, edit, and reorder items with drag and drop
- **Multiple QuickMenus**: Different menus for different contexts or tasks
- **Contextual activation**: Shows different QuickMenus based on the active app

#### QuickMenu advantages:
- Instant access to frequently used apps without searching the Start menu
- Personalized organization for your workflow
- Reduced clicks and navigation time
- Intuitive, highly customizable visual interface
- Configuration persistence across sessions

### Screenshot Capture
- Full-screen or area capture
- Monitor selector for multi-display systems
- Screen identifier for easier selection
- Integrated editor with annotation tools
- Add text, shapes, and arrows to captures
- Undo/redo system (Ctrl+Z / Ctrl+Y)
- Multiple output formats (PNG, JPG, BMP)
- Auto-save with customizable names
- Integration with the system image editor

### Audio & Video Recording
- Audio recording with input device selection
- Screen video recording (full screen or selected area)
- Integrated video editor for post-processing
- Multiple codecs and formats
- Quality and bitrate control
- Real-time preview
- Recording specific screens in multi-monitor setups

### Virtual Keyboard System (K-Board)
- Create custom keyboard layouts
- Auto-activation per active process
- Per-app key remapping
- Support for complex key combinations
- Programmable keyboard macros
- Saveable and switchable profiles

### Achievements System
- Unlock achievements based on app usage
- Detailed usage statistics tracking:
  - Commands created, deleted, executed
  - Actions created, deleted, executed
  - KBoards created, deleted, executed
  - QuickMenus created, deleted, opened
  - Macros recorded and executed
  - Captures and recordings performed
  - And much more...
- Historical stats visualization

### Automation
- Run commands on Windows startup
- Run commands on logout
- Run commands based on active processes
- Detect application windows and titles
- Task scheduling via Windows Task Scheduler
- Auto-disable commands after a specified time

## 📋 Available Actions

K-Desktop includes multiple action types:

### Applications
- Run Application
- Open Folder
- Web Page
- Run Commands (multiple sequential actions)
- Run BAT (batch scripts)

### Multimedia
- Capture Screen
- Record Screen (On/Off)
- Record Audio (On/Off)
- Play Sound
- Change Audio Output
- Adjust System Volume

### Utilities
- Macros (record and playback key sequences)
- K-Board (activate virtual keyboards)
- QuickMenu (show custom menus)
- Ad-Hoc Network (create temporary wireless networks)

### System Actions
- Send Keystroke (simulate keys)
- TopMost (pin window always on top)
- Show/Hide Hidden Files
- Disable in... (disable K-Desktop in the specified app)
- Show Message (custom notifications)
- Shutdown PC (shutdown, restart, sleep, hibernate)
- Wait (pauses between actions)

## 🛠️ Included Tools

### System Management
- `FMenu` - Main menu and control center
- `FComandoRapido` - Quick command configurator
- `FDebug` - Debugging and diagnostics tools
- `FFirstRun` - First-run window

### Capture & Recording
- `FCapturarPantalla` - Advanced screenshot tool with editor
- `FAudioRecorder` - Multi-track audio recorder
- `FVideoRecorder` - Screen recorder with area selector
- `FVideoRecorderEditor` - Integrated video editor

### Configuration
- `FSelectDisplay` - Multi-monitor selector
- `FIdentificarPantalla` - Screen identifier
- `FIconDialog` - Icon selector for customization
- `FNombreIcono` - Rename or change QuickMenu items
- `FRenombrar` - Rename command

### Others
- `FLogros` - Achievements and stats viewer
- `FAyuda` - Help system with tutorials
- `FImportar` - Configuration import/export
- `FKLink` - Quick links manager (hardlinks)
- `FSerial` - Serial-based activation system
- `FMsgBox` - Custom dialog boxes
- `FrmNotification` - Pop-up notification system

### Auxiliary Component
- `KPS.exe` - Auxiliary app launcher for K-Desktop

## 💻 System Requirements

- **Operating System**: Windows 7 or later (Windows 10/11 recommended)
- **.NET Framework**: 4.7.2 or later
- **Architecture**: x64
- **RAM**: 2 GB minimum (4 GB recommended)
- **Disk space**: 50 MB

## 📦 Installation and Usage

### Build

To build from the command line:

```cmd
msbuild KDesktop.sln /p:Configuration=Release /p:Platform=x64
```

Or open `KDesktop.sln` in Visual Studio and build from there.

### Run

To run K-Desktop:

```cmd
.\bin\x64\Release\KDesktop.exe
```

### QuickMenu usage

1. **Create a QuickMenu**: 
   - Open the main K-Desktop menu
   - Go to the "QuickMenu" section
   - Create a new menu

2. **Add items**:
   - Right click in the QuickMenu area
   - Select "New Item" or "New Separator"
   - Configure the item with name, icon, and action

3. **Organize items**:
   - Drag and drop items to reorder
   - Create submenus for better organization
   - Use separators to group related items

4. **Activate QuickMenu**:
   - Create a command with the **QuickMenu** action
   - Select the created menu
   - Assign a hotkey for the command
   - Press the hotkey anywhere
   - The menu appears at the cursor position
   - Select the desired item

## ⚙️ Configuration

K-Desktop stores its configuration in the Windows Registry and in **%AppData%\Roaming\KDesktop**.
From the "Options" section you can import or export the configuration.

## 🎯 Remote Commands (UDP)

K-Desktop includes support for remote control via UDP, allowing commands to be executed from other devices on the local network.

---

## 📄 License

This project is licensed under the WTFPL – [Do What the Fuck You Want to Public License](http://www.wtfpl.net/about/).

---

<div align="center">

**🖥️ Developed by Kobayashi82 🖥️**

*"Automate your desktop, boost your productivity"*

</div>

<div align="center">
  <img src="/images/Comandos.jpg" width="45%">
  <img src="/images/Opciones.jpg" width="45%">
</div>

<div align="center">
  <img src="/images/Ayuda.jpg" width="45%">
  <img src="/images/Logros.jpg" width="45%">
</div>

<div align="center">
  <img src="/images/K-Board.jpg" width="45%">
  <img src="/images/QuickMenu.png" width="45%">
</div>
