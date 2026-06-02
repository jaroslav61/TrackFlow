using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.Models;

public abstract partial class Vehicle : ObservableObject
{
    protected Vehicle(string code, string name)
    {
        _code = code;
        _name = name;
    }

    private string _code;
    private string _name;

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public override string ToString() => $"{Code} - {Name}";
}
