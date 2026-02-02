using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.Models;

public sealed class LocoRecord : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private int _address = 3; // 1..10239
    private string _description = string.Empty;
    private string _iconName = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    // Icon file name (e.g. '754.png')
    public string IconName
    {
        get => _iconName;
        set => SetProperty(ref _iconName, value);
    }
}
