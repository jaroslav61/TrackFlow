using CommunityToolkit.Mvvm.ComponentModel;
using TrackFlow.Models;
using System;

namespace TrackFlow.ViewModels.Operation;

public partial class LocoItemViewModel : ObservableObject
{
    private readonly Action _markDirty;

    public LocoRecord Model { get; }

    public LocoItemViewModel(LocoRecord model, Action markDirty)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _markDirty = markDirty ?? throw new ArgumentNullException(nameof(markDirty));
    }

    public string Id => Model.Id;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value;
            OnPropertyChanged();
            _markDirty();
        }
    }

    public int Address
    {
        get => Model.Address;
        set
        {
            if (Model.Address == value) return;
            Model.Address = value;
            OnPropertyChanged();
            _markDirty();
        }
    }

    public string? Description
    {
        get => Model.Description;
        set
        {
            if (Model.Description == value) return;
            Model.Description = value;
            OnPropertyChanged();
            _markDirty();
        }
    }
}
