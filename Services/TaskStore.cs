using System.IO;
using System.Linq;
using System.Text.Json;

namespace Reminder;

public sealed class TaskStore
{
    private readonly string path;
    public TaskStore(string directory) { Directory.CreateDirectory(directory); path = Path.Combine(directory, "tasks.json"); }
    public AppState Load()
    {
        if (!File.Exists(path)) return new();
        try {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ?? throw new InvalidDataException("Task file is empty.");
            state.Tasks = state.Tasks.Select(Recurring.Migrate).ToList();
            return state;
        }
        catch (JsonException ex) { throw new InvalidDataException("Couldn't read tasks.json. The file was kept; restore tasks.json.bak before opening Reminder again.", ex); }
    }
    public void Save(AppState state)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
}
