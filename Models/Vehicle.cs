namespace TrackFlow.Models;

public abstract class Vehicle
{
    protected Vehicle(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; }
    public string Name { get; }

    public override string ToString() => $"{Code} - {Name}";
}
