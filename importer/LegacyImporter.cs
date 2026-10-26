using System.Globalization;
using KDesktop.Core;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Microsoft.Win32;

namespace KDesktop.Importer;

/// <summary>
/// Imports commands, QuickMenus and options from K-Desktop 4 (HKCU\Software\KDesktop4). It lived in K-Desktop until
/// 5.0.263; now it is this program of its own, run once by whoever still has K-Desktop 4.
/// </summary>
public static class LegacyImporter
{
    private const string RegRoot = @"Software\KDesktop4";
    private const string Sep = "|-|";

    private static readonly string LegacyDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KDesktop 4.0");

    public sealed class Report
    {
        public int Commands { get; set; }
        public int Actions { get; set; }
        public int QuickMenus { get; set; }
        public int MenuItems { get; set; }
        public int Icons { get; set; }
        public List<string> Skipped { get; } = [];

        public override string ToString() =>
            $"{Commands} comandos, {Actions} acciones, {QuickMenus} QuickMenus ({MenuItems} elementos), {Icons} iconos" +
            (Skipped.Count > 0 ? $"; {Skipped.Count} acciones no soportadas" : "");
    }

    public static bool IsAvailable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegRoot + @"\Comandos");
        return key is not null && key.SubKeyCount > 0;
    }

    public static (AppConfig Config, Report Report) Import()
    {
        AppPaths.EnsureCreated();
        var report = new Report();
        var config = new AppConfig();

        using var root = Registry.CurrentUser.OpenSubKey(RegRoot) ?? throw new InvalidOperationException("No se encontraron datos de K-Desktop 4.");

        ImportOptions(root, config.Settings);
        ImportCommands(root, config, report);
        ImportQuickMenus(root, config, report);
        ResolveReferences(config);

        Log.Info($"Importación de K-Desktop 4: {report}");
        foreach (var s in report.Skipped) Log.Info("  · omitido: " + s);
        return (config, report);
    }

    // ── Options ──────────────────────────────────────────────────────────

    private static void ImportOptions(RegistryKey root, AppSettings s)
    {
        using var o = root.OpenSubKey("Opciones");
        if (o is null) return;

        s.StartWithWindows = Bool(o, "IniciarWindows", true);
        s.ShowTrayIcon = Bool(o, "IconoSistema", true);
        s.BlockSideButtons = Bool(o, "DisableXButton", true);
        s.UdpScope = Bool(o, "UDP", true) ? KDesktop.Core.Remote.RemoteScope.LocalNetwork : KDesktop.Core.Remote.RemoteScope.ThisPc;
        if (int.TryParse(Str(o, "UDPPort"), out var port) && port >= 1000) s.UdpPort = port;
        s.StartupMessage = NullIfEmpty(Str(o, "MensajeIniciar_Texto"));
        s.StartupSound = Sound(Str(o, "MensajeIniciar_Sonido"));

        using (var d = o.OpenSubKey("Desactivar"))
            if (d is not null)
                foreach (var name in d.GetValueNames().Where(n => n.Trim().Length > 0))
                    s.DisabledIn.Add(name.Trim().ToLowerInvariant());

        using (var b = o.OpenSubKey("DeshabilitarBackFoward"))
            if (b is not null)
            {
                s.BlockSideButtonsIn.Clear();
                foreach (var name in b.GetValueNames().Where(n => n.Trim().Length > 0))
                    s.BlockSideButtonsIn.Add(name.Trim().ToLowerInvariant() switch { "iexplorer" => "iexplore", var x => x });
            }
    }

    // ── Commands ─────────────────────────────────────────────────────────

    private static void ImportCommands(RegistryKey root, AppConfig config, Report report)
    {
        using var cmds = root.OpenSubKey("Comandos");
        if (cmds is null) return;

        var startup = new HashSet<string>();
        var exit = new HashSet<string>();
        foreach (var keyName in cmds.GetSubKeyNames())
        {
            using var k = cmds.OpenSubKey(keyName);
            if (k is null) continue;
            var name = Str(k, "Nombre").Trim();
            if (name.Length == 0) continue;

            var parts = keyName.Split(Sep);
            var cmd = new Command
            {
                Id = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : Ids.New(),
                Name = name,
                Sound = Sound(Str(k, "Sonido")),
                Remote = Bool(k, "Remote"),
                IgnoreDisabledApps = Bool(k, "EjecutarSiempre"),
                Enabled = !Bool(k, "Deshabilitar"),
                BlockKey = Bool(k, "BloquearPulsacion"),
                Hidden = Bool(k, "Oculto"),
            };

            foreach (var hk in Str(k, "Hotkey").Split(Sep).Select(Hotkey.Normalize).Where(h => h.Length > 0).Distinct(Hotkey.Comparer))
                cmd.Hotkeys.Add(hk);
            foreach (var p in Str(k, "Procesos").Split(Sep).Select(p => p.Trim()).Where(p => p.Length > 0))
                cmd.Processes.Add(p.ToLowerInvariant());

            if (Str(k, "Icono") == "[CUSTOM]")
                cmd.Icon = CopyIcon(Path.Combine(LegacyDataDir, "Iconos", "Comandos", cmd.Id + ".ico"), "cmd-" + cmd.Id, report);

            using var acts = k.OpenSubKey("Acciones");
            if (acts is not null)
            {
                var list = new List<(int Index, string Type, string Settings)>();
                foreach (var an in acts.GetSubKeyNames())
                {
                    using var a = acts.OpenSubKey(an);
                    if (a is null) continue;
                    list.Add((int.TryParse(Str(a, "Indice"), out var i) ? i : int.MaxValue, Str(a, "Tipo"), Str(a, "Settings")));
                }
                foreach (var (_, type, settings) in list.OrderBy(x => x.Index))
                {
                    var action = ConvertAction(type, settings, out var note);
                    if (action is null)
                    {
                        report.Skipped.Add($"{name}: {type}");
                        cmd.Notes = string.IsNullOrEmpty(cmd.Notes) ? $"Acción no disponible en K-Desktop 5: {type}" : cmd.Notes + $"; {type}";
                        continue;
                    }
                    if (note is not null) cmd.Notes = note;
                    cmd.Actions.Add(action);
                    report.Actions++;
                }
            }

            if (Bool(k, "EjecutarIniciar")) startup.Add(cmd.Id);
            if (Bool(k, "EjecutarSalir")) exit.Add(cmd.Id);
            config.Commands.Add(cmd);
            report.Commands++;
        }

        var sorted = config.Commands.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        config.Commands.Clear();
        foreach (var c in sorted) config.Commands.Add(c);
        AddStartupEvents(config, startup, exit);
    }

    /// <summary>«EjecutarIniciar» / «EjecutarSalir» of K-Desktop 4 become the «Al iniciar / Al salir de K-Desktop» events.</summary>
    private static void AddStartupEvents(AppConfig config, IReadOnlySet<string> startup, IReadOnlySet<string> exit)
    {
        var onStart = config.Commands.Where(c => startup.Contains(c.Id)).ToList();
        var onExit = config.Commands.Where(c => exit.Contains(c.Id)).ToList();
        if (onStart.Count > 0) config.Events.Add(AppConfig.MigratedEvent(Loc.T("event.migrated.startup"), new AppStartTrigger(), onStart));
        if (onExit.Count > 0) config.Events.Add(AppConfig.MigratedEvent(Loc.T("event.migrated.exit"), new AppExitTrigger(), onExit));
    }

    private static CommandAction? ConvertAction(string type, string settings, out string? note)
    {
        note = null;
        var s = settings.Split(Sep);
        string At(int i) => i < s.Length ? s[i] : string.Empty;
        bool B(int i) => bool.TryParse(At(i), out var v) && v;

        switch (type.Trim().ToLowerInvariant())
        {
            case "ejecutar aplicacion":
                return new RunAppAction
                {
                    Path = At(0),
                    Arguments = At(1),
                    WorkingDirectory = At(2),
                    WindowMode = Window(At(3)),
                    Priority = At(4).Replace(" ", "").ToLowerInvariant() switch
                    {
                        "low" => ProcessPriorityLevel.Idle,
                        "belownormal" => ProcessPriorityLevel.BelowNormal,
                        "abovenormal" => ProcessPriorityLevel.AboveNormal,
                        "high" => ProcessPriorityLevel.High,
                        "realtime" => ProcessPriorityLevel.RealTime,
                        _ => ProcessPriorityLevel.Normal,
                    },
                    TopMost = B(5),
                    RunAsAdmin = B(6),
                };

            case "abrir carpeta":
            {
                var special = SpecialFolderOf(settings);
                return special is not null ? new OpenFolderAction { Special = special } : new OpenFolderAction { Path = settings };
            }

            case "pagina web":
                return new OpenUrlAction
                {
                    Url = At(0),
                    Browser = BrowserOf(At(1)) ?? string.Empty,
                    NewWindow = B(2),
                    Incognito = B(3),
                };

            case "aplicaciones":
                return At(0) switch
                {
                    "Cerrar Aplicacion" => new AppControlAction { Mode = AppControlMode.Close, ProcessName = At(1), Force = B(2) },
                    "Esperar Aplicacion" => new AppControlAction
                    {
                        Mode = AppControlMode.WaitFor,
                        ProcessName = At(1),
                        WaitFor = At(3) switch { "Activar" => WaitCondition.Activated, "Cerrar" => WaitCondition.Closed, _ => WaitCondition.Opened },
                    },
                    _ => new AppControlAction { Mode = AppControlMode.Activate, ProcessName = At(1), Force = B(2) },
                };

            case "ejecutar bat":
            {
                var path = At(1);
                var script = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                return new RunScriptAction { Shell = ScriptShell.Cmd, Script = script, WindowMode = Window(At(2)), RunAsAdmin = B(3) };
            }

            case "reproducir sonido":
                return new PlaySoundAction { Sound = Sound(At(0)) ?? string.Empty, Wait = B(1) };

            case "salida de audio":
            {
                // Sound|OSD|font|colour|device id|device id…
                var known = KDesktop.Core.Platform.AudioDevices.Playback();
                var action = new AudioOutputAction { Sound = Sound(At(0)), ShowOsd = B(1) };
                foreach (var id in s.Skip(4).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct())
                    action.Devices.Add(new AudioDeviceRef { Id = id, Name = known.FirstOrDefault(d => d.Id == id)?.Name ?? id });
                return action;
            }

            case "volumen":
                return At(0) switch
                {
                    "Establecer Volumen" => new VolumeAction { Mode = VolumeMode.Set, Value = Math.Clamp(int.TryParse(At(1), out var v) ? v : 0, 0, 100) },
                    "Subir Volumen" => new VolumeAction { Mode = VolumeMode.Up, Value = 0 },
                    "Bajar Volumen" => new VolumeAction { Mode = VolumeMode.Down, Value = 0 },
                    _ => new VolumeAction { Mode = VolumeMode.ToggleMute, Value = 0 },
                };

            case "enviar pulsación" or "enviar pulsacion":
                return At(0) switch
                {
                    "Enviar Texto" => new SendInputAction { Mode = SendInputMode.Text, Value = At(1) },
                    "Mover Ratón" => new SendInputAction { Mode = SendInputMode.MouseMoveTo, Value = At(1).Replace('-', ',') },
                    "Desplazar Ratón" => new SendInputAction { Mode = SendInputMode.MouseMoveBy, Value = At(1).Replace('-', ',') },
                    _ => new SendInputAction { Mode = SendInputMode.Keys, Value = Hotkey.Normalize(At(1)) },
                };

            case "topmost (on/off)": return new TopMostAction { ShowOsd = B(1) };
            case "archivos ocultos (on/off)": return new HiddenFilesAction { ShowOsd = B(1) };
            case "desactivar en (on/off)": return new ToggleDisabledInAction { ShowOsd = B(1) };

            case "mostrar mensaje":
                return new ShowMessageAction { Text = At(0), Seconds = Seconds(At(1), At(2), 3), Countdown = B(3), Wait = B(4) };

            case "apagar equipo":
                return new PowerAction
                {
                    Mode = At(0) switch
                    {
                        "Reiniciar Equipo" => PowerMode.Restart,
                        "Cerrar Sesion" => PowerMode.LogOff,
                        "Cambiar Usuario" => PowerMode.SwitchUser,
                        _ => PowerMode.Shutdown,
                    },
                    DelaySeconds = (int)Seconds(At(2), At(3), 0),
                    ShowOsd = B(5),
                };

            case "quickmenu":
                return new OpenQuickMenuAction { MenuId = At(0), MenuName = At(0) };

            case "comandos":
                return new CommandControlAction
                {
                    Mode = At(0) switch
                    {
                        "Habilitar Comando" => CommandControlMode.Enable,
                        "Deshabilitar Comando" => CommandControlMode.Disable,
                        "Comando (On/Off)" => CommandControlMode.Toggle,
                        _ => CommandControlMode.Run,
                    },
                    CommandId = At(1),
                };

            case "esperar":
                return new WaitAction { Seconds = Seconds(At(0), At(1), 1), Countdown = B(2), Message = At(6) };

            default:
                return null;
        }
    }

    // ── QuickMenus ───────────────────────────────────────────────────────

    private static void ImportQuickMenus(RegistryKey root, AppConfig config, Report report)
    {
        using var menus = root.OpenSubKey("QuickMenus");
        if (menus is null) return;

        foreach (var keyName in menus.GetSubKeyNames())
        {
            using var k = menus.OpenSubKey(keyName);
            if (k is null) continue;
            var menu = new QuickMenu
            {
                Name = NullIfEmpty(Str(k, "Nombre")) ?? keyName,
                Size = int.TryParse(Str(k, "Tamaño"), out var size) ? Math.Clamp(size, 0, 5) : 1,
            };
            foreach (var item in ReadItems(k, report)) menu.Items.Add(item);
            config.QuickMenus.Add(menu);
            report.QuickMenus++;
        }
    }

    private static List<QuickMenuItem> ReadItems(RegistryKey parent, Report report)
    {
        var raw = new List<(int Index, QuickMenuItem Item)>();
        foreach (var keyName in parent.GetSubKeyNames())
        {
            using var k = parent.OpenSubKey(keyName);
            if (k is null) continue;
            var index = int.TryParse(Str(k, "Indice"), out var i) ? i : int.MaxValue;
            var command = Str(k, "Comando");
            var label = Str(k, "Nombre");
            var id = NullIfEmpty(Str(k, "ID")) ?? Ids.New();

            QuickMenuItem item;
            if (command == "[SEPARADOR]")
            {
                item = new QuickMenuItem { Id = id, Kind = QuickMenuItemKind.Separator };
            }
            else
            {
                var kind = command switch
                {
                    "[TITULO]" => QuickMenuItemKind.Title,
                    "" => QuickMenuItemKind.Submenu,
                    _ => QuickMenuItemKind.Command,
                };
                item = new QuickMenuItem
                {
                    Id = id,
                    Kind = kind,
                    CommandId = kind == QuickMenuItemKind.Command ? command : null,
                    Label = label is "[COMANDO]" or "" ? null : label.Replace("&&", "&"),
                };
                var icon = Str(k, "Icono");
                if (icon.Length > 0 && !icon.StartsWith('['))
                    item.Icon = CopyIcon(icon, "qm-" + id, report);
                foreach (var child in ReadItems(k, report)) item.Children.Add(child);
            }
            raw.Add((index, item));
            report.MenuItems++;
        }
        return raw.OrderBy(r => r.Index).Select(r => r.Item).ToList();
    }

    // ── Post-processing ──────────────────────────────────────────────────

    private static void ResolveReferences(AppConfig config)
    {
        foreach (var action in config.Commands.SelectMany(c => c.Actions))
        {
            switch (action)
            {
                case OpenQuickMenuAction q when config.FindMenu(q.MenuId) is { } menu:
                    q.MenuId = menu.Id;
                    q.MenuName = menu.Name;
                    break;
                case CommandControlAction cc when config.FindCommand(cc.CommandId) is { } target:
                    cc.CommandName = target.Name;
                    break;
            }
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string? CopyIcon(string source, string baseName, Report report)
    {
        try
        {
            if (!File.Exists(source)) return null;
            var file = baseName + Path.GetExtension(source).ToLowerInvariant();
            File.Copy(source, Path.Combine(AppPaths.IconsDir, file), true);
            report.Icons++;
            return file;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo copiar el icono {source}: {ex.Message}");
            return null;
        }
    }

    private static WindowMode Window(string mode) => mode switch
    {
        "Minimizado" => WindowMode.Minimized,
        "Maximizado" => WindowMode.Maximized,
        "Oculto" => WindowMode.Hidden,
        _ => WindowMode.Normal,
    };

    private static double Seconds(string value, string unit, double fallback)
    {
        if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return fallback;
        return unit == "min" ? v * 60 : v;
    }

    /// <summary>K-Desktop 4 sound names map 1:1 to the bundled .wav files (except "Sin Sonido").</summary>
    private static string? Sound(string name)
    {
        name = name.Replace("NoSonar", "").Trim();
        if (name.Length == 0 || name.Equals("Sin Sonido", StringComparison.OrdinalIgnoreCase)) return null;
        return name == "Harware Insert" ? "Hardware Insert" : name;
    }

    /// <summary>The special folders of «Abrir carpeta», by the Spanish names K-Desktop 4 stored.</summary>
    private static string? SpecialFolderOf(string legacy) => legacy.Trim().ToLowerInvariant() switch
    {
        "equipo" => "ThisPC",
        "panel de control" => "ControlPanel",
        "control de volumen" => "VolumeMixer",
        "ajustes de audio" => "SoundSettings",
        "papelera de reciclaje" => "RecycleBin",
        "papelera (vaciar)" => "EmptyRecycleBin",
        "modo 'god'" => "GodMode",
        "documentos" => "Documents",
        "imagenes" => "Pictures",
        "musicas" => "Music",
        "escritorio" => "Desktop",
        "videos" => "Videos",
        "descargas" => "Downloads",
        "k-desktop" or "k-temp" => "KDesktopData",
        "usuario" => "UserProfile",
        "fuentes" => "Fonts",
        "menu inicio (usuario)" => "StartMenu",
        "menu inicio" => "CommonStartMenu",
        "directivas de grupo" => "GroupPolicy",
        "programador de tareas" => "TaskScheduler",
        "registro de windows" => "Registry",
        "teclado en pantalla" => "OnScreenKeyboard",
        "conexiones de red" => "NetworkConnections",
        "task switcher" => "TaskView",
        "powershell" => "PowerShell",
        "windows apps" => "WindowsApps",
        _ => null,
    };

    private static string? BrowserOf(string name) => name.Trim().ToLowerInvariant() switch
    {
        "google chrome" => "chrome",
        "mozilla firefox" => "firefox",
        "microsoft edge" => "edge",
        "brave" => "brave",
        "opera" => "opera",
        "vivaldi" => "vivaldi",
        _ => null,
    };

    private static string Str(RegistryKey k, string name) => k.GetValue(name)?.ToString() ?? string.Empty;

    private static bool Bool(RegistryKey k, string name, bool fallback = false) =>
        bool.TryParse(Str(k, name), out var v) ? v : fallback;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
