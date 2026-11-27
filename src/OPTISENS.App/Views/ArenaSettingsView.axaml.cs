using System.ComponentModel;
using Avalonia.Controls;
using OPTISENS.App.ViewModels;

namespace OPTISENS.App.Views;

public partial class ArenaSettingsView : UserControl
{
    private ArenaSettingsViewModel? _vm;

    public ArenaSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = DataContext as ArenaSettingsViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                ApplyScale(_vm.CrosshairScale);
            }
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArenaSettingsViewModel.CrosshairScale) && _vm is not null)
            ApplyScale(_vm.CrosshairScale);
    }

    private void ApplyScale(double scale)
    {
        if (PreviewCanvas.RenderTransform is Avalonia.Media.ScaleTransform st)
            st.ScaleX = st.ScaleY = scale;
    }
}
