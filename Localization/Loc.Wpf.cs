using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace Reminder;

public sealed partial class Loc
{
    public static XmlLanguage Xml => XmlLanguage.GetLanguage(Culture.IetfLanguageTag);
}

// {local:Tr Key} binds a property to the current translation of Key.
public sealed class Tr(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider services) =>
        new Binding($"[{key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(services);
}
