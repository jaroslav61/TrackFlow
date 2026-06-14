using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Serilog;

namespace TrackFlow.Services;

public enum DiagnosticLevel { Info, Warning, Critical, Success }

public class DiagnosticEvent
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DiagnosticLevel Level { get; set; }

    // =====================================================================================
    // UI helper vlastnosti pre DoctorWindow (farebné ikonky pred textom)
    // =====================================================================================

    /// <summary>
    /// Prvý znak správy, ak ide o podporovanú ikonku (napr. ▶ alebo ■).
    /// Inak prázdny string.
    /// </summary>
    public string MessageIcon
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return string.Empty;

            // Momentálne farbíme iba ikonky pre Začiatok/Koniec cesty.
            // Správy generuje napr. OperationViewModel: "▶ Začiatok cesty: ..." a "■ Koniec cesty: ...".
            if (Message.StartsWith("▶", StringComparison.Ordinal)) return "▶";
            if (Message.StartsWith("■", StringComparison.Ordinal)) return "■";
            if (Message.StartsWith("⚠", StringComparison.Ordinal)) return "⚠";

            return string.Empty;
        }
    }

    /// <summary>
    /// URI na ikonku (Image) pre DoctorWindow, ak správa začína podporovaným markerom.
    /// Používame to pre simulované udalosti (napr. "🎮 SIMULOVANÝ SENZOR: ...").
    /// </summary>
    public string MessageIconAsset
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return string.Empty;

            // Simulátor – pokračovanie (sim_cont)
            // Nový formát (bez emoji): Source="Simulátor" + prefix správy.
            if (string.Equals(Source, "Simulátor", StringComparison.Ordinal)
                && Message.StartsWith("SIMULOVANÝ SENZOR:", StringComparison.Ordinal))
                return "avares://TrackFlow/Assets/Appicons/16/sim_cont.png";

            // Spätná kompatibilita: starý marker "🎮 ..."
            if (Message.StartsWith("🎮", StringComparison.Ordinal))
                return "avares://TrackFlow/Assets/Appicons/16/sim_cont.png";

            return string.Empty;
        }
    }

    // Pozn.: Image.Source v Avalonia bez problémov berie priamo IImage, ale pri bindovaní string-u
    // (avares://...) sa nie vždy uplatní type-converter. Preto pre Doctor UI poskytujeme aj IImage.
    private static IImage? _simContIcon;
    private static IImage? TryLoadIcon(string assetUri)
    {
        try
        {
            var uri = new Uri(assetUri);
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Ikonka pre DoctorWindow ako IImage (spoľahlivé pri bindovaní).
    /// </summary>
    public IImage? MessageIconImage
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return null;

            // Simulátor – pokračovanie (sim_cont)
            if (string.Equals(Source, "Simulátor", StringComparison.Ordinal)
                && Message.StartsWith("SIMULOVANÝ SENZOR:", StringComparison.Ordinal))
            {
                _simContIcon ??= TryLoadIcon("avares://TrackFlow/Assets/Appicons/16/sim_cont.png");
                return _simContIcon;
            }

            // Spätná kompatibilita: starý marker "🎮 ..."
            if (Message.StartsWith("🎮", StringComparison.Ordinal))
            {
                _simContIcon ??= TryLoadIcon("avares://TrackFlow/Assets/Appicons/16/sim_cont.png");
                return _simContIcon;
            }

            return null;
        }
    }

    /// <summary>
    /// Správa bez úvodnej ikonky (ak existuje). Ak ikonka nie je podporovaná, vráti pôvodnú Message.
    /// </summary>
    public string MessageText
    {
        get
        {
            if (string.IsNullOrEmpty(Message))
                return string.Empty;

            var displayMessage = TrackFlowDoctorFormatter.FormatMessageForDisplay(Source, Message);

            // 1) textové ikonky (▶/■)
            var icon = MessageIcon;
            if (!string.IsNullOrEmpty(icon))
            {
                if (string.Equals(icon, "⚠", StringComparison.Ordinal))
                {
                    if (displayMessage.StartsWith("⚠️", StringComparison.Ordinal))
                        return displayMessage.Substring("⚠️".Length).TrimStart();
                    if (displayMessage.StartsWith("⚠", StringComparison.Ordinal))
                        return displayMessage.Substring("⚠".Length).TrimStart();
                }

                // odstráň ikonku + prípadné medzery
                return displayMessage.Length > 1
                    ? displayMessage.Substring(1).TrimStart()
                    : string.Empty;
            }

            // 2) obrázkové ikonky: text nemeníme (ikonka je viazaná zvlášť).
            // Výnimka: ak správa používa starý emoji marker, odstránime ho.
            if (Message.StartsWith("🎮", StringComparison.Ordinal))
                return displayMessage.Length > 1 ? displayMessage.Substring(1).TrimStart() : string.Empty;

            return displayMessage;
        }
    }

    public string DisplaySource => TrackFlowDoctorFormatter.TranslateSource(Source);
    public string DisplayLevelText => TrackFlowDoctorFormatter.TranslateLevel(Level);

    public bool HasMessageIcon => !string.IsNullOrEmpty(MessageIcon) && !IsWarningIcon;
    public bool HasMessageIconAsset => !string.IsNullOrEmpty(MessageIconAsset);
    public bool HasMessageIconImage => MessageIconImage != null;
    public bool IsRouteStartIcon => string.Equals(MessageIcon, "▶", StringComparison.Ordinal);
    public bool IsRouteEndIcon => string.Equals(MessageIcon, "■", StringComparison.Ordinal);
    public bool IsWarningIcon => string.Equals(MessageIcon, "⚠", StringComparison.Ordinal);
}

internal static partial class TrackFlowDoctorFormatter
{
    private static readonly IReadOnlyDictionary<string, string> SourceTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RouteActivation"] = "Aktivácia cesty",
        ["Safety"] = "Bezpečnosť",
        ["Routes"] = "Cesty",
        ["Layout"] = "Koľajisko",
        ["Marker"] = "Značka"
    };

    private static readonly IReadOnlyDictionary<string, string> MultiTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["REZ-ENGINE"] = "Rezervačné okno",
        ["ORCHESTRACIA"] = "Orchestrácia",
        ["CESTA"] = "Cesta",
        ["BLOK"] = "Blok",
        ["VYHYBKA"] = "Výhybka",
        ["NAVESTIDLO"] = "Návestidlo",
        ["UVOLNENIE"] = "Uvoľnenie",
        ["CAKANIE"] = "Čakanie",
        ["ARBITRAZ"] = "Arbitráž",
        ["PAT"] = "Patová situácia",
        ["TAIL-CLEAR"] = "Uvoľnenie chvosta",
        ["MOVEMENT"] = "Pohyb"
    };

    private static readonly IReadOnlyDictionary<string, string> InlineKeyTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["action"] = "akcia",
        ["route"] = "cesta",
        ["current"] = "aktuálny",
        ["next"] = "ďalší",
        ["source"] = "zdroj",
        ["state"] = "stav",
        ["detail"] = "detail",
        ["flow"] = "tok",
        ["publisher"] = "publikoval",
        ["version"] = "verzia",
        ["lead"] = "čelo",
        ["frontier"] = "hranica"
    };

    private static readonly IReadOnlyDictionary<string, string> InlineValueTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["advance"] = "posun",
        ["skip"] = "preskočené",
        ["deny"] = "odmietnuté",
        ["deny-behind-frontier"] = "odmietnuté za hranicou",
        ["refresh-skip-unchanged"] = "prekreslenie bez zmeny",
        ["reservation-apply-skip-unchanged"] = "bez zmeny pri prenose rezervácie",
        ["boundary-entry"] = "vstup na hranici blokov",
        ["tail-clear"] = "uvoľnenie chvosta",
        ["tail-clear-force-stop"] = "nútený stoj po uvoľnení chvosta",
        ["clear-route-frontier"] = "vymazanie hranice cesty",
        ["clear-all-frontiers"] = "vymazanie všetkých hraníc",
        ["AdvanceReservationWindow"] = "posun rezervácií",
        ["stoj-pred-segmentom"] = "stoj pred ďalším úsekom",
        ["stoj-po-tail-clear"] = "stoj po uvoľnení chvosta",
        ["stoj-po-prejazde"] = "stoj po prejazde",
        ["traversal-stop"] = "stoj za vlakom",
        ["traversal-go"] = "jazda podľa úseku",
        ["traversal-stop-foreign-owner"] = "stoj - ďalší blok patrí inej ceste",
        ["wait-stop"] = "stoj počas čakania",
        ["route-activate-base"] = "základná aktivácia cesty",
        ["skip-duplicate"] = "duplicitný záznam",
        ["true"] = "áno",
        ["false"] = "nie"
    };

    private static readonly Regex StructuredValueRegex = new(@"(?<key>[^=,]+?)=\[(?<value>.*?)\]", RegexOptions.CultureInvariant);

    public static bool ShouldDisplayInDoctorWindow(DiagnosticEvent entry)
        => ShouldDisplayInDoctorWindow(entry.Source, entry.Message);

    public static bool ShouldDisplayInDoctorWindow(string? source, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return true;

        if (message.StartsWith("[MULTI][ORCHESTRACIA]", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("[MULTI][REZ-ENGINE]", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("[MULTI][CESTA]", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("[MULTI][TAIL-CLEAR]", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("[MULTI][UI-HIGHLIGHT]", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(source, "Návestidlo", StringComparison.OrdinalIgnoreCase)
            && message.StartsWith("Syntéza aspektu pre ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public static string TranslateSource(string source)
        => SourceTranslations.TryGetValue(source, out var translated) ? translated : source;

    public static string TranslateLevel(DiagnosticLevel level)
        => level switch
        {
            DiagnosticLevel.Info => "Informácia",
            DiagnosticLevel.Warning => "Varovanie",
            DiagnosticLevel.Critical => "Kritické",
            DiagnosticLevel.Success => "Úspech",
            _ => level.ToString()
        };

    public static string FormatMessageForDisplay(string? source, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        if (message.StartsWith("[MULTI][", StringComparison.OrdinalIgnoreCase))
            return FormatStructuredMultiMessage(message);

        var translated = TranslateInlineTokens(message);

        if (string.Equals(source, "Safety", StringComparison.OrdinalIgnoreCase)
            && translated.StartsWith("Adjacency for ", StringComparison.OrdinalIgnoreCase))
        {
            translated = translated.Replace("Adjacency for ", "Susedné bloky pre ", StringComparison.OrdinalIgnoreCase);
        }

        if (translated.StartsWith("✅ Centrála ", StringComparison.OrdinalIgnoreCase))
            translated = translated.Replace(" úspešne pripojená", " pripojená", StringComparison.OrdinalIgnoreCase);

        return translated;
    }

    private static string FormatStructuredMultiMessage(string message)
    {
        var tags = new List<string>();
        var index = 0;

        while (index < message.Length && message[index] == '[')
        {
            var end = message.IndexOf(']', index + 1);
            if (end <= index)
                break;

            tags.Add(message.Substring(index + 1, end - index - 1));
            index = end + 1;
        }

        var body = message.Substring(index).TrimStart(' ', ':');
        var mainTag = tags.LastOrDefault(tag => !string.Equals(tag, "MULTI", StringComparison.OrdinalIgnoreCase)) ?? "MULTI";
        var title = tags.Any(tag => string.Equals(tag, "DUPLIKAT", StringComparison.OrdinalIgnoreCase))
            ? "Duplicitné čakanie"
            : MultiTitles.TryGetValue(mainTag, out var mappedTitle)
                ? mappedTitle
                : mainTag;

        var formattedBody = TryFormatStructuredSummary(mainTag, body);
        if (!string.IsNullOrWhiteSpace(formattedBody))
            return formattedBody;

        body = TranslateStructuredBody(body);
        return string.IsNullOrWhiteSpace(body) ? title : $"{title}: {body}";
    }

    private static string? TryFormatStructuredSummary(string mainTag, string body)
    {
        var fields = ParseStructuredFields(body);
        if (fields.Count == 0)
            return null;

        string Field(string name, string fallback = "-")
            => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? TranslateInlineTokens(value)
                : fallback;

        return mainTag.ToUpperInvariant() switch
        {
            "REZ-ENGINE" => TrackFlowDoctorMessages.FormatReservationEngineSummary(
                Field("cesta", Field("route")),
                Field("aktuálny", Field("current")),
                Field("ďalší", Field("next")),
                Field("akcia", Field("action"))),
            "CESTA" => TrackFlowDoctorMessages.FormatRouteSummary(
                Field("cesta", Field("route")),
                Field("stav", Field("state")),
                Field("vlak")),
            "BLOK" => TrackFlowDoctorMessages.FormatBlockSummary(
                Field("blok"),
                Field("stav", Field("state")),
                Field("vlak"),
                Field("cesta", Field("route"))),
            "VYHYBKA" => TrackFlowDoctorMessages.FormatTurnoutSummary(
                Field("výhybka"),
                Field("stav", Field("state")),
                Field("požadovaný"),
                Field("žiada"),
                Field("vlastník")),
            "NAVESTIDLO" => TrackFlowDoctorMessages.FormatSignalSummary(
                Field("návestidlo"),
                Field("stav", Field("state")),
                Field("aspekt"),
                Field("cesta", Field("route")),
                Field("vlak")),
            "UVOLNENIE" => TrackFlowDoctorMessages.FormatCleanupSummary(
                Field("cesta", Field("route")),
                Field("stav", Field("state")),
                Field("rezervácie"),
                Field("výhybky")),
            "CAKANIE" => TrackFlowDoctorMessages.FormatWaitSummary(
                Field("cesta", Field("route")),
                Prefer(Field("blok", string.Empty), Field("prvok", string.Empty), "-"),
                Field("stav", Field("state")),
                Field("dôvod"),
                Field("pokus")),
            "ARBITRAZ" => TrackFlowDoctorMessages.FormatArbitrationSummary(
                Field("typ"),
                Field("prvok"),
                Field("víťaz")),
            "PAT" => TrackFlowDoctorMessages.FormatDeadlockSummary(
                Field("cesta", Field("route")),
                Field("čaká-na"),
                Field("blokuje"),
                Field("pat")),
            _ => null
        };
    }

    private static Dictionary<string, string> ParseStructuredFields(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in StructuredValueRegex.Matches(body))
        {
            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            result[key] = value;

            if (InlineKeyTranslations.TryGetValue(key, out var translatedKey))
                result[translatedKey] = value;
        }

        return result;
    }

    private static string Prefer(string first, string second, string fallback)
        => !string.IsNullOrWhiteSpace(first) && first != "-" ? first
            : !string.IsNullOrWhiteSpace(second) && second != "-" ? second
            : fallback;


    private static string TranslateStructuredBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var translated = StructuredValueRegex.Replace(body, match =>
        {
            var key = match.Groups["key"].Value;
            var value = TranslateInlineTokens(match.Groups["value"].Value);
            var translatedKey = InlineKeyTranslations.TryGetValue(key, out var mappedKey) ? mappedKey : key;
            return $"{translatedKey}=[{value}]";
        });

        return TranslateInlineTokens(translated);
    }

    private static string TranslateInlineTokens(string text)
    {
        var translated = text;

        foreach (var pair in InlineValueTranslations.OrderByDescending(item => item.Key.Length))
        {
            translated = translated.Replace($"[{pair.Key}]", $"[{pair.Value}]", StringComparison.OrdinalIgnoreCase);
            translated = translated.Replace($"'{pair.Key}'", $"'{pair.Value}'", StringComparison.OrdinalIgnoreCase);
            translated = translated.Replace($"={pair.Key}", $"={pair.Value}", StringComparison.OrdinalIgnoreCase);
        }

        translated = translated
            .Replace("MoveLoco", "presun vlaku", StringComparison.OrdinalIgnoreCase)
            .Replace("Route marker click", "Klik na marker cesty", StringComparison.OrdinalIgnoreCase)
            .Replace("Panic stop", "Núdzové zastavenie", StringComparison.OrdinalIgnoreCase)
            .Replace("EmergencyStop", "núdzový STOP", StringComparison.OrdinalIgnoreCase)
            .Replace("Auto-reconnect", "Automatické pripájanie", StringComparison.OrdinalIgnoreCase)
            .Replace("keepalive timeout", "vypršal dohľad spojenia", StringComparison.OrdinalIgnoreCase)
            .Replace("PathElementIds", "trajektórii cesty", StringComparison.OrdinalIgnoreCase)
            .Replace("Layout obsahuje", "Koľajisko obsahuje", StringComparison.OrdinalIgnoreCase)
            .Replace("baseAspect", "základná návesť", StringComparison.OrdinalIgnoreCase)
            .Replace("NextSignalAspect", "ďalšia návesť", StringComparison.OrdinalIgnoreCase)
            .Replace("NextSignalStop", "ďalšie návestidlo na STOJ", StringComparison.OrdinalIgnoreCase)
            .Replace("selectedRoute", "vybraná cesta", StringComparison.OrdinalIgnoreCase)
            .Replace("blocks", "bloky", StringComparison.OrdinalIgnoreCase)
            .Replace("reason", "dôvod", StringComparison.OrdinalIgnoreCase)
            .Replace("caller trace", "sled volania", StringComparison.OrdinalIgnoreCase);

        return translated;
    }
}

public class TrackFlowDoctorService
{
    public const int MaxHistoryEntries = 5000;
    private static readonly TimeSpan DuplicateSuppressionWindow = TimeSpan.FromMilliseconds(350);
    private static readonly TrackFlowDoctorService InstanceValue = new();
    public static TrackFlowDoctorService Instance => InstanceValue;
    private readonly object _dedupeSync = new();
    private string? _lastFingerprint;
    private DateTime _lastFingerprintTimestamp;

    public ObservableCollection<DiagnosticEvent> Events { get; }

    private sealed class ThreadSafeDiagnosticEventCollection : ObservableCollection<DiagnosticEvent>
    {
        private readonly object _sync = new();
        private readonly TrackFlowDoctorService _owner;

        public ThreadSafeDiagnosticEventCollection(TrackFlowDoctorService owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, DiagnosticEvent item)
        {
            lock (_sync)
                base.InsertItem(index, item);
        }

        protected override void RemoveItem(int index)
        {
            lock (_sync)
                base.RemoveItem(index);
        }

        protected override void ClearItems()
        {
            lock (_sync)
            {
                base.ClearItems();
                _owner.ResetDuplicateSuppression();
            }
        }

        protected override void SetItem(int index, DiagnosticEvent item)
        {
            lock (_sync)
                base.SetItem(index, item);
        }

        protected override void MoveItem(int oldIndex, int newIndex)
        {
            lock (_sync)
                base.MoveItem(oldIndex, newIndex);
        }
    }

    public IReadOnlyList<DiagnosticEvent> GetEventsChronologicalSnapshot()
        => Events.Reverse().ToList();

    private TrackFlowDoctorService()
    {
        Events = new ThreadSafeDiagnosticEventCollection(this);
    }

    public string ExportCurrentLogText()
        => ExportEventsText(GetEventsChronologicalSnapshot());

    public string ExportEventsText(IEnumerable<DiagnosticEvent> entries)
    {
        var builder = new StringBuilder();

        foreach (var entry in entries)
            builder.AppendLine(FormatExportLine(entry));

        return builder.ToString();
    }

    public void InsertMarker()
    {
        var markerTimestamp = DateTime.Now;
        AddDiagnosticEvent(
            source: "Marker",
            message: $"========== MARKER ==========\nčas=[{markerTimestamp:yyyy-MM-dd HH:mm:ss}]",
            level: DiagnosticLevel.Info,
            timestamp: markerTimestamp);

        Log.Information("[DOKTOR][Marker] ========== MARKER ========== čas=[{MarkerTime}]", markerTimestamp.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    public string FormatExportLine(DiagnosticEvent entry)
    {
        var message = entry.Message
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);

        return $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\t{FormatLevelForExport(entry.Level)}\t{entry.Source}\t{message}";
    }

    private static string FormatLevelForExport(DiagnosticLevel level)
        => level switch
        {
            DiagnosticLevel.Info => "Informácia",
            DiagnosticLevel.Warning => "Varovanie",
            DiagnosticLevel.Critical => "Kritické",
            DiagnosticLevel.Success => "Úspech",
            _ => level.ToString()
        };

    public void Diagnose(string source, string message, DiagnosticLevel level = DiagnosticLevel.Info)
    {
        AddDiagnosticEvent(source, message, level, DateTime.Now);

        // Zapíšeme aj do klasického logu pre istotu
        Log.Information($"[DOKTOR][{source}] {message}");
    }

    private void AddDiagnosticEvent(string source, string message, DiagnosticLevel level, DateTime timestamp)
    {
        var fingerprint = $"{source}\u001f{level}\u001f{message}";
        lock (_dedupeSync)
        {
            if (string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal)
                && (timestamp - _lastFingerprintTimestamp) <= DuplicateSuppressionWindow)
            {
                return;
            }

            _lastFingerprint = fingerprint;
            _lastFingerprintTimestamp = timestamp;
        }

        void AddToCollection()
        {
            Events.Insert(0, new DiagnosticEvent
            {
                Timestamp = timestamp,
                Source = source,
                Message = message,
                Level = level
            });

            if (Events.Count > MaxHistoryEntries)
                Events.RemoveAt(MaxHistoryEntries); // Limit histórie
        }

        // Zabezpečíme zápis do kolekcie na UI vlákne.
        // Pozn.: v unit testoch / headless kontexte nemusí byť Avalonia Dispatcher inicializovaný.
        try
        {
            var ui = Dispatcher.UIThread;
            if (ui.CheckAccess())
                AddToCollection();
            else
                ui.Post(AddToCollection);
        }
        catch
        {
            // Fallback – best effort (napr. počas unit testov).
            AddToCollection();
        }
    }

    private void ResetDuplicateSuppression()
    {
        lock (_dedupeSync)
        {
            _lastFingerprint = null;
            _lastFingerprintTimestamp = default;
        }
    }
}