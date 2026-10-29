using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

/// <summary>E6: exporting one item with what it needs, and importing it without stepping on anything.</summary>
public class ItemTransferTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "kdesktop-tests", Guid.NewGuid().ToString("N")[..8] + ItemTransfer.Extension);

    /// <summary>An event that runs «Abrir», which plays a macro, opens a menu with «Saludo», and puts a layout.</summary>
    private static (AppConfig Config, EventRule Rule, Command Open, Command Hello, Macro Macro, QuickMenu Menu) Sample()
    {
        var config = new AppConfig();
        var macro = new Macro { Name = "Teclear" };
        macro.Steps.Add(new MacroStep { Kind = MacroStepKind.KeyDown, Delay = 10 });
        var hello = new Command { Name = "Saludo", Hotkeys = ["Ctrl+Alt+H"] };
        hello.Actions.Add(new ShowMessageAction { Text = "hola" });
        var menu = new QuickMenu { Name = "Menú" };
        menu.Items.Add(QuickMenuOps.NewCommandItem(hello));
        var arrangement = new WindowArrangement { Name = "Trabajo" };
        var open = new Command { Name = "Abrir" };
        open.Actions.Add(new PlayMacroAction { MacroId = macro.Id });
        open.Actions.Add(new OpenQuickMenuAction { MenuId = menu.Id });
        open.Actions.Add(new ArrangementAction { ArrangementId = arrangement.Id });
        var rule = new EventRule { Name = "Al arrancar" };
        rule.Commands.Add(open.Id);
        var unrelated = new Command { Name = "Otro" };
        config.Macros.Add(macro);
        config.QuickMenus.Add(menu);
        config.Arrangements.Add(arrangement);
        foreach (var c in new[] { hello, open, unrelated }) config.Commands.Add(c);
        config.Events.Add(rule);
        return (config, rule, open, hello, macro, menu);
    }

    [Fact]
    public void AnEventTakesItsCommandsAndWhatTheyUseAllTheWayButNothingElse()
    {
        var (config, rule, _, _, _, _) = Sample();
        var bundle = ItemTransfer.Collect(config, TransferKind.Event, rule.Id);

        Assert.Single(bundle.Events);
        Assert.Equal(["Abrir", "Saludo"], bundle.Commands.Select(c => c.Name).Order());
        Assert.Single(bundle.Macros);
        Assert.Single(bundle.QuickMenus);
        Assert.Single(bundle.Arrangements);
        Assert.Empty(bundle.KBoards);
    }

    [Fact]
    public void IntoAnEmptyConfigEverythingIsAddedWithItsIdsAndReferences()
    {
        var (config, rule, _, _, _, _) = Sample();
        var file = TempFile();
        ItemTransfer.Export(config, TransferKind.Event, rule.Id, file);

        var target = new AppConfig();
        var incoming = ItemTransfer.Read(file);
        Assert.Equal("Al arrancar", incoming.Name);
        Assert.Empty(ItemTransfer.Clashes(target, incoming.Bundle));
        var result = ItemTransfer.Apply(target, incoming, ItemTransfer.ClashChoice.Copy);

        Assert.Equal(rule.Id, result.RootId);
        Assert.Equal(6, result.Added);
        var open = target.Commands.Single(c => c.Name == "Abrir");
        Assert.Equal(target.Events[0].Commands[0], open.Id);
        Assert.Equal(target.Macros[0].Id, open.Actions.OfType<PlayMacroAction>().Single().MacroId);
        Assert.Equal("Menú", open.Actions.OfType<OpenQuickMenuAction>().Single().MenuName);
        Assert.Equal("Ctrl+Alt+H", target.Commands.Single(c => c.Name == "Saludo").Hotkeys.Single());
    }

    [Fact]
    public void ImportingWhatIsAlreadyHereTheSameAddsNothing()
    {
        var (config, rule, _, _, _, _) = Sample();
        var file = TempFile();
        ItemTransfer.Export(config, TransferKind.Event, rule.Id, file);
        var before = config.Commands.Count;

        var incoming = ItemTransfer.Read(file);
        Assert.Empty(ItemTransfer.Clashes(config, incoming.Bundle));
        var result = ItemTransfer.Apply(config, incoming, ItemTransfer.ClashChoice.Copy);

        Assert.Equal(0, result.Added);
        Assert.Equal(rule.Id, result.RootId);
        Assert.Equal(before, config.Commands.Count);
    }

    [Fact]
    public void WhenAMacroWasMadeIsNotAChange()
    {
        var (config, rule, _, _, macro, _) = Sample();
        var file = TempFile();
        ItemTransfer.Export(config, TransferKind.Event, rule.Id, file);
        macro.Created = macro.Created.AddDays(-3);

        Assert.Empty(ItemTransfer.Clashes(config, ItemTransfer.Read(file).Bundle));
    }

    [Fact]
    public void WhatChangedSinceIsAskedAndThenCopiedWithNewIdsNamesAndNoTakenHotkeys()
    {
        var (config, rule, open, hello, macro, _) = Sample();
        var file = TempFile();
        ItemTransfer.Export(config, TransferKind.Event, rule.Id, file);
        // Here, «Saludo» changed after exporting.
        hello.Actions.Add(new ShowMessageAction { Text = "otra vez" });

        var incoming = ItemTransfer.Read(file);
        var clash = Assert.Single(ItemTransfer.Clashes(config, incoming.Bundle));
        Assert.Equal(TransferKind.Command, clash.Kind);
        Assert.Equal("Saludo", clash.Name);

        var result = ItemTransfer.Apply(config, incoming, ItemTransfer.ClashChoice.Copy);

        // «Saludo» is copied, and so is what uses it all the way up (its menu, «Abrir», the event) so the copies work as
        // they were exported; the macro and the layout, the same as here, are used as they are.
        Assert.Equal(4, result.Added);
        Assert.Equal(1, result.HotkeysDropped);
        var copy = config.Commands.Single(c => c.Name == "Saludo 2");
        Assert.NotEqual(hello.Id, copy.Id);
        Assert.Empty(copy.Hotkeys);
        var ruleCopy = config.Events.Single(e => e.Id == result.RootId);
        Assert.Equal("Al arrancar 2", ruleCopy.Name);
        var openCopy = config.Commands.Single(c => c.Id == ruleCopy.Commands.Single());
        Assert.Equal("Abrir 2", openCopy.Name);
        var menuCopy = config.QuickMenus.Single(q => q.Id == openCopy.Actions.OfType<OpenQuickMenuAction>().Single().MenuId);
        Assert.Equal(copy.Id, menuCopy.Items.Single().CommandId);
        Assert.Equal(macro.Id, openCopy.Actions.OfType<PlayMacroAction>().Single().MacroId);
        Assert.Single(config.Macros);
        // Nothing that was here changed.
        Assert.Equal(2, hello.Actions.Count);
        Assert.Equal(open.Id, rule.Commands.Single());
    }

    [Fact]
    public void AChangedOneThatIsTheRootGetsACopyAndItsReferencesPointToTheCopies()
    {
        var (config, rule, open, _, _, menu) = Sample();
        var file = TempFile();
        ItemTransfer.Export(config, TransferKind.Command, open.Id, file);
        menu.Name = "Mi menú";
        open.Notes = "cambiado";

        var result = ItemTransfer.Apply(config, ItemTransfer.Read(file), ItemTransfer.ClashChoice.Copy);

        var copy = config.Commands.Single(c => c.Id == result.RootId);
        Assert.Equal("Abrir 2", copy.Name);
        var menuCopy = config.QuickMenus.Single(q => q.Id != menu.Id);
        Assert.Equal("Menú", menuCopy.Name);
        Assert.Equal(menuCopy.Id, copy.Actions.OfType<OpenQuickMenuAction>().Single().MenuId);
        // The event here still runs the command here.
        Assert.Equal(open.Id, rule.Commands.Single());
    }

    [Fact]
    public void KeepingMineUsesWhatIsHere()
    {
        var (config, rule, open, _, _, menu) = Sample();
        var file = TempFile();
        ItemTransfer.Export(config, TransferKind.Event, rule.Id, file);
        menu.Name = "Mi menú";

        var result = ItemTransfer.Apply(config, ItemTransfer.Read(file), ItemTransfer.ClashChoice.KeepMine);

        Assert.Equal(0, result.Added);
        Assert.Single(config.QuickMenus);
        Assert.Equal(menu.Id, open.Actions.OfType<OpenQuickMenuAction>().Single().MenuId);
    }

    [Fact]
    public void OwnIconsTravelAndOneWithTheSameNameButOtherContentIsKept()
    {
        var (config, _, _, hello, _, _) = Sample();
        Directory.CreateDirectory(AppPaths.IconsDir);
        var name = "transfer-" + Guid.NewGuid().ToString("N")[..6] + ".png";
        File.WriteAllBytes(Path.Combine(AppPaths.IconsDir, name), [1, 2, 3]);
        hello.Icon = name;
        var file = TempFile();
        ItemTransfer.Export(config, TransferKind.Command, hello.Id, file);
        // Here, by the time it is imported, that file is another picture.
        File.WriteAllBytes(Path.Combine(AppPaths.IconsDir, name), [9, 9]);

        var target = new AppConfig();
        ItemTransfer.Apply(target, ItemTransfer.Read(file), ItemTransfer.ClashChoice.Copy);

        var icon = target.Commands.Single().Icon!;
        Assert.NotEqual(name, icon);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(AppPaths.IconsDir, icon)));
        Assert.Equal([9, 9], File.ReadAllBytes(Path.Combine(AppPaths.IconsDir, name)));
    }

    [Fact]
    public void AFileThatIsNotAnExportIsTurnedDown()
    {
        var file = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "no");
        Assert.ThrowsAny<InvalidDataException>(() => ItemTransfer.Read(file));
    }
}
