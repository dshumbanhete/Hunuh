using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.IO;



namespace Hunuh;

public sealed class DataStore
{
    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    readonly string _path;
    public readonly object Sync = new();
    public AppData Data { get; private set; } = new();

    public DataStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LinguaLearn", "data.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        Load();
    }

    void Load()
    {
        try
        {
            if (File.Exists(_path))
                Data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(_path), Opts) ?? Seed();
            else
                Data = Seed();
        }
        catch { Data = Seed(); }
        Save();
    }

    public void Save()
    {
        lock (Sync) File.WriteAllText(_path, JsonSerializer.Serialize(Data, Opts));
    }

    public static AppUser MakeUser(string username, string display, string password, Role role)
    {
        var (hash, salt) = PasswordHasher.Hash(password);
        return new AppUser { Username = username, DisplayName = display, PasswordHash = hash, Salt = salt, Role = role };
    }

    static AppData Seed()
    {
        var teacher = MakeUser("teacher", "Demo Teacher", "Teacher123!", Role.Teacher);
        var d = new AppData();
        d.Users.Add(MakeUser("admin", "Administrator", "Admin123!", Role.Admin));
        d.Users.Add(teacher);
        d.Users.Add(MakeUser("learner", "Demo Learner", "Learner123!", Role.User));

        d.Lessons.Add(new Lesson
        {
            Language = "Spanish", Title = "Greetings", Level = "Beginner", AuthorId = teacher.Id,
            Words =
            {
                new VocabItem { Term = "Hola", Translation = "Hello", Example = "Hola, ¿cómo estás?" },
                new VocabItem { Term = "Gracias", Translation = "Thank you", Example = "Muchas gracias." },
                new VocabItem { Term = "Por favor", Translation = "Please", Example = "Un café, por favor." },
                new VocabItem { Term = "Buenos días", Translation = "Good morning", Example = "Buenos días, señora." }
            }
        });
        d.Lessons.Add(new Lesson
        {
            Language = "Shona", Title = "First words", Level = "Beginner", AuthorId = teacher.Id,
            Words =
            {
                new VocabItem { Term = "Mhoro", Translation = "Hello" },
                new VocabItem { Term = "Mangwanani", Translation = "Good morning" },
                new VocabItem { Term = "Ndatenda", Translation = "Thank you" },
                new VocabItem { Term = "Ndapota", Translation = "Please" }
            }
        });
        return d;
    }
}
