using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HakuStream.Kit.Storage;

public sealed class JsonStore<T>(string name, ILogger logger) where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _filePath =
        Path.Combine(AppContext.BaseDirectory, "data", name + ".json");

    public T Load()
    {
        if (!File.Exists(_filePath)) return new T();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<T>(json, Options) ?? new T();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read {File}; starting with empty state", _filePath);
            return new T();
        }
    }

    public void Save(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        var tmpPath = _filePath + ".tmp";
        File.WriteAllText(tmpPath, JsonSerializer.Serialize(value, Options));
        File.Move(tmpPath, _filePath, overwrite: true);
    }
}
