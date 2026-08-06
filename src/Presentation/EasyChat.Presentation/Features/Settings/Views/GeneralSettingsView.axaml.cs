using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EasyChat.Contracts.Speech;
using LangResources = EasyChat.Presentation.Lang.Resources;

namespace EasyChat.Presentation.Features.Settings.Views;

public partial class GeneralSettingsView : UserControl
{
    public GeneralSettingsView() => InitializeComponent();

    private async void ChangeApplicationDataLocation_OnClick(object? sender, RoutedEventArgs args)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || DataContext is not SettingViewModel viewModel)
            return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LangResources.SelectApplicationDataLocation,
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
            await viewModel.ChangeApplicationDataLocationAsync(path);
    }

    private async void ImportAsrModelFolder_OnClick(object? sender, RoutedEventArgs args)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || DataContext is not SettingViewModel viewModel)
            return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LangResources.ImportAsrModelFolder,
            AllowMultiple = true
        });
        var paths = folders
            .Where(folder => folder.Path.IsFile)
            .Select(folder => folder.Path.LocalPath)
            .ToArray();
        if (paths.Length > 0)
        {
            await viewModel.ImportAsrModelsAsync(
                paths,
                SpeechRecognitionModelImportSourceKind.Directory);
        }
    }

    private async void ImportAsrModelArchive_OnClick(object? sender, RoutedEventArgs args)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || DataContext is not SettingViewModel viewModel)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LangResources.ImportAsrModelArchive,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(LangResources.AsrModelArchives)
                {
                    Patterns = ["*.zip", "*.tar", "*.tar.gz", "*.tgz"]
                }
            ]
        });
        var paths = files
            .Where(file => file.Path.IsFile)
            .Select(file => file.Path.LocalPath)
            .ToArray();
        if (paths.Length > 0)
        {
            await viewModel.ImportAsrModelsAsync(
                paths,
                SpeechRecognitionModelImportSourceKind.Archive);
        }
    }
}
