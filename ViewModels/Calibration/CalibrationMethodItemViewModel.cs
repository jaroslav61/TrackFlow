using Avalonia.Media;
using TrackFlow.Models.Calibration;

namespace TrackFlow.ViewModels.Calibration;

public sealed class CalibrationMethodItemViewModel
{
	public CalibrationMethodItemViewModel(CalibrationMethod method, string description, IImage? icon)
	{
		Method = method;
		Description = description;
		Icon = icon;
	}

	public CalibrationMethod Method { get; }
	public string Description { get; }
	public IImage? Icon { get; }

	public override string ToString() => Description;
}

