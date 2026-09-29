using System.Globalization;

namespace Gravitone.Core;

/// <summary>
/// The interface language. The source text is English; <see cref="T(string)"/> looks a text up in the Italian
/// table when Italian is in use and returns the English text otherwise (so a missing translation is never blank).
/// </summary>
internal static class Loc
{
    static bool _italian;

    /// <summary>The language codes the settings offer ("" = follow Windows).</summary>
    public static readonly string[] Languages = ["", "en", "it"];

    /// <param name="setting">"" (the language of Windows), "en" or "it".</param>
    public static void Init(string? setting)
    {
        string code = string.IsNullOrEmpty(setting) ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName : setting;
        _italian = code.Equals("it", StringComparison.OrdinalIgnoreCase);
    }

    public static string T(string english) => _italian && It.TryGetValue(english, out var italian) ? italian : english;

    /// <summary>Same, then <see cref="string.Format(string, object[])"/> with the arguments.</summary>
    public static string T(string english, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(english), args);

    static readonly Dictionary<string, string> It = new()
    {
        // Dock menus
        ["Open"] = "Apri",
        ["Empty Recycle Bin"] = "Svuota il Cestino",
        ["Recycle Bin"] = "Cestino",
        ["Show in File Explorer"] = "Mostra in Esplora file",
        ["File Explorer"] = "Esplora file",
        ["Remove from Dock"] = "Rimuovi dal Dock",
        ["Remove"] = "Rimuovi",
        ["Options"] = "Opzioni",
        ["Keep in Dock"] = "Mantieni nel Dock",
        ["Open at Login"] = "Apri all'avvio",
        ["Recent"] = "Recenti",
        ["New Window"] = "Nuova finestra",
        ["New Window as Administrator"] = "Nuova finestra come amministratore",
        ["Open as Administrator"] = "Apri come amministratore",
        ["Show All Windows"] = "Mostra tutte le finestre",
        ["Hide"] = "Nascondi",
        ["Quit"] = "Esci",
        ["{0} Settings…"] = "Impostazioni di {0}…",
        ["Replace the Windows Taskbar"] = "Sostituisci la barra delle applicazioni",
        ["Hide the Windows Taskbar"] = "Nascondi la barra di Windows",
        ["Show the Windows Taskbar"] = "Mostra la barra di Windows",
        ["Menu Bar"] = "Barra dei menu in alto",
        ["Start with Windows"] = "Avvia con Windows",
        ["Quit {0}"] = "Esci da {0}",
        // Menu bar
        ["Wi-Fi: signal {0}%"] = "Wi-Fi: segnale {0}%",
        ["Ethernet: connected"] = "Ethernet: connesso",
        ["No Internet connection"] = "Nessuna connessione a Internet",
        ["No audio device"] = "Nessun dispositivo audio",
        ["Volume: muted"] = "Volume: disattivato",
        ["Volume: {0}%"] = "Volume: {0}%",
        ["Battery: {0}%"] = "Batteria: {0}%",
        ["Battery: {0}% (charging)"] = "Batteria: {0}% (in carica)",
        ["Battery"] = "Batteria",
        // Stack
        ["This folder is empty"] = "La cartella è vuota",
        ["Open \"{0}\" in File Explorer"] = "Apri «{0}» in Esplora file",
        // Displays
        ["Display {0} ({1} × {2})"] = "Schermo {0} ({1} × {2})",
        ["Display {0} ({1} × {2}), primary"] = "Schermo {0} ({1} × {2}), principale",
        // Settings
        ["{0} Settings"] = "Impostazioni di {0}",
        ["Appearance"] = "Aspetto",
        ["Icon size"] = "Dimensione delle icone",
        ["Magnify icons under the pointer"] = "Ingrandimento al passaggio del mouse",
        ["Magnified size"] = "Dimensione ingrandita",
        ["Genie effect when minimizing and restoring"] = "Effetto genio per riduzione a icona e ripristino",
        ["Position"] = "Posizione",
        ["Position on screen"] = "Posizione sullo schermo",
        ["Bottom"] = "Basso",
        ["Left"] = "Sinistra",
        ["Right"] = "Destra",
        ["Hide automatically"] = "Nascondi automaticamente",
        ["Dock display"] = "Schermo del Dock",
        ["Primary"] = "Principale",
        ["{0} (disconnected)"] = "{0} (scollegato)",
        ["Follow the pointer to the bottom edge of another display"] = "Segui il puntatore sul bordo inferiore di un altro schermo",
        ["System"] = "Sistema",
        ["Replace the Windows taskbar"] = "Sostituisci la barra delle applicazioni di Windows",
        ["Menu bar on top"] = "Barra dei menu in alto",
        ["Menu bar on every display"] = "Barra dei menu su tutti gli schermi",
        ["Win+1 … Win+9 open the Dock's apps"] = "Win+1 … Win+9 per avviare le app del Dock",
        ["Show or hide the Windows taskbar: {0}"] = "Mostra o nascondi la barra di Windows: {0}",
        ["Language"] = "Lingua",
        ["Same as Windows"] = "Come Windows",
        ["English"] = "English",
        ["Italiano"] = "Italiano",
    };
}
