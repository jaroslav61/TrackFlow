namespace TrackFlow.ViewModels.Library;

public sealed class IconItem
{
    public IconItem(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }
    public string FullPath { get; }
}
