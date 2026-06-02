using System.Collections.Concurrent;
using System;

namespace TrackFlow.Services
{
    public static class IconRegistry
    {
        private static readonly ConcurrentDictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string name, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(fullPath))
                return;
            _map[name] = fullPath;
        }

        public static bool TryGet(string name, out string fullPath)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                fullPath = null!;
                return false;
            }
            return _map.TryGetValue(name, out fullPath!);
        }
    }
}
