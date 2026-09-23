using System.Windows;
using System.Windows.Controls;
using UnrealKit.Core.Projects;
using UnrealKit.Desktop.Models;

namespace UnrealKit.Desktop.Views;

public sealed class ConsolePresetTemplateSelector : DataTemplateSelector
{
    public DataTemplate? BoolTemplate   { get; set; }
    public DataTemplate? ActionTemplate { get; set; }
    public DataTemplate? ValueTemplate  { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        => item is ConsoleCommandPresetOption o ? o.Kind switch
        {
            ConsoleCommandKind.Bool   => BoolTemplate,
            ConsoleCommandKind.Action => ActionTemplate,
            ConsoleCommandKind.Value  => ValueTemplate,
            _                         => base.SelectTemplate(item, container)
        } : base.SelectTemplate(item, container);
}
