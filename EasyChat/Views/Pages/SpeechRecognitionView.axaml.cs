using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EasyChat.Views.Pages;

public partial class SpeechRecognitionView : UserControl
{
    public SpeechRecognitionView()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
