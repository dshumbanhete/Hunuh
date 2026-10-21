namespace Hunuh;

public class AccessDeniedException(string message) : Exception(message);

/// <summary>Sign-in, sign-out and the single choke point every service uses to verify a token.</summary>
public sealed class AuthService(DataStore store, TokenService tokens)
{
    public string? Login(string username, string password)
    {
        var u = store.Data.Users.FirstOrDefault(x =>
            x.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase));
        if (u is null || !u.IsActive || !PasswordHasher.Verify(password, u.PasswordHash, u.Salt)) return null;
        return tokens.Issue(u, TimeSpan.FromHours(8));
    }

    public void Logout(string? token) { if (token is not null) tokens.Revoke(token); }
    public bool IsValid(string? token) => tokens.Validate(token) is not null;
    public TokenClaims? Claims(string? token) => tokens.Validate(token);

    /// <summary>Validates the token, re-checks the account, and enforces the allowed roles.
    /// The stored role is authoritative, so role changes and deactivation apply immediately.</summary>
    public AppUser Require(string? token, params Role[] allowed)
    {
        var c = tokens.Validate(token)
            ?? throw new AccessDeniedException("Your session has expired. Please sign in again.");
        var u = store.Data.Users.FirstOrDefault(x => x.Id == c.UserId);
        if (u is null || !u.IsActive) throw new AccessDeniedException("This account is no longer active.");
        if (allowed.Length > 0 && !allowed.Contains(u.Role))
            throw new AccessDeniedException($"The {u.Role} profile cannot perform this action.");
        return u;
    }
}

public sealed class LearningService(DataStore store, AuthService auth)
{
    public List<Lesson> GetLessons(string? token)
    {
        auth.Require(token);
        lock (store.Sync) return store.Data.Lessons.OrderBy(l => l.Language).ThenBy(l => l.Title).ToList();
    }

    public Lesson CreateLesson(string? token, string language, string title, string level)
    {
        var u = auth.Require(token, Role.Teacher, Role.Admin);
        if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Language and title are required.");
        var l = new Lesson { Language = language.Trim(), Title = title.Trim(), Level = level, AuthorId = u.Id };
        lock (store.Sync) store.Data.Lessons.Add(l);
        store.Save();
        return l;
    }

    Lesson Owned(AppUser u, Guid id)
    {
        var l = store.Data.Lessons.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Lesson not found.");
        if (u.Role != Role.Admin && l.AuthorId != u.Id)
            throw new AccessDeniedException("Teachers can only edit their own lessons.");
        return l;
    }

    public void DeleteLesson(string? token, Guid id)
    {
        var u = auth.Require(token, Role.Teacher, Role.Admin);
        lock (store.Sync) store.Data.Lessons.Remove(Owned(u, id));
        store.Save();
    }

    public void AddWord(string? token, Guid lessonId, VocabItem w)
    {
        var u = auth.Require(token, Role.Teacher, Role.Admin);
        if (string.IsNullOrWhiteSpace(w.Term) || string.IsNullOrWhiteSpace(w.Translation))
            throw new InvalidOperationException("Term and translation are required.");
        lock (store.Sync) Owned(u, lessonId).Words.Add(w);
        store.Save();
    }

    public void RemoveWord(string? token, Guid lessonId, VocabItem w)
    {
        var u = auth.Require(token, Role.Teacher, Role.Admin);
        lock (store.Sync) Owned(u, lessonId).Words.Remove(w);
        store.Save();
    }

    public void RecordResult(string? token, Guid lessonId, int score, int total)
    {
        var u = auth.Require(token);
        lock (store.Sync)
        {
            var l = store.Data.Lessons.FirstOrDefault(x => x.Id == lessonId);
            if (l is null) return;
            store.Data.Progress.Add(new ProgressEntry
            {
                UserId = u.Id, LessonId = l.Id, LessonTitle = $"{l.Language} · {l.Title}",
                Score = score, Total = total
            });
        }
        store.Save();
    }

    public List<ProgressEntry> GetMyProgress(string? token)
    {
        var u = auth.Require(token);
        lock (store.Sync)
            return store.Data.Progress.Where(p => p.UserId == u.Id).OrderByDescending(p => p.Date).ToList();
    }
}

public sealed class AdminService(DataStore store, AuthService auth)
{
    public List<AppUser> ListUsers(string? token)
    {
        auth.Require(token, Role.Admin);
        lock (store.Sync) return store.Data.Users.OrderBy(u => u.Username).ToList();
    }

    public void CreateUser(string? token, string username, string display, string password, Role role)
    {
        auth.Require(token, Role.Admin);
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Username is required.");
        if (password.Length < 8) throw new InvalidOperationException("Password must be at least 8 characters.");
        lock (store.Sync)
        {
            if (store.Data.Users.Any(u => u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("That username already exists.");
            store.Data.Users.Add(DataStore.MakeUser(username.Trim(),
                string.IsNullOrWhiteSpace(display) ? username.Trim() : display.Trim(), password, role));
        }
        store.Save();
    }

    AppUser Target(AppUser admin, Guid id, bool allowSelf = false)
    {
        var t = store.Data.Users.FirstOrDefault(u => u.Id == id)
            ?? throw new InvalidOperationException("User not found.");
        if (!allowSelf && t.Id == admin.Id)
            throw new InvalidOperationException("You can't change your own role or status.");
        return t;
    }

    public void SetRole(string? token, Guid id, Role role)
    {
        var a = auth.Require(token, Role.Admin);
        lock (store.Sync) Target(a, id).Role = role;
        store.Save();
    }

    public void SetActive(string? token, Guid id, bool active)
    {
        var a = auth.Require(token, Role.Admin);
        lock (store.Sync) Target(a, id).IsActive = active;
        store.Save();
    }

    public void ResetPassword(string? token, Guid id, string password)
    {
        var a = auth.Require(token, Role.Admin);
        if (password.Length < 8) throw new InvalidOperationException("Password must be at least 8 characters.");
        lock (store.Sync)
        {
            var t = Target(a, id, allowSelf: true);
            (t.PasswordHash, t.Salt) = PasswordHasher.Hash(password);
        }
        store.Save();
    }
}
