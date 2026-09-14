using System.Windows;
using ValheimWorldSync.Platform.Windows.Configuration;

namespace ValheimWorldSync.Desktop;

public partial class OnboardingWindow : Window
{
    private readonly ProfileStore store;
    public OnboardingWindow(ProfileStore store, string player) { InitializeComponent(); this.store = store; PlayerBox.Text = player; }
    private async void CreateClicked(object sender, RoutedEventArgs e) => await Continue(false);
    private async void JoinClicked(object sender, RoutedEventArgs e) => await Continue(true);
    private async Task Continue(bool join)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PlayerBox.Text)) throw new InvalidDataException("Informe o nome do jogador.");
            var catalog = await store.LoadAsync();
            await store.SaveSettingsAsync(catalog.Settings with { PlayerName = PlayerBox.Text.Trim() });
            var settings = new SettingsWindow(store, join) { Owner = this };
            if (settings.ShowDialog() == true) DialogResult = true;
        }
        catch (Exception exception) { ErrorText.Text = exception.Message; }
    }
}
