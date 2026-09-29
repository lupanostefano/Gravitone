# Gravitone

Un Dock in stile macOS per Windows 11 (WPF, .NET 9).

## Avvio

```bash
dotnet run
```

## Configurazione

`%APPDATA%\Gravitone\config.json` — creato al primo avvio con le app trovate sul PC.

| Chiave | Default | Significato |
| --- | --- | --- |
| `IconSize` | 48 | Dimensione delle icone a riposo |
| `Magnification` | true | Ingrandimento al passaggio del mouse |
| `MagnifiedSize` | 80 | Dimensione massima ingrandita |
| `AutoHide` | false | Nasconde il Dock finché il mouse non tocca il bordo |
| `Edge` | Bottom | `Bottom`, `Left`, `Right` |
| `ReplaceTaskbar` | true | Nasconde la barra delle applicazioni di Windows finché il Dock è aperto |
| `TaskbarHotkey` | Ctrl+Alt+Shift+B | Mostra / nasconde di nuovo la barra di Windows |
| `MenuBar` | true | Barra dei menu in alto |

Ogni elemento ha `Name`, `Target` (percorso, collegamento `.lnk` o `shell:AppsFolder\<AUMID>`) e `Arguments` opzionali.
Errori in `%APPDATA%\Gravitone\log.txt`. Per uscire: clic destro in un punto vuoto del Dock → *Esci da Gravitone*.

## Uso

- Clic su un'app chiusa: la avvia. Su un'app aperta: la porta in primo piano, o la riduce a icona se è già
  in primo piano (con più finestre passa alla successiva).
- Maiusc+clic o clic centrale: nuova finestra. Clic destro sull'icona: elenco delle finestre, Chiudi,
  Rimuovi dal Dock / Aggiungi al Dock.
- Le app aperte ma non fissate compaiono dopo il separatore.
- Icona Start: clic apre il menu Start, clic destro il menu Win+X.
- Clic destro in un punto vuoto: sostituzione della barra, mostra/nascondi la barra di Windows, barra dei menu,
  avvio con Windows.

## Barra dei menu

Striscia in alto come su macOS; le finestre massimizzate partono sotto di essa.

- Logo a sinistra: clic apre Start, clic destro il menu Win+X. Accanto, in grassetto, l'app in primo piano.
- A destra le icone dell'area di notifica delle altre app (clic, doppio clic e clic destro arrivano all'app come
  dalla barra di Windows), poi Wi-Fi, volume e batteria (clic: Impostazioni rapide) e data e ora (clic: notifiche
  e calendario). Windows apre questi due pannelli sempre in basso a destra.

## Barra delle applicazioni

Con `ReplaceTaskbar` il Dock nasconde la barra di Windows e la rimette com'era all'uscita. Se Gravitone viene
chiuso in modo anomalo ci pensa il processo `GravitoneGuard.exe`. In ogni caso, per riaverla subito:

```bash
Gravitone.exe --restore-taskbar
```

## Struttura

- `Interop/` — P/Invoke Win32, DWM, registrazione come app bar
- `Core/` — configurazione, icone della shell, geometria e ingrandimento, avvio app, sostituzione della barra
  (`TaskbarState`, `TaskbarReplacer`, `Guard`)
- `UI/AcrylicWindow` — base delle finestre acrylic; `UI/BackdropWindow` — la piastra del Dock
- `UI/DockWindow` — striscia trasparente con icone, etichette, animazioni, input
- `UI/MenuBarWindow`, `UI/TrayIconsPanel` — barra dei menu; `Core/SystemStatus` — volume, rete, batteria

## Componenti di terze parti

- [ManagedShell](https://github.com/cairoshell/ManagedShell) (Apache-2.0): riceve le icone dell'area di notifica.
