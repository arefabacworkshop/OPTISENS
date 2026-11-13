using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OPTISENS.App.ViewModels;

namespace OPTISENS.App.Views;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttached;
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is not SetupViewModel vm) return;
        vm.PickFolderAsync = async () =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider is not { CanPickFolder: true } sp) return null;
            var folders = await sp.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose OPTISENS Assets Folder",
                AllowMultiple = false
            });
            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        };
    }
}
