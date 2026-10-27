using System.Text.Json;

namespace PreySense.Mode
{
    public static class GameProfileStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string StorePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PreySense",
            "game-profiles.json");

        public static List<GameProfile> Load()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new List<GameProfile>();

                string json = File.ReadAllText(StorePath);
                return JsonSerializer.Deserialize<List<GameProfile>>(json, JsonOptions) ?? new List<GameProfile>();
            }
            catch
            {
                return new List<GameProfile>();
            }
        }

        public static void Save(IEnumerable<GameProfile> profiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            string json = JsonSerializer.Serialize(profiles, JsonOptions);
        string tempPath = StorePath + $".{Environment.ProcessId}.tmp";
        try
        {
            // Write and flush a complete replacement first. A power loss or
            // process crash must not leave the user with a truncated profile file.
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, StorePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Cleanup failure does not invalidate a successful replacement.
            }
        }
        }

        public static string NormalizeExeName(string pathOrName)
        {
            string name = Path.GetFileName(pathOrName.Trim());
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";
            return name;
        }
    }
}
