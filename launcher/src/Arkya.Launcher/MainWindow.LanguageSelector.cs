using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Arkya.Launcher;

public partial class MainWindow
{
    private ResourceDictionary? languageResources;
    private bool updatingLanguage;

    private void UpdateLanguageResources()
    {
        var language = CurrentLanguage;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"/Arkya.Launcher;component/Localization/Strings.{language}.xaml", UriKind.Relative)
        };
        if (languageResources is not null) Resources.MergedDictionaries.Remove(languageResources);
        Resources.MergedDictionaries.Add(dictionary);
        languageResources = dictionary;
        Language = XmlLanguage.GetLanguage(language);
        updatingLanguage = true;
        try
        {
            LoginLanguageSelector.SelectedValue = language;
            LauncherLanguageSelector.SelectedValue = language;
        }
        finally { updatingLanguage = false; }
    }

    private async void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingLanguage || selection is null || (sender as ComboBox)?.SelectedValue is not string language ||
            language == CurrentLanguage) return;
        try
        {
            selection.SelectLanguage(language);
            UpdateLanguageResources();
            ApplyLanguage();
            await RefreshLocalizedFeedAsync();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            UpdateLanguageResources();
            ShowError(exception);
        }
    }
}
