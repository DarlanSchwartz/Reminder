using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Reminder;

public sealed class TaskStore
{
    private readonly string path;
    public TaskStore(string directory) { Directory.CreateDirectory(directory); path = Path.Combine(directory, "tasks.json"); }
    public AppState Load()
    {
        if (!File.Exists(path)) return new();
        AppState state;
        try { state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ?? throw new InvalidDataException("Task file is empty."); }
        catch (JsonException ex) { throw new InvalidDataException("Couldn't read tasks.json. The file was kept; restore tasks.json.bak before opening Reminder again.", ex); }
        // A hand-edited file may say "Tasks": null or contain null entries; both would crash later.
        state.Tasks = state.Tasks is null ? new List<TaskItem>() : state.Tasks.FindAll(t => t is not null);
        return state;
    }
    public void Save(AppState state)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
        else File.Move(temp, path);
    }
}
