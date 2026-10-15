using System.Windows.Data;
using System.Windows.Markup;
using KDesktop.Core.Localization;

namespace KDesktop.Localization;

/// <summary>
/// XAML markup extension for translated text: <c>Text="{l:Tr commands.title}"</c>.
/// Produces a binding, so the text updates instantly when the language changes.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
