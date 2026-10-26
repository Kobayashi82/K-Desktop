using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using KDesktop.Core;
using KDesktop.Core.Localization;
using KDesktop.Core.Storage;
using KDesktop.Importer;

// K-Desktop 4 → K-Desktop 5: reads what K-Desktop 4 keeps in the registry (HKCU\Software\KDesktop4) and its icons, and
// writes it as K-Desktop 5's configuration. Both must be closed (K-Desktop 5 would save its own over it). Spanish only:
// K-Desktop 4 only ever was.
//   KDesktop.Importer.exe [--data <carpeta>] [--si]

Console.OutputEncoding = Encoding.UTF8;
var yes = args.Any(a => a is "--si" or "--yes" or "-y");
var dataIndex = Array.FindIndex(args, a => a.Equals("--data", StringComparison.OrdinalIgnoreCase));
if (dataIndex >= 0 && dataIndex + 1 < args.Length) AppPaths.UseDataDir(args[dataIndex + 1]);

// The K-Desktop 5 that was open, to open it again at the end (the importer never guesses where it is installed).
string? kdesktop5 = null;

Title("Importar K-Desktop 4 en K-Desktop 5");
try
{
    return Run();
}
catch (Exception ex)
{
    Say(ConsoleColor.Red, "No se pudo importar: " + ex.Message);
    Log.Error("Importador de K-Desktop 4", ex);
    End();
    return 1;
}

int Run()
{
    if (!LegacyImporter.IsAvailable())
    {
        Say(ConsoleColor.Yellow, "No hay datos de K-Desktop 4 en este usuario de Windows: no hay nada que importar.");
        End();
        return 1;
    }

    // K-Desktop 4 answers the same hotkeys: it has to go.
    var old = Instances(v => v < 5);
    if (old.Count > 0)
    {
        Say(ConsoleColor.Yellow, "K-Desktop 4 está abierto. Respondería a los mismos atajos que K-Desktop 5.");
        if (Ask("¿Cerrarlo ahora?", true))
            foreach (var p in old)
            {
                try { p.Process.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* already gone */ }
            }
    }

    // K-Desktop 5 would save its configuration over the imported one: closed first, opened again at the end. (Into
    // another data folder, --data, the one running is not the one written.)
    var running = dataIndex < 0 ? Instances(v => v >= 5) : [];
    if (running.Count > 0)
    {
        Say(ConsoleColor.Yellow, "K-Desktop 5 está abierto y tiene que cerrarse para importar (se vuelve a abrir al acabar).");
        if (!Ask("¿Cerrarlo ahora?", true)) return Cancelled();
        kdesktop5 = running.Select(r => r.Path).FirstOrDefault(p => p is not null);
        if (kdesktop5 is not null) Process.Start(new ProcessStartInfo(kdesktop5, "--exit") { UseShellExecute = true })?.Dispose();
        var until = DateTime.Now.AddSeconds(20);
        while (Instances(v => v >= 5).Count > 0 && DateTime.Now < until) Thread.Sleep(300);
        if (Instances(v => v >= 5).Count > 0)
        {
            Say(ConsoleColor.Red, "K-Desktop 5 sigue abierto. Ciérralo (botón derecho en su icono › Salir) y vuelve a abrir el importador.");
            End();
            return 1;
        }
    }

    var exists = File.Exists(AppPaths.ConfigFile);
    Console.WriteLine($"Datos de K-Desktop 5: {AppPaths.DataDir}");
    if (exists)
    {
        Say(ConsoleColor.Yellow, "Ya hay una configuración de K-Desktop 5: se reemplazarán sus comandos, QuickMenus y opciones por los de K-Desktop 4.");
        Console.WriteLine("Antes se guarda una copia en la carpeta Backups.");
        if (!Ask("¿Seguir?", false)) return Reopen(Cancelled());
    }

    Loc.Instance.Load(null);
    using var store = new ConfigStore();
    store.Load();
    var theme = store.Config.Settings.Theme;
    if (exists)
    {
        Directory.CreateDirectory(AppPaths.BackupDir);
        var copy = Path.Combine(AppPaths.BackupDir, $"config.antes-de-importar.{DateTime.Now:yyyy-MM-dd HH.mm.ss}.json");
        File.Copy(AppPaths.ConfigFile, copy, overwrite: true);
        Console.WriteLine($"Copia: {copy}");
    }

    var (config, report) = LegacyImporter.Import();
    if (exists) config.Settings.Theme = theme;
    store.Replace(config);
    store.Flush();

    Console.WriteLine();
    Say(ConsoleColor.Green, "Importado:");
    Console.WriteLine($"  {report.Commands} comandos con {report.Actions} acciones");
    Console.WriteLine($"  {report.QuickMenus} QuickMenus con {report.MenuItems} elementos");
    Console.WriteLine($"  {report.Icons} iconos");
    if (report.Skipped.Count > 0)
    {
        Say(ConsoleColor.Yellow, $"  {report.Skipped.Count} acciones que ya no existen en K-Desktop 5 (anotadas en las notas de su comando):");
        foreach (var skipped in report.Skipped) Console.WriteLine("    · " + skipped);
    }
    Console.WriteLine();
    Console.WriteLine("Si K-Desktop 4 se inicia con Windows, desinstálalo o quítalo del inicio: ya no hace falta.");
    return Reopen(0);
}

int Reopen(int code)
{
    if (kdesktop5 is not null && Ask("¿Abrir K-Desktop 5?", true))
        Process.Start(new ProcessStartInfo(kdesktop5, "--show") { UseShellExecute = true })?.Dispose();
    End();
    return code;
}

int Cancelled()
{
    Say(ConsoleColor.Yellow, "No se ha importado nada.");
    End();
    return 2;
}

bool Ask(string question, bool byDefault)
{
    if (yes) return true;
    Console.Write($"{question} [{(byDefault ? "S/n" : "s/N")}] ");
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
    return string.IsNullOrEmpty(answer) ? byDefault : answer is "s" or "si" or "sí" or "y" or "yes";
}

void End()
{
    if (yes || Console.IsInputRedirected) return;
    Console.WriteLine();
    Console.Write("Pulsa Intro para salir.");
    Console.ReadLine();
}

static void Title(string text)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(text);
    Console.WriteLine(new string('─', text.Length));
    Console.ResetColor();
    Console.WriteLine();
}

static void Say(ConsoleColor color, string text)
{
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ResetColor();
}

// K-Desktop (4 or 5) running now, by the major version of its exe; an elevated one is read with the limited query.
static List<(Process Process, string? Path)> Instances(Func<int, bool> version)
{
    var found = new List<(Process, string?)>();
    foreach (var name in (string[])["KDesktop", "KDesktop4", "KDesktopL"])
        foreach (var p in Process.GetProcessesByName(name))
        {
            var path = ProcessPath(p.Id);
            if (path is not null && version(FileVersionInfo.GetVersionInfo(path).FileMajorPart)) found.Add((p, path));
            else p.Dispose();
        }
    return found;
}

static string? ProcessPath(int pid)
{
    var handle = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
    if (handle == IntPtr.Zero) return null;
    try
    {
        var buffer = new char[1024];
        var size = buffer.Length;
        return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
    }
    finally
    {
        CloseHandle(handle);
    }
}

[DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, [Out] char[] name, ref int size);
