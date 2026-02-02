using System;

namespace TrackFlow.Models;

public sealed class LocoRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public int Address { get; set; } = 3; // 1..10239
    public string Description { get; set; } = string.Empty;
    // Icon file name (e.g. '754.png')
    public string IconName { get; set; } = string.Empty;
}