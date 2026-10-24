using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Arkya.Launcher.Core;

namespace Arkya.Launcher;

public partial class MainWindow
{
    private Func<string>? statusText;
    private Func<string>? availableText;
    private Func<string>? releaseNotesText;
    private Func<string>? launcherUpdateText;

    private string CurrentLanguage => selection?.Preferences.Language ?? "fr";
    private CultureInfo DisplayCulture => CultureInfo.GetCultureInfo(CurrentLanguage == "en" ? "en-GB" : "fr-FR");
    private string T(string french, string english) => CurrentLanguage == "en" ? english : french;

    private void ApplyLanguage()
    {
        if (statusText is not null) StatusLabel.Text = LoginStatus.Text = statusText();
        if (availableText is not null) AvailableLabel.Text = availableText();
        if (releaseNotesText is not null) ReleaseNotes.Text = releaseNotesText();
        if (launcherUpdateText is not null) LauncherUpdateStatus.Text = launcherUpdateText();
        UpdateLauncherUpdateButton();
        UpdateChannel();
        UpdateButtons();
        RenderFeed();
    }

    private async Task RefreshLocalizedFeedAsync()
    {
        feedGeneration++;
        feedRequest?.Cancel();
        posts = [];
        feedFailed = false;
        feedMessage = null;
        ArticlePanel.Visibility = Visibility.Collapsed;
        ArticleCaption.Text = ArticleTitle.Text = ArticleSummary.Text = ArticleBody.Text = "";
        RenderFeed();
        await RefreshFeedAsync();
    }

    private void SetStatus(Func<string> message)
    {
        statusText = message;
        StatusLabel.Text = LoginStatus.Text = message();
    }

    private void SetAvailableText(Func<string> message)
    {
        availableText = message;
        AvailableLabel.Text = message();
    }

    private void SetReleaseNotesText(Func<string> message)
    {
        releaseNotesText = message;
        ReleaseNotes.Text = message();
    }

    private void SetLauncherUpdateText(Func<string> message)
    {
        launcherUpdateText = message;
        LauncherUpdateStatus.Text = message();
    }

    private void UpdateLauncherUpdateButton() => LauncherUpdateButton.Content = LauncherUpdateInstaller.IsInstalledLauncher(AppContext.BaseDirectory)
        ? T("Mettre à jour et redémarrer", "Update and restart") : T("Installer le nouveau launcher", "Install the new launcher");

    private string FormatBytes(long value)
    {
        var gigabytes = value >= 1024L * 1024 * 1024;
        var amount = value / (gigabytes ? 1024d * 1024 * 1024 : 1024d * 1024);
        return amount.ToString("0.0", DisplayCulture) + " " + (gigabytes ? T("Go", "GB") : T("Mo", "MB"));
    }

    private string LocalizeProgress(string message) => message switch
    {
        "Téléchargement du jeu" => T(message, "Downloading the game"),
        "Vérification et installation du jeu" => T(message, "Verifying and installing the game"),
        "Installation du jeu" => T(message, "Installing the game"),
        "Le jeu est prêt" => T(message, "The game is ready"),
        "Mise à jour du launcher" => T(message, "Updating the launcher"),
        _ => T(message, "Preparing files")
    };

    private void ShowError(Exception exception) => SetStatus(() => exception switch
    {
        HttpRequestException => T("Connexion au serveur impossible. Vérifiez votre connexion et réessayez.",
            "Unable to connect to the server. Check your connection and try again."),
        LauncherAuthenticationException { SessionInvalid: true } => T(exception.Message,
            "Your session is no longer valid. Sign in with Discord again."),
        LauncherAuthenticationException => T(exception.Message,
            "Sign-in could not be completed. Please try signing in with Discord again."),
        UnauthorizedAccessException => T(exception.Message,
            "Access to a required file or folder was denied. Check its permissions or choose another game folder."),
        InvalidDataException or JsonException => T(exception.Message,
            "The downloaded or installed files are invalid. Please check for updates and try again."),
        CryptographicException => T(exception.Message,
            "Saved sign-in information could not be read. Please sign in with Discord again."),
        IOException => T(exception.Message,
            "A required file could not be read or written. Check available disk space and try again."),
        Win32Exception => T(exception.Message,
            "Windows could not start the requested application. Please try again."),
        ArgumentException => T(exception.Message,
            "The selected location or configuration is invalid. Please check it and try again."),
        _ => T(exception.Message, "The operation could not be completed. Please try again.")
    });
}
