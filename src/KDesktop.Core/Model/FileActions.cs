using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>What «Archivos y carpetas» does.</summary>
public enum FileOperation { Copy, Move, Rename, Delete, CreateFolder, Zip, Unzip, Backup }

/// <summary>What happens when something with the same name is already in the destination.</summary>
public enum FileExists { Replace, Skip, IfNewer, KeepBoth }

/// <summary>A backup: the destination kept equal to the source, or a new copy with the date each time.</summary>
public enum BackupMode { Mirror, Dated }

/// <summary>
/// «Archivos y carpetas»: copy, move, rename, delete, zip, unzip or back up. The sources are a list of files, folders or
/// patterns («C:\Música\*.mp3»), written or added with the buttons; the destination folder is never walked as a source,
/// so a backup inside what it backs up does not copy itself (<see cref="Platform.FileJobs"/>).
/// </summary>
public partial class FilesAction : CommandAction
{
    [ObservableProperty] private FileOperation _operation = FileOperation.Copy;

    /// <summary>Files, folders or patterns (a «*» or «?» in the last part), with {fecha}/{hora}.</summary>
    public ObservableCollection<string> Sources
    {
        get;
        set
        {
            if (field is not null) field.CollectionChanged -= OnSourcesChanged;
            field = value ?? [];
            field.CollectionChanged += OnSourcesChanged;
            OnPropertyChanged(nameof(Sources));
        }
    } = [];

    /// <summary>Patterns also look in the subfolders (keeping the folders in the destination).</summary>
    [ObservableProperty] private bool _subfolders;

    /// <summary>The folder (or .zip for «Comprimir») where things go; for «Crear carpeta», the folder to create.</summary>
    [ObservableProperty] private string _destination = string.Empty;

    /// <summary>«Renombrar»: the new name (with {fecha}/{hora}).</summary>
    [ObservableProperty] private string _newName = string.Empty;

    [ObservableProperty] private FileExists _exists = FileExists.IfNewer;

    /// <summary>«Borrar»: to the recycle bin (or gone for good).</summary>
    [ObservableProperty] private bool _recycle = true;

    [ObservableProperty] private BackupMode _backupMode = BackupMode.Mirror;

    /// <summary>«Sincronizar»: also take out of the destination what is no longer in the source.</summary>
    [ObservableProperty] private bool _mirrorDelete;

    /// <summary>«Con fecha»: each copy as a .zip.</summary>
    [ObservableProperty] private bool _backupZip;

    /// <summary>«Con fecha»: how many copies to keep (0: all).</summary>
    [ObservableProperty] private int _keep = 5;

    /// <summary>The next action waits until it is done (otherwise it goes on by itself).</summary>
    [ObservableProperty] private bool _wait = true;

    /// <summary>Progress and result on the OSD.</summary>
    [ObservableProperty] private bool _showOsd = true;

    public FilesAction() => Sources.CollectionChanged += OnSourcesChanged;

    private void OnSourcesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(Sources));

    public override ActionKind Kind => ActionKind.Files;

    public override string Summary
    {
        get
        {
            var what = Loc.T($"enum.fileOp.{Operation}");
            if (Operation == FileOperation.CreateFolder) return $"{what}  ·  {Or(Destination, "summary.noFolder")}";
            var first = Sources.Count == 0 ? Loc.T("summary.noPath") : Path.GetFileName(Sources[0].TrimEnd('\\')) is { Length: > 0 } name ? name : Sources[0];
            var more = Sources.Count > 1 ? $" +{Sources.Count - 1}" : "";
            var to = Operation is FileOperation.Delete or FileOperation.Rename || string.IsNullOrWhiteSpace(Destination) ? ""
                : "  →  " + (Path.GetFileName(Destination.TrimEnd('\\')) is { Length: > 0 } dest ? dest : Destination);
            if (Operation == FileOperation.Rename && NewName.Length > 0) to = "  →  " + NewName;
            return $"{what}  ·  {first}{more}{to}";
        }
    }
}
