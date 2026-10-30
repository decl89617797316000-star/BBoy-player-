using Microsoft.Win32;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsMiniPlayer;

public partial class MainWindow : Window
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private IWavePlayer? output;
    private WaveStream? reader;
    private bool changingSelection;
    private string musicFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Mini Player");
    private string? currentUrl;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(musicFolder);
        LoadLocalTracks();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(0, (area.Width - Width) / 2 - 110);
        Top = Math.Max(0, area.Height - Height - 8);
        _ = SearchStations("rock");
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) => StopAudio();

    private void RadioTab_Click(object sender, RoutedEventArgs e)
    {
        RadioView.Visibility = Visibility.Visible; MusicView.Visibility = Visibility.Collapsed;
    }
    private void MusicTab_Click(object sender, RoutedEventArgs e)
    {
        RadioView.Visibility = Visibility.Collapsed; MusicView.Visibility = Visibility.Visible;
    }
    private void YouTube_Click(object sender, RoutedEventArgs e) => OpenInChrome("https://www.youtube.com/");
    private void Vk_Click(object sender, RoutedEventArgs e) => OpenInChrome("https://vk.com/music");
    private void OpenInChrome(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); Status.Text = "Открыто в браузере по умолчанию"; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void SearchRadio_Click(object sender, RoutedEventArgs e) => await SearchStations(RadioSearch.Text.Trim());

    private async Task SearchStations(string query)
    {
        if (!IsLoaded) return;
        try
        {
            Status.Text = "Ищу станции в Radio Browser…";
            var safe = Uri.EscapeDataString(string.IsNullOrWhiteSpace(query) ? "music" : query);
            var json = await http.GetStringAsync($"https://de1.api.radio-browser.info/json/stations/search?name={safe}&limit=80&hidebroken=true&order=clickcount&reverse=true");
            var stations = JsonSerializer.Deserialize<List<RadioStation>>(json) ?? new();
            Stations.ItemsSource = stations.Where(s => !string.IsNullOrWhiteSpace(s.UrlResolved))
                .Select(s => new RadioStation { Name = $"{s.Name}  ·  {s.Country}  ·  {s.Tags}", UrlResolved = s.UrlResolved, Country = s.Country, Tags = s.Tags }).ToList();
            Status.Text = $"Найдено станций: {Stations.Items.Count}";
        }
        catch (Exception ex) { Status.Text = "Не удалось загрузить каталог: " + ex.Message; }
    }

    private async void Stations_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Stations.SelectedItem is RadioStation station && !changingSelection)
            await PlayUrl(station.UrlResolved!, station.Name);
    }

    private async Task PlayUrl(string url, string title)
    {
        StopAudio();
        try
        {
            Status.Text = "Подключаюсь…";
            currentUrl = url;
            await Task.Run(() =>
            {
                var newReader = new MediaFoundationReader(url);
                var newOutput = new WaveOutEvent();
                newOutput.Init(newReader);
                Dispatcher.Invoke(() => { reader = newReader; output = newOutput; output.Play(); });
            });
            NowPlaying.Text = title;
            Status.Text = "Воспроизведение";
        }
        catch (Exception ex) { StopAudio(); Status.Text = "Ошибка потока: " + ex.Message; }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopAudio();
    private void StopAudio()
    {
        try { output?.Stop(); } catch { }
        output?.Dispose(); output = null;
        reader?.Dispose(); reader = null;
    }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Аудиофайлы|*.mp3;*.wav;*.wma;*.aac;*.m4a;*.flac;*.ogg|Все файлы|*.*", Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                var dest = Path.Combine(musicFolder, Path.GetFileName(file));
                if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                {
                    var stem = Path.GetFileNameWithoutExtension(dest);
                    var ext = Path.GetExtension(dest); int i = 1;
                    while (File.Exists(dest)) dest = Path.Combine(musicFolder, $"{stem} ({i++}){ext}");
                    File.Copy(file, dest);
                }
            }
            LoadLocalTracks();
        }
    }
    private void LoadLocalTracks()
    {
        Directory.CreateDirectory(musicFolder);
        Tracks.ItemsSource = Directory.GetFiles(musicFolder).Where(f => new[] { ".mp3", ".wav", ".wma", ".aac", ".m4a", ".flac", ".ogg" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f => new LocalTrack { Path = f, Name = Path.GetFileNameWithoutExtension(f) }).ToList();
    }
    private async void Tracks_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tracks.SelectedItem is LocalTrack track) await PlayUrl(track.Path, track.Name);
    }
    private void OpenMusicFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(musicFolder);
        Process.Start(new ProcessStartInfo(musicFolder) { UseShellExecute = true });
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Выбери прямую ссылку нельзя скачать через диалог — вставь URL ниже", Filter = "Все файлы|*.*" };
        var url = Microsoft.VisualBasic.Interaction.InputBox("Вставь прямую ссылку на аудиофайл, скачивание которого разрешено автором:", "Скачать аудио", "");
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
        { Status.Text = "Нужна корректная HTTP(S)-ссылка"; return; }
        try
        {
            Status.Text = "Скачиваю разрешённый аудиофайл…";
            var bytes = await http.GetByteArrayAsync(uri);
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 6) ext = ".mp3";
            var name = Path.GetFileName(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(name)) name = "audio" + ext;
            var path = Path.Combine(musicFolder, name);
            int n = 1; var stem = Path.GetFileNameWithoutExtension(path);
            while (File.Exists(path)) path = Path.Combine(musicFolder, $"{stem} ({n++}){ext}");
            await File.WriteAllBytesAsync(path, bytes);
            LoadLocalTracks();
            Status.Text = "Файл сохранён в " + musicFolder;
        }
        catch (Exception ex) { Status.Text = "Ошибка загрузки: " + ex.Message; }
    }
}

public sealed class RadioStation
{
    public string Name { get; set; } = "";
    public string? UrlResolved { get; set; }
    public string? Country { get; set; }
    public string? Tags { get; set; }
}
public sealed class LocalTrack
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
}
