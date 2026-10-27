using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SongSense.App;
public static class BuildIdentity
{
    public static string ProductVersion => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "sin identificar";
    public static string FileVersion => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "sin identificar";
    public static string Description => $"Song Sense\nVersión {ProductVersion}\nBuild {FileVersion}\n\nVersión previa. Aceptación externa de accesibilidad, escalas, monitores y pruebas con personas pendiente.\n\nLectura local y proveedores elegidos explícitamente. Los análisis solo se envían con una acción expresa. La copia del escritorio y los paquetes se identifican por separado en la evidencia técnica.";
}
public partial class MainWindow
{
    private void ShowAbout(object sender, RoutedEventArgs args)
    {
        var about = new Window { Title = "Acerca de Song Sense", Owner = this, Width = 480, Height = 380, MinWidth = 360, MinHeight = 300, Content = AuxiliaryContent.Create(new { Version = $"Versión {BuildIdentity.ProductVersion}\nBuild {BuildIdentity.FileVersion}\n\nAceptación pendiente\nVersión previa: accesibilidad, escalas, monitores y pruebas con personas requieren aceptación externa.\n\nLectura local y proveedores elegidos explícitamente. Los análisis requieren una acción expresa." }, "Version", "Song Sense", "Versión y estado de aceptación"), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        about.SetResourceReference(BackgroundProperty, "Canvas"); about.SetResourceReference(ForegroundProperty, "Ink");
        about.SourceInitialized += (_, _) => WindowAppearance.Apply(about);
        about.ShowDialog();
        if (IsVisible) ApplicationMenuButton.Focus();
    }
}
