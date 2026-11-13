using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OPTISENS.Core.Config;

namespace OPTISENS.App.ViewModels;

public enum GripStyle { Palm, Claw, Fingertip }

public sealed record GripOption(GripStyle Value, string Label)
{
    public override string ToString() => Label;
}

public readonly record struct HardwareProfile(double Dpi, double PadWidthCm, GripStyle Grip);

public partial class HardwareProfileViewModel : ViewModelBase
{
    [ObservableProperty] private decimal _dpi = Constants.DpiDefault;
    [ObservableProperty] private decimal _padWidthCm = (decimal)Constants.PadWidthDefaultCm;
    [ObservableProperty] private GripOption _selectedGrip;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private string _boundsHintText = FormatBounds((double)Constants.PadWidthDefaultCm);

    partial void OnPadWidthCmChanged(decimal value) => BoundsHintText = FormatBounds((double)value);

    private static string FormatBounds(double padWidthCm)
    {
        double hi = Math.Min(Constants.BoundsMaxCm360, Constants.PadTurnFraction * Math.Max(0, padWidthCm));
        return $"Bounds: {Constants.BoundsMinCm360:F1} cm ↔ {hi:F1} cm";
    }

    public IReadOnlyList<GripOption> GripStyles { get; } =
    [
        new(GripStyle.Palm, "Palm Grip"),
        new(GripStyle.Claw, "Claw Grip"),
        new(GripStyle.Fingertip, "Fingertip Grip"),
    ];

    public HardwareProfileViewModel() => _selectedGrip = GripStyles[0];

    public event Action<HardwareProfile>? ProfileReady;

    [RelayCommand]
    private void StartCalibration()
    {
        ValidationError = null;
        if (Dpi < Constants.DpiMin || Dpi > Constants.DpiMax)
        {
            ValidationError = $"DPI must be between {Constants.DpiMin} and {Constants.DpiMax}.";
            return;
        }
        if (PadWidthCm < (decimal)Constants.PadWidthMinCm)
        {
            ValidationError = $"Mousepad width must be at least {Constants.PadWidthMinCm} cm.";
            return;
        }
        ProfileReady?.Invoke(new HardwareProfile((double)Dpi, (double)PadWidthCm, SelectedGrip.Value));
    }

    [RelayCommand]
    private static void ExitApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
