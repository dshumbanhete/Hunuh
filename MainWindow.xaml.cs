using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Hunuh;

public partial class MainWindow : Window
{
    readonly DataStore _store = new();
    readonly TokenService _tokens = new();
    readonly AuthService _auth;
    readonly LearningService _learn;
    readonly AdminService _admin;

    string? _token;   // the session token; passed to every service call

    // practice state
    Lesson? _practice;
    List<VocabItem> _deck = new();
    int _idx, _correct;

    public MainWindow()
    {
        InitializeComponent();
        _auth = new AuthService(_store, _tokens);
        _learn = new LearningService(_store, _auth);
        _admin = new AdminService(_store, _auth);
        URole.ItemsSource = Enum.GetValues<Role>();
        URole.SelectedItem = Role.User;
    }

    // ---------- helpers ----------
    bool Guard(Action action)
    {
        try { action(); return true; }
        catch (AccessDeniedException ex)
        {
            MessageBox.Show(ex.Message, "Access denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (!_auth.IsValid(_token)) ShowLogin();
            return false;
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Hunuh", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }

    static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;

    // ---------- sign in / out ----------
    void LoginPass_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) SignIn_Click(s, e); }

    void SignIn_Click(object s, RoutedEventArgs e)
    {
        _token = _auth.Login(LoginUser.Text, LoginPass.Password);
        if (_token is null) { LoginError.Text = "Invalid username or password."; return; }
        LoginError.Text = ""; LoginPass.Clear();
        ShowMain();
    }

    void SignOut_Click(object s, RoutedEventArgs e) { _auth.Logout(_token); ShowLogin(); }

    void ShowLogin()
    {
        _token = null;
        PracticePanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility = Visibility.Collapsed;
        LoginPanel.Visibility = Visibility.Visible;
    }

    void ShowMain()
    {
        var c = _auth.Claims(_token)!;
        WhoAmI.Text = $"{c.Username}  ·  {c.Role}";
        // Tabs are shown from the token's role; the services still enforce access independently.
        TabManage.Visibility = Vis(c.Role is Role.Teacher or Role.Admin);
        TabUsers.Visibility = Vis(c.Role == Role.Admin);
        LoginPanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility = Visibility.Visible;
        Tabs.SelectedItem = TabLearn;
        RefreshLessons(); RefreshProgress(); RefreshUsers();
    }

    // ---------- refresh ----------
    void RefreshLessons()
    {
        Guard(() =>
        {
            var lessons = _learn.GetLessons(_token);
            var lid = (LearnLessons.SelectedItem as Lesson)?.Id;
            var mid = (ManageLessons.SelectedItem as Lesson)?.Id;
            LearnLessons.ItemsSource = lessons;
            ManageLessons.ItemsSource = lessons;
            LearnLessons.SelectedItem = lessons.FirstOrDefault(l => l.Id == lid);
            ManageLessons.SelectedItem = lessons.FirstOrDefault(l => l.Id == mid);
            LearnWords.ItemsSource = (LearnLessons.SelectedItem as Lesson)?.Words.ToList();
            ManageWords.ItemsSource = (ManageLessons.SelectedItem as Lesson)?.Words.ToList();
        });
    }

    void RefreshProgress() => Guard(() => ProgressGrid.ItemsSource = _learn.GetMyProgress(_token));

    void RefreshUsers()
    {
        if (_auth.Claims(_token)?.Role != Role.Admin) return;
        Guard(() => UsersGrid.ItemsSource = _admin.ListUsers(_token));
    }

    // ---------- learn / practice ----------
    void LearnLessons_SelectionChanged(object s, SelectionChangedEventArgs e) =>
        LearnWords.ItemsSource = (LearnLessons.SelectedItem as Lesson)?.Words.ToList();

    void StartPractice_Click(object s, RoutedEventArgs e)
    {
        if (LearnLessons.SelectedItem is not Lesson l || l.Words.Count == 0)
        {
            MessageBox.Show("Select a lesson that has vocabulary first."); return;
        }
        _practice = l;
        _deck = l.Words.OrderBy(_ => Random.Shared.Next()).ToList();
        _idx = 0; _correct = 0;
        Feedback.Text = "";
        AnswerRow.Visibility = Visibility.Visible;
        PracticePanel.Visibility = Visibility.Visible;
        ShowCard();
    }

    void ShowCard()
    {
        PromptText.Text = $"({_idx + 1}/{_deck.Count})  Translate: {_deck[_idx].Term}";
        AnswerBox.Clear(); AnswerBox.Focus();
    }

    void AnswerBox_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Check_Click(s, e); }

    void Check_Click(object s, RoutedEventArgs e)
    {
        if (_practice is null || _idx >= _deck.Count) return;
        var card = _deck[_idx];
        var accepted = card.Translation.Split('/', ',', ';').Select(x => x.Trim());
        bool ok = accepted.Any(a => a.Equals(AnswerBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (ok) _correct++;
        Feedback.Text = ok ? "✓ Correct!" : $"✗ Answer: {card.Translation}";
        _idx++;

        if (_idx < _deck.Count) { ShowCard(); return; }

        PromptText.Text = $"Finished: {_correct} / {_deck.Count}";
        AnswerRow.Visibility = Visibility.Collapsed;
        Guard(() => _learn.RecordResult(_token, _practice.Id, _correct, _deck.Count));
        RefreshProgress();
    }

    // ---------- manage lessons ----------
    void ManageLessons_SelectionChanged(object s, SelectionChangedEventArgs e) =>
        ManageWords.ItemsSource = (ManageLessons.SelectedItem as Lesson)?.Words.ToList();

    void AddLesson_Click(object s, RoutedEventArgs e)
    {
        var level = (NewLevel.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Beginner";
        if (Guard(() => _learn.CreateLesson(_token, NewLang.Text, NewTitle.Text, level)))
        {
            NewTitle.Clear(); RefreshLessons();
        }
    }

    void DeleteLesson_Click(object s, RoutedEventArgs e)
    {
        if (ManageLessons.SelectedItem is not Lesson l) return;
        if (MessageBox.Show($"Delete \"{l.Title}\"?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        if (Guard(() => _learn.DeleteLesson(_token, l.Id))) RefreshLessons();
    }

    void AddWord_Click(object s, RoutedEventArgs e)
    {
        if (ManageLessons.SelectedItem is not Lesson l) { MessageBox.Show("Select a lesson first."); return; }
        var w = new VocabItem { Term = NewTerm.Text.Trim(), Translation = NewTrans.Text.Trim(), Example = NewExample.Text.Trim() };
        if (Guard(() => _learn.AddWord(_token, l.Id, w)))
        {
            NewTerm.Clear(); NewTrans.Clear(); NewExample.Clear(); RefreshLessons();
        }
    }

    void RemoveWord_Click(object s, RoutedEventArgs e)
    {
        if (ManageLessons.SelectedItem is not Lesson l || ManageWords.SelectedItem is not VocabItem w) return;
        if (Guard(() => _learn.RemoveWord(_token, l.Id, w))) RefreshLessons();
    }

    // ---------- users (admin) ----------
    void CreateUser_Click(object s, RoutedEventArgs e)
    {
        var role = (Role)URole.SelectedItem;
        if (Guard(() => _admin.CreateUser(_token, UUser.Text, UDisplay.Text, UPass.Password, role)))
        {
            UUser.Clear(); UDisplay.Clear(); UPass.Clear(); RefreshUsers();
        }
    }

    void SetRole_Click(object s, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not AppUser u) return;
        var role = (Role)URole.SelectedItem;
        if (Guard(() => _admin.SetRole(_token, u.Id, role))) RefreshUsers();
    }

    void ToggleActive_Click(object s, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not AppUser u) return;
        if (Guard(() => _admin.SetActive(_token, u.Id, !u.IsActive))) RefreshUsers();
    }

    void ResetPw_Click(object s, RoutedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not AppUser u) return;
        if (Guard(() => _admin.ResetPassword(_token, u.Id, UPass.Password)))
        {
            UPass.Clear(); MessageBox.Show("Password reset.");
        }
    }
}
