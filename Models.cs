using System.Text.Json.Serialization;
namespace Hunuh;

public enum Role { User, Teacher, Admin }

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public Role Role { get; set; } = Role.User;
    public bool IsActive { get; set; } = true;
}

public class VocabItem
{
    public string Term { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Example { get; set; } = "";
}

public class Lesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Language { get; set; } = "";
    public string Title { get; set; } = "";
    public string Level { get; set; } = "Beginner";
    public Guid AuthorId { get; set; }
    public List<VocabItem> Words { get; set; } = new();
    [JsonIgnore] public int WordCount => Words.Count;
}

public class ProgressEntry
{
    public Guid UserId { get; set; }
    public Guid LessonId { get; set; }
    public string LessonTitle { get; set; } = "";
    public int Score { get; set; }
    public int Total { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    [JsonIgnore] public string Result => $"{Score}/{Total}";
}

public class AppData
{
    public List<AppUser> Users { get; set; } = new();
    public List<Lesson> Lessons { get; set; } = new();
    public List<ProgressEntry> Progress { get; set; } = new();
}
