using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EasyChat.Views.Dialogs;

public partial class FixedAreaFormDialogView : UserControl
{
    public FixedAreaFormDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
