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
}
