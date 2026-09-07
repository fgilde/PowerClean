namespace Cleaner.Core.Services;

/// <summary>Einschätzung, ob ein Autostart-Eintrag sinnvoll, unnötig oder verdächtig ist.</summary>
public enum AutostartAdvice
{
    /// <summary>Sollte an bleiben (Sicherheit/Treiber/Hardware).</summary>
    Keep,
    /// <summary>Geschmackssache — nützlich, wenn man das Programm täglich nutzt.</summary>
    Optional,
    /// <summary>Frisst nur Boot-Zeit/RAM — kann fast immer deaktiviert werden.</summary>
    Unnecessary,
    /// <summary>Verdächtig / potenziell unerwünscht — genauer hinschauen.</summary>
    Caution,
    /// <summary>Nicht in der Wissensbasis — im Zweifel per Websuche prüfen.</summary>
    Unknown,
}

public sealed record AutostartAssessment(AutostartAdvice Advice, string Info);

/// <summary>
/// Kleine eingebaute Wissensbasis für bekannte Autostart-Einträge (à la "Should I remove it"):
/// erklärt, was das Programm ist, und gibt eine Empfehlung. Matcht per Substring auf Name und
/// Befehl (case-insensitive). Dazu Heuristiken für verdächtige Muster (Start aus Temp-Ordnern).
/// </summary>
public static class AutostartAdvisor
{
    private sealed record Rule(string[] Patterns, AutostartAdvice Advice, string Info);

    private static readonly Rule[] Rules =
    [
        // ===== Behalten (Sicherheit / Treiber / Eingabegeräte) =====
        new(["securityhealth"], AutostartAdvice.Keep,
            "Windows-Sicherheit (Defender-Taskleistensymbol). Sollte aktiv bleiben."),
        new(["windowsdefender", "msmpeng"], AutostartAdvice.Keep,
            "Microsoft Defender Antivirus — nicht deaktivieren."),
        new(["rtkauduservice", "realtek"], AutostartAdvice.Keep,
            "Realtek-Audiotreiber-Dienst. Ohne ihn können Sound-Funktionen (Klinken-Erkennung u.a.) ausfallen."),
        new(["igfxtray", "igfxem", "intelgraphics"], AutostartAdvice.Keep,
            "Intel-Grafiktreiber-Komponente. Für Hotkeys/Anzeige-Umschaltung zuständig."),
        new(["syntp", "synaptics", "etd.exe", "elantech"], AutostartAdvice.Keep,
            "Touchpad-Treiber (Gesten, Scrollen). Auf Laptops anlassen."),
        new(["nvdisplay", "nvcontainer", "nvidia web helper"], AutostartAdvice.Optional,
            "NVIDIA-Treiberkomponente (Systray/Telemetrie/Updates). Treiber läuft auch ohne — GeForce-Experience-Features dann eingeschränkt."),

        // ===== Optional (nur sinnvoll bei täglicher Nutzung) =====
        new(["onedrive"], AutostartAdvice.Optional,
            "Microsoft OneDrive Cloud-Sync. Anlassen, wenn du OneDrive nutzt — sonst deaktivieren."),
        new(["dropbox"], AutostartAdvice.Optional,
            "Dropbox Cloud-Sync. Nur nötig, wenn Dateien automatisch synchronisiert werden sollen."),
        new(["googledrive", "google drive"], AutostartAdvice.Optional,
            "Google Drive Sync-Client. Nur bei aktiver Nutzung nötig."),
        new(["powertoys"], AutostartAdvice.Optional,
            "Microsoft PowerToys (FancyZones, PowerRename u.a.). Anlassen, wenn du die Tools nutzt."),
        new(["docker desktop"], AutostartAdvice.Optional,
            "Docker Desktop — startet die Container-Umgebung (viel RAM!). Nur für aktive Docker-Nutzer sinnvoll, sonst manuell starten."),
        new(["jetbrains", "toolbox"], AutostartAdvice.Optional,
            "JetBrains Toolbox — verwaltet IDE-Updates. Kann auch manuell gestartet werden."),
        new(["logioptions", "logitune", "lghub", "logiplugin", "logibolt"], AutostartAdvice.Optional,
            "Logitech-Geräte-Software (Maus/Tastatur/Webcam-Konfiguration). Ohne Autostart gehen Sondertasten-Profile evtl. verloren."),
        new(["voicemod"], AutostartAdvice.Optional,
            "Voicemod Stimmenverzerrer. Nur anlassen, wenn du es regelmäßig nutzt."),
        new(["wallpaper engine", "wallpaper32", "wallpaper64"], AutostartAdvice.Optional,
            "Wallpaper Engine — animierte Hintergründe. Kostet etwas GPU/RAM."),
        new(["f.lux", "flux"], AutostartAdvice.Optional,
            "f.lux — passt Bildschirmfarben an die Tageszeit an."),
        new(["sharex"], AutostartAdvice.Optional,
            "ShareX Screenshot-Tool. Anlassen, wenn du per Hotkey Screenshots machst."),
        new(["everything.exe", "voidtools"], AutostartAdvice.Optional,
            "Everything — blitzschnelle Dateisuche. Autostart hält den Index aktuell."),
        new(["obs"], AutostartAdvice.Optional,
            "OBS Studio Streaming/Recording. Nur bei täglicher Nutzung in den Autostart."),
        new(["phone link", "yourphone"], AutostartAdvice.Optional,
            "Microsoft Phone Link (Smartphone-Kopplung). Ohne gekoppeltes Handy unnötig."),

        // ===== Unnötig (Boot-Bremsen, Updater, Launcher) =====
        new(["steam"], AutostartAdvice.Unnecessary,
            "Steam Gaming-Client. Startet auch beim Spielstart automatisch — muss nicht mit Windows booten."),
        new(["epicgameslauncher", "epicwebhelper"], AutostartAdvice.Unnecessary,
            "Epic Games Launcher. Braucht keinen Autostart — startet beim Spielen von selbst."),
        new(["riotclient", "riot client"], AutostartAdvice.Unnecessary,
            "Riot Client (League of Legends/Valorant). Startet beim Spielstart — Autostart unnötig."),
        new(["battle.net", "battlenet"], AutostartAdvice.Unnecessary,
            "Blizzard Battle.net Launcher. Autostart unnötig."),
        new(["eadesktop", "origin"], AutostartAdvice.Unnecessary,
            "EA-App/Origin Launcher. Autostart unnötig."),
        new(["goggalaxy", "gog galaxy"], AutostartAdvice.Unnecessary,
            "GOG Galaxy Launcher. Autostart unnötig."),
        new(["ubisoftconnect", "uplay"], AutostartAdvice.Unnecessary,
            "Ubisoft Connect Launcher. Autostart unnötig."),
        new(["discord"], AutostartAdvice.Unnecessary,
            "Discord Chat. Praktisch für Vielnutzer, aber ein typischer Boot-Verzögerer — bei Bedarf manuell starten."),
        new(["spotify"], AutostartAdvice.Unnecessary,
            "Spotify. Muss nicht mit Windows starten — öffnet sich schnell genug bei Bedarf."),
        new(["ms-teams", "teams.exe", "msteams"], AutostartAdvice.Unnecessary,
            "Microsoft Teams. Startet spürbar den Boot aus — bei Bedarf manuell öffnen (Meetings-Links starten es automatisch)."),
        new(["skype"], AutostartAdvice.Unnecessary,
            "Skype. Kaum noch nötig — Autostart aus."),
        new(["zoom"], AutostartAdvice.Unnecessary,
            "Zoom. Meeting-Links starten Zoom automatisch — Autostart unnötig."),
        new(["slack"], AutostartAdvice.Unnecessary,
            "Slack. Optional — nur bei täglicher Nutzung sinnvoll, sonst Boot-Bremse."),
        new(["whatsapp"], AutostartAdvice.Unnecessary,
            "WhatsApp Desktop. Autostart unnötig, Nachrichten kommen aufs Handy."),
        new(["telegram"], AutostartAdvice.Unnecessary,
            "Telegram Desktop. Autostart optional — eher unnötig."),
        new(["itunes", "ituneshelper"], AutostartAdvice.Unnecessary,
            "iTunesHelper — wartet nur auf angeschlossene Apple-Geräte. Kann fast immer deaktiviert werden."),
        new(["applemobiledevice", "icloud"], AutostartAdvice.Unnecessary,
            "Apple-Hintergrunddienst (iCloud/Geräte-Sync). Nur bei aktiver iCloud-Nutzung anlassen."),
        new(["jusched", "java update"], AutostartAdvice.Unnecessary,
            "Java Update Scheduler — prüft nur auf Java-Updates. Klassischer unnötiger Autostart."),
        new(["adobearm", "adobe acrobat update", "adobegcclient", "creative cloud", "ccxprocess", "coresync"], AutostartAdvice.Unnecessary,
            "Adobe-Updater/Hintergrunddienst. Updates kann man manuell einspielen — deaktivieren spart Boot-Zeit."),
        new(["ccleaner"], AutostartAdvice.Unnecessary,
            "CCleaner-Autostart (Smart Cleaning/Monitoring). Unnötig — Cleaning bei Bedarf starten. (Du hast ja PowerClean.)"),
        new(["microsoftedgeautolaunch", "edgeupdate"], AutostartAdvice.Unnecessary,
            "Microsoft Edge Vorab-Start/Updater. Edge startet auch ohne schnell — kann deaktiviert werden."),
        new(["chrome.exe --no-startup-window", "googlechromeautolaunch", "googleupdate"], AutostartAdvice.Unnecessary,
            "Google Chrome Hintergrund-Start/Updater. Kann deaktiviert werden — Chrome updatet sich beim Start selbst."),
        new(["onenote"], AutostartAdvice.Unnecessary,
            "OneNote Schnellstart/Send-to-OneNote. Meist verzichtbar."),
        new(["cortana"], AutostartAdvice.Unnecessary,
            "Cortana. Kaum noch genutzt — deaktivieren."),
        new(["hpsupport", "hp jumpstart", "canon", "epson", "brother"], AutostartAdvice.Unnecessary,
            "Drucker-Hersteller-Software. Drucken funktioniert auch ohne — nur Status-Popups gehen verloren."),

        // ===== Vorsicht =====
        new(["teamviewer"], AutostartAdvice.Caution,
            "TeamViewer Fernwartung startet mit Windows — dein PC ist damit dauerhaft remote erreichbar. Nur anlassen, wenn gewollt."),
        new(["anydesk"], AutostartAdvice.Caution,
            "AnyDesk Fernwartung startet mit Windows — Remote-Zugriff dauerhaft möglich. Nur anlassen, wenn gewollt."),
        new(["utorrent", "bittorrent"], AutostartAdvice.Caution,
            "Torrent-Client im Autostart: lädt/seedet dauerhaft im Hintergrund (Bandbreite, Datenträger)."),
        new(["mcafee", "wildtangent"], AutostartAdvice.Caution,
            "Häufig vorinstallierte Bundle-Software (Bloatware). Prüfen, ob du sie wirklich nutzt."),
    ];

    /// <summary>Bewertet einen Autostart-Eintrag anhand Wissensbasis + Heuristiken.</summary>
    public static AutostartAssessment Analyze(AutostartEntry entry)
    {
        var haystack = ((entry.Name ?? "") + " " + (entry.Command ?? "")).ToLowerInvariant();

        // Heuristik zuerst: Programme, die aus Temp-Ordnern starten, sind fast nie legitim.
        if (haystack.Contains(@"\temp\") || haystack.Contains(@"\tmp\") || haystack.Contains("%temp%"))
            return new AutostartAssessment(AutostartAdvice.Caution,
                "Startet aus einem Temp-Ordner — das ist für seriöse Software sehr ungewöhnlich und ein typisches Malware-Muster. Unbedingt prüfen (Rechtsklick → Im Web suchen).");

        foreach (var rule in Rules)
            if (rule.Patterns.Any(haystack.Contains))
                return new AutostartAssessment(rule.Advice, rule.Info);

        // Fallback: Beschreibung aus der Datei anzeigen, wenn vorhanden.
        var desc = entry.Description;
        return new AutostartAssessment(AutostartAdvice.Unknown,
            string.IsNullOrWhiteSpace(desc)
                ? "Nicht in der Wissensbasis. Im Zweifel: Rechtsklick → Im Web suchen, bevor du löschst."
                : desc!);
    }
}
