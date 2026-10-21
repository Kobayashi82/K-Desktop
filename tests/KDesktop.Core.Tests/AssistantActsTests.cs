using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>Stage 8.1: the program an order names, and the command made for the occasion with the catalogue's actions.</summary>
public class AssistantActsTests
{
    private static readonly InstalledApp[] Apps =
    [
        new("Inno Setup Example Scripts", "a"), new("Steam", "b"), new("Steam VR", "c"), new("Google Chrome", "d"),
        new("Visual Studio Code", "e"), new("Bloc de notas", "f"), new("Spotify", "g"),
    ];

    [Theory]
    [InlineData("Steam", "Steam")]
    [InlineData("steam", "Steam")]
    [InlineData("chrome", "Google Chrome")]
    [InlineData("el bloc de notas", "Bloc de notas")]
    [InlineData("vs code", "Visual Studio Code")]
    [InlineData("vscode", "Visual Studio Code")]
    [InlineData("el programa Spotify", "Spotify")]
    public void AnAppIsFoundByItsWords(string said, string expected) => Assert.Equal(expected, AppFinder.FindApp(said, Apps)?.Name);

    [Theory]
    [InlineData("Epic Games")]
    [InlineData("scripts de ejemplo de steam")]
    [InlineData("la ventana")]
    public void WhatIsNotThereIsNotFound(string said) => Assert.Null(AppFinder.FindApp(said, Apps));

    private static readonly OpenWindow Chrome = new(1, 10, "chrome", "YouTube - Google Chrome", "Google Chrome");
    private static readonly OpenWindow ChromeMail = new(2, 10, "chrome", "Correo - Google Chrome", "Google Chrome");
    private static readonly OpenWindow Steam = new(3, 20, "steamwebhelper", "Steam", "Steam Client WebHelper");
    private static readonly OpenWindow[] Windows = [Chrome, ChromeMail, Steam];

    private static AssistantActs.Plan Build(string op, string app, ForegroundWindow? foreground = null, Func<string, string?>? process = null) =>
        AssistantActs.Build(new ActMessage("lander", "a-1", op, app), Windows, process ?? (_ => null), foreground ?? new ForegroundWindow(Steam.Hwnd, Steam.Pid, Steam.Process));

    [Fact]
    public void WindowsAreNamedAsTheUserSeesThem()
    {
        Assert.Equal("Steam", Steam.DisplayName);
        Assert.Equal("Google Chrome", Chrome.DisplayName);
        Assert.Equal("Papelera de reciclaje", new OpenWindow(4, 30, "explorer", "Papelera de reciclaje - Explorador de archivos", "Explorador de Windows").DisplayName);
        Assert.Equal("Visual Studio Code", new OpenWindow(5, 40, "Code", "Program.cs - K-Desktop - Visual Studio Code", "Visual Studio Code").DisplayName);
    }

    [Fact]
    public void ClosingAWindowIsThatWindowOnly()
    {
        var plan = Build("closeWindow", "YouTube");
        var action = Assert.IsType<WindowAction>(Assert.Single(plan.Command!.Actions));
        Assert.Equal(WindowOperation.Close, action.Operation);
        Assert.Equal(Chrome.Hwnd, action.Handle);
        Assert.Equal("Google Chrome", plan.Name);
        // The one most in front when several fit.
        Assert.Equal(Chrome.Hwnd, Assert.IsType<WindowAction>(Build("minimize", "chrome").Command!.Actions[0]).Handle);
    }

    [Fact]
    public void QuittingIsTheWholeProgramEvenWithoutAWindow()
    {
        var quit = Assert.IsType<AppControlAction>(Build("quit", "chrome").Command!.Actions[0]);
        Assert.Equal((AppControlMode.Close, "chrome"), (quit.Mode, quit.ProcessName));
        var tray = Build("quit", "Discord", process: s => s.Contains("discord", StringComparison.OrdinalIgnoreCase) ? "discord" : null);
        Assert.Equal("discord", Assert.IsType<AppControlAction>(tray.Command!.Actions[0]).ProcessName);
        Assert.Equal("noWindow", Build("minimize", "Discord").Reason);
        Assert.Equal("noProcess", Build("quit", "Discord").Reason);
    }

    [Fact]
    public void WithoutAProgramItIsTheWindowInFront()
    {
        var front = new ForegroundWindow(ChromeMail.Hwnd, ChromeMail.Pid, ChromeMail.Process);
        Assert.Equal(ChromeMail.Hwnd, Assert.IsType<WindowAction>(Build("maximize", "", front).Command!.Actions[0]).Handle);
        Assert.Equal(ChromeMail.Hwnd, Assert.IsType<WindowAction>(Build("maximize", "esta ventana", front).Command!.Actions[0]).Handle);
        var volume = Assert.IsType<VolumeAction>(Build("mute", "", front).Command!.Actions[0]);
        Assert.Equal((VolumeTarget.ForegroundApp, VolumeMode.Mute), (volume.Target, volume.Mode));
        // K-Desktop itself, or a window that is not on the list (the desktop): not there.
        Assert.Equal("notAllowed", Build("minimize", "", new ForegroundWindow(99, 99, "explorer")).Reason);
    }

    [Fact]
    public void TheVolumeOfAProgram()
    {
        var plan = AssistantActs.Build(new ActMessage("", "a", "volume", "Steam", 140), Windows, _ => null, new ForegroundWindow(0, 0, ""));
        var volume = Assert.IsType<VolumeAction>(plan.Command!.Actions[0]);
        Assert.Equal((VolumeTarget.App, "steamwebhelper", VolumeMode.Set, 100), (volume.Target, volume.ProcessName, volume.Mode, volume.Value));
    }

    [Fact]
    public void OnlyItsOperations() => Assert.Throws<ArgumentException>(() => Build("runScript", "Steam"));

    [Fact]
    public void ClosingAsksLikeClosingAProgram()
    {
        Assert.Equal(ToolRisk.AskOnce, ActionRisk.Of(new WindowAction { Operation = WindowOperation.Close }));
        Assert.Equal(ToolRisk.Safe, ActionRisk.Of(new WindowAction { Operation = WindowOperation.Minimize }));
    }
}
