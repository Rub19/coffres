using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChestCounter;
using Microsoft.Win32;

namespace Coffres;

public static class Program
{
    public const string Version = "1.1.2";
    const int Port = 47821;
    static readonly string Url = $"http://localhost:{Port}/";
    const string Site = "https://coffres.ethone.dev/";
    const string ContentUrl = "https://raw.githubusercontent.com/LED-N/Fortnite-ChestCounter/main/content/content.json";
    static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Coffres");
    static readonly string StorePath = Path.Combine(DataDir, "store.json");
    static readonly string LogPath = Path.Combine(DataDir, "errors.log");
    static readonly string DefaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static readonly TimeSpan AbandonedLive = TimeSpan.FromMinutes(10), Timeout = TimeSpan.FromMinutes(3);
    static readonly DateTime Started = DateTime.UtcNow;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static readonly object Gate = new();
    static JsonObject store = new();
    static bool live, fortnite;
    static int done, total;
    static string current;
    static JsonObject liveMatch;
    static string liveFile;
    static long liveSize;
    static DateTime liveReadAt;
    static JsonNode content;
    static JsonObject update;
    static string updateError;
    static bool updating;
    static readonly HashSet<string> notifiedHours = new();

    static readonly Dictionary<string, (string fr, string en, string es)> Notes = new()
    {
        ["1.1.2"] = (
            "La partie en cours est détectée comme dans le Chest Counter original (drapeau « en direct » du replay) : le point rouge ne clignote plus et une partie pas finie n'est plus lue trop tôt.",
            "The match in progress is detected like the original Chest Counter (the replay's live flag): the red dot no longer flickers and an unfinished match is no longer read too early.",
            "La partida en curso se detecta como en el Chest Counter original (indicador «en directo» de la repetición): el punto rojo ya no parpadea."),
        ["1.1.1"] = (
            "Coffres est maintenant open source (GPL v3) : code sur github.com/Rub19/coffres, licences dans le pied de page.",
            "Coffres is now open source (GPL v3): code at github.com/Rub19/coffres, licenses in the footer.",
            "Coffres ahora es de código abierto (GPL v3): código en github.com/Rub19/coffres, licencias en el pie de página."),
        ["1.1.0"] = (
            "Overlay natif dans le jeu (Ctrl+Maj+O / Ctrl+Maj+L), icône près de l'horloge, comptage pendant la partie quand c'est possible, notifications de fin de partie et des heures du trésor, mises à jour automatiques, carte de bienvenue, désinstallation.",
            "Native in-game overlay (Ctrl+Shift+O / Ctrl+Shift+L), notification area icon, live counting when possible, end-of-match and Treasure Hour notifications, automatic updates, welcome card, uninstall.",
            "Overlay nativo en el juego (Ctrl+Mayús+O / Ctrl+Mayús+L), icono junto al reloj, conteo en directo cuando es posible, notificaciones de fin de partida y de horas del tesoro, actualizaciones automáticas, bienvenida, desinstalación."),
    };

    public static void Main(string[] args)
    {
        var background = args.Contains("--background");
        var listener = new HttpListener();
        listener.Prefixes.Add(Url);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException)
        {
            if (!background) OpenWindow();
            return;
        }

        Directory.CreateDirectory(DataDir);
        TryDelete(Environment.ProcessPath + ".old");
        try
        {
            if (File.Exists(StorePath)) store = JsonNode.Parse(File.ReadAllText(StorePath))!.AsObject();
        }
        catch (Exception ex)
        {
            Log("store: " + ex.Message);
            File.Copy(StorePath, StorePath + ".bad", true);
        }
        store["folder"] ??= DefaultFolder;
        store["matches"] ??= new JsonObject();
        store["user"] ??= new JsonObject();
        store["app"] ??= new JsonObject();
        L.Set(Lang);

        LoadContent(embeddedOnly: true);
        Tray.Start(Tooltip, Menu, OpenWindow);
        new Thread(ScanLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        new Thread(SlowLoop) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        if (!background) OpenWindow();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Tray.Remove();
        while (listener.IsListening)
        {
            var ctx = listener.GetContext();
            ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
        }
    }

    // ---------------- settings written by the page ----------------
    static JsonNode Pref(string key)
    {
        lock (Gate) return store["user"]?["prefs"]?[key]?.DeepClone();
    }
    static JsonNode OverlayPref(string key)
    {
        lock (Gate) return store["user"]?["prefs"]?["overlay"]?[key]?.DeepClone();
    }
    static bool Flag(JsonNode n, bool fallback) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
    static double Num(JsonNode n, double fallback) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : fallback;
    static string Lang => (string)Pref("lang") ?? L.SystemDefault;
    static string T(string fr, string en, string es) => Lang switch { "en" => en, "es" => es, _ => fr };

    // ---------------- window / tray ----------------
    static void OpenWindow()
    {
        var edge = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(f => Path.Combine(Environment.GetFolderPath(f), @"Microsoft\Edge\Application\msedge.exe"))
            .FirstOrDefault(File.Exists);
        if (edge != null)
            Process.Start(edge, $"--app={Url} --window-size=1180,880");
        else
            Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
    }

    static string Tooltip()
    {
        var s = Numbers();
        return $"Coffres · {s.Total:N0} " + T("coffres", "chests", "cofres") + (live ? T(" · partie en cours", " · match in progress", " · partida en curso") : "");
    }

    static IReadOnlyList<Tray.Item> Menu() => new List<Tray.Item>
    {
        new(T("Ouvrir Coffres", "Open Coffres", "Abrir Coffres"), OpenWindow),
        new("-", () => { }),
        new(T("Overlay dans le jeu", "In-game overlay", "Overlay en el juego"), () => SetOverlayPref("native", !Flag(OverlayPref("native"), true)), Flag(OverlayPref("native"), true)),
        new(T("Afficher / masquer l'overlay  (Ctrl+Maj+O)", "Show / hide the overlay  (Ctrl+Shift+O)", "Mostrar / ocultar el overlay  (Ctrl+Mayús+O)"), NativeOverlay.Toggle),
        new(T("Overlay verrouillé  (Ctrl+Maj+L)", "Overlay locked  (Ctrl+Shift+L)", "Overlay bloqueado  (Ctrl+Mayús+L)"), NativeOverlay.ToggleLock, NativeOverlay.IsLocked),
        new("-", () => { }),
        new(T("Quitter", "Quit", "Salir"), Quit),
    };

    static void SetOverlayPref(string key, bool value)
    {
        lock (Gate)
        {
            var prefs = (store["user"]!["prefs"] ??= new JsonObject()).AsObject();
            var ov = (prefs["overlay"] ??= new JsonObject()).AsObject();
            ov[key] = value;
        }
        Save();
        ApplyOverlay();
    }

    static void Quit()
    {
        NativeOverlay.Stop();
        Tray.Remove();
        Environment.Exit(0);
    }

    // ---------------- storage ----------------
    static void Save()
    {
        string json;
        lock (Gate) json = store.ToJsonString();
        lock (StorePath)
        {
            File.WriteAllText(StorePath + ".tmp", json);
            File.Move(StorePath + ".tmp", StorePath, true);
        }
    }

    public static void Log(string text)
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024) File.Move(LogPath, LogPath + ".1", true);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch { }
    }

    static void TryDelete(string path)
    {
        for (var i = 0; i < 5 && File.Exists(path); i++)
        {
            try { File.Delete(path); } catch { Thread.Sleep(500); }
        }
    }

    // ---------------- replays ----------------
    static void ScanLoop()
    {
        while (true)
        {
            try { Scan(); } catch (Exception ex) { Log("scan: " + ex); }
            try { ApplyOverlay(); } catch (Exception ex) { Log("overlay: " + ex.Message); }
            Thread.Sleep(5000);
        }
    }

    static void Scan()
    {
        string folder;
        lock (Gate) folder = (string)store["folder"];
        fortnite = FortniteRunning();
        if (!Directory.Exists(folder)) { live = false; return; }
        var files = new DirectoryInfo(folder).GetFiles("*.replay");
        var now = DateTime.UtcNow;
        var recording = files.Where(f => now - f.LastWriteTimeUtc < AbandonedLive && IsLive(f.FullName) == true)
            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        live = recording != null;
        if (live) ReadLive(recording); else { liveMatch = null; liveFile = null; }

        List<FileInfo> candidates;
        lock (Gate)
        {
            var matches = store["matches"]!.AsObject();
            candidates = files.Where(f =>
            {
                var k = matches[f.Name];
                return k == null || (int?)k["v"] != Summary.Engine || (k["error"] != null && (long?)k["size"] != f.Length);
            }).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        }
        var todo = candidates.Where(f => Ready(f, now)).ToList();
        foreach (var f in candidates) lastSize[f.FullName] = f.Length;
        total = todo.Count; done = 0;
        foreach (var f in todo)
        {
            current = f.Name;
            var entry = Entry(f, Analyze(f, Timeout));
            if (entry["error"] != null) Log($"{f.Name}: {entry["error"]}");
            bool isNew;
            lock (Gate)
            {
                isNew = store["matches"]![f.Name] == null && f.LastWriteTimeUtc >= Started;
                store["matches"]![f.Name] = entry;
            }
            done++;
            Save();
            if (isNew) Announce(entry);
        }
        total = 0; done = 0; current = null;
        Tray.Refresh();
    }

    static JsonObject Entry(FileInfo f, string result)
    {
        var entry = new JsonObject
        {
            ["file"] = f.Name, ["size"] = f.Length, ["v"] = Summary.Engine,
            ["mtime"] = new DateTimeOffset(f.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
        };
        var parsed = JsonNode.Parse(result)!.AsObject();
        if ((bool)parsed["ok"]!)
            foreach (var (k, v) in parsed["m"]!.AsObject().ToList()) entry[k] = v?.DeepClone();
        else
            entry["error"] = (string)parsed["error"];
        return entry;
    }

    static void ReadLive(FileInfo f)
    {
        if (!Flag(Pref("live"), true)) { liveMatch = null; return; }
        if (f.Name != liveFile) { liveFile = f.Name; liveMatch = null; liveSize = 0; }
        if (f.Length == liveSize || DateTime.UtcNow - liveReadAt < TimeSpan.FromSeconds(30) || KeyMissing(f.FullName)) return;
        liveSize = f.Length;
        liveReadAt = DateTime.UtcNow;
        var e = Entry(f, Analyze(f, TimeSpan.FromSeconds(60)));
        liveMatch = e["error"] == null && (bool?)e["counts"] == true ? e : null;
    }

    static string Analyze(FileInfo file, TimeSpan timeout)
    {
        byte[] bytes;
        using (var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }
        string result = null;
        var worker = new Thread(() => result = Summary.Analyze(new MemoryStream(bytes, false), file.Name))
        {
            IsBackground = true, Priority = ThreadPriority.Lowest,
        };
        worker.Start();
        return worker.Join(timeout) ? result : JsonSerializer.Serialize(new { ok = false, error = "Timeout" });
    }

    static readonly Dictionary<string, long> lastSize = new();

    static bool Ready(FileInfo f, DateTime now)
    {
        var quiet = now - f.LastWriteTimeUtc;
        var flag = IsLive(f.FullName);
        if (flag == true) return quiet > AbandonedLive;
        if (flag == false && quiet > TimeSpan.FromMinutes(1)) return true;
        var stable = lastSize.TryGetValue(f.FullName, out var size) && size == f.Length;
        if (flag == null) return stable && quiet > TimeSpan.FromMinutes(2);
        return stable && quiet >= TimeSpan.FromSeconds(4);
    }

    static bool? IsLive(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != 0x1CA2E27F) return null;
            if (br.ReadUInt32() >= 7)
            {
                var count = br.ReadInt32();
                if (count < 0 || count > 64) return null;
                fs.Seek(count * 20L, SeekOrigin.Current);
            }
            fs.Seek(12, SeekOrigin.Current);
            var len = br.ReadInt32();
            fs.Seek(len < 0 ? -2L * len : len, SeekOrigin.Current);
            return br.ReadUInt32() != 0;
        }
        catch
        {
            return null;
        }
    }

    static bool KeyMissing(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt32() != 0x1CA2E27F) return true;
            if (br.ReadUInt32() >= 7)
            {
                var count = br.ReadInt32();
                if (count < 0 || count > 64) return true;
                fs.Seek(count * 20L, SeekOrigin.Current);
            }
            fs.Seek(12, SeekOrigin.Current);
            var len = br.ReadInt32();
            fs.Seek(len < 0 ? -2L * len : len, SeekOrigin.Current);
            br.ReadUInt32(); br.ReadInt64(); br.ReadUInt32();
            if (br.ReadUInt32() == 0) return false;
            var keyLen = br.ReadInt32();
            if (keyLen <= 0 || keyLen > 256) return true;
            return br.ReadBytes(keyLen).All(b => b == 0);
        }
        catch
        {
            return true;
        }
    }

    static bool FortniteRunning()
    {
        var found = Process.GetProcessesByName("FortniteClient-Win64-Shipping");
        foreach (var p in found) p.Dispose();
        return found.Length > 0;
    }

    // ---------------- figures (same rules as the page) ----------------
    sealed record Figures(int Total, int Team, int Treasures, int Last, bool HasLast, int Session, int SessionMatches);

    static Figures Numbers()
    {
        lock (Gate)
        {
            var all = store["matches"]!.AsObject().Select(kv => kv.Value!.AsObject()).Where(m => m["error"] == null && m["list"] != null).ToList();
            string Key(JsonObject m) => (string)m["epic"] ?? (string)m["player"] ?? "?";
            long St(JsonObject m) => (long?)m["start"] ?? (long?)m["mtime"] ?? 0;
            var wanted = (string)Pref("profile");
            var keys = all.Where(m => m["player"] != null).OrderByDescending(St).Select(Key).ToList();
            var pk = keys.Contains(wanted ?? "\0") ? wanted : keys.FirstOrDefault();
            var counted = all.Where(m => (bool?)m["counts"] == true && (pk == null || m["player"] == null || Key(m) == pk)).OrderByDescending(St).ToList();
            var manual = (store["user"]?["manual"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                .Where(e => ((string)e["profile"] ?? pk) == pk).Sum(e => (int?)e["chests"] ?? 0);
            var startedMs = new DateTimeOffset(Started).ToUnixTimeMilliseconds();
            var session = counted.Where(m => (long?)m["mtime"] >= startedMs).ToList();
            return new Figures(
                Math.Max(0, counted.Sum(m => (int?)m["chests"] ?? 0) + manual),
                counted.Sum(m => (int?)m["teamChests"] ?? 0),
                counted.Sum(m => (m["treasures"] as JsonArray)?.Count ?? 0),
                (int?)counted.FirstOrDefault()?["chests"] ?? 0, counted.Count > 0,
                session.Sum(m => (int?)m["chests"] ?? 0), session.Count);
        }
    }

    static void Announce(JsonObject m)
    {
        if (!Flag(Pref("notifyMatches"), true) || (bool?)m["counts"] != true) return;
        L.Set(Lang);
        var f = Numbers();
        var chests = (int?)m["chests"] ?? 0;
        var treasures = (m["treasures"] as JsonArray)?.Select(t => (string)t?["n"]).Where(n => n != null).ToList() ?? new List<string>();
        Tray.Notify(
            T($"Partie comptée : +{chests} coffres", $"Match counted: +{chests} chests", $"Partida contada: +{chests} cofres"),
            T($"Total : {L.N(f.Total)} coffres", $"Total: {L.N(f.Total)} chests", $"Total: {L.N(f.Total)} cofres")
            + (treasures.Count > 0 ? "\n★ " + string.Join(", ", treasures) : ""));
    }

    // ---------------- in-game overlay ----------------
    static bool overlayStarted;
    static string lastView;

    static void ApplyOverlay()
    {
        L.Set(Lang);
        var want = Flag(OverlayPref("native"), true) && (!Flag(OverlayPref("onlyGame"), true) || fortnite || live);
        if (!want)
        {
            if (overlayStarted) { NativeOverlay.Stop(); overlayStarted = false; }
            return;
        }
        NativeOverlay.SetStyle((string)OverlayPref("style") == "card" ? "classic" : "hud");
        NativeOverlay.SetAppearance((int)Math.Round(Num(OverlayPref("size"), 1) * 100), (int)Math.Round(Num(OverlayPref("opacity"), 1) * 100));
        if (!overlayStarted)
        {
            int? x, y; bool locked;
            lock (Gate)
            {
                x = (int?)store["app"]!["overlayX"]; y = (int?)store["app"]!["overlayY"];
                locked = (bool?)store["app"]!["overlayLocked"] ?? false;
            }
            NativeOverlay.Start(x, y, locked,
                (nx, ny) => { lock (Gate) { store["app"]!["overlayX"] = nx; store["app"]!["overlayY"] = ny; } Save(); },
                l => { lock (Gate) store["app"]!["overlayLocked"] = l; Save(); });
            overlayStarted = true;
            lastView = null;
        }
        var view = View();
        var key = JsonSerializer.Serialize(view);
        if (key != lastView) { lastView = key; NativeOverlay.Update(view); }
    }

    static OverlayView View()
    {
        var f = Numbers();
        var lm = liveMatch;
        var liveChests = (int?)lm?["chests"] ?? 0;
        var liveTreasures = (lm?["treasures"] as JsonArray)?.Count ?? 0;
        var total = f.Total + liveChests;
        var team = f.Team + ((int?)lm?["teamChests"] ?? 0);
        var squad = Flag(OverlayPref("squad"), false) && team > 0 ? L.N(total + team) : null;
        var rows = new List<OverlayRow>
        {
            new(L.T("Session", "Session", "Sesión"), L.Count(f.Session + liveChests, "coffre", "chest", "cofre"),
                Detail: f.SessionMatches > 0 ? $"{L.Count(f.SessionMatches, "partie", "parties", "match", "matches", "partida", "partidas")} · {L.T("moy.", "avg.", "media")} {Math.Round((double)f.Session / f.SessionMatches):0}" : null),
        };
        if (f.HasLast) rows.Add(new(L.T("Dernière", "Last", "Última"), L.Count(f.Last, "coffre", "chest", "cofre")));
        if (squad != null) rows.Add(new(OverlayModel.SquadLabel, L.Count(total + team, "coffre", "chest", "cofre")));
        var treasures = f.Treasures + liveTreasures;
        rows.Add(new(L.T("Trésors", "Treasures", "Tesoros"), $"★ {L.N(treasures)}", Treasure: true));
        var session = L.N(f.Session + liveChests);
        var last = f.HasLast ? L.N(f.Last) : null;
        if (lm != null)
            return new OverlayView(L.N(total), L.N(liveChests), L.T("partie en cours", "current match", "partida actual"), true, L.T("● EN DIRECT", "● LIVE", "● EN DIRECTO"), rows, L.N(treasures), session, last, squad);
        if (live)
            return new OverlayView(L.N(total), "…", L.T("en cours", "in progress", "en curso"), true, L.T("● PARTIE EN COURS", "● MATCH IN PROGRESS", "● PARTIDA EN CURSO"), rows, L.N(treasures), session, last, squad);
        return new OverlayView(L.N(total), last, OverlayModel.LastMatchCaption, false, null, rows, L.N(treasures), session, last, squad);
    }

    // ---------------- hunt data, Treasure Hours, updates ----------------
    static void SlowLoop()
    {
        var nextFetch = DateTime.MinValue;
        while (true)
        {
            try
            {
                if (DateTime.UtcNow >= nextFetch)
                {
                    nextFetch = DateTime.UtcNow.AddHours(3);
                    LoadContent(embeddedOnly: false);
                    CheckUpdate().Wait();
                }
                HourReminder();
                if ((string)Pref("update") == "auto" && update != null && !live && !fortnite && !updating) InstallUpdate().Wait();
            }
            catch (Exception ex) { Log("slow: " + ex.Message); }
            Thread.Sleep(30000);
        }
    }

    static void LoadContent(bool embeddedOnly)
    {
        try
        {
            if (!embeddedOnly)
            {
                content = JsonNode.Parse(Http.GetStringAsync(ContentUrl).Result);
                return;
            }
        }
        catch (Exception ex) { Log("content: " + ex.Message); }
        if (content != null) return;
        using var s = typeof(TreasureCatalog).Assembly.GetManifestResourceStream("content.json")!;
        content = JsonNode.Parse(s);
    }

    static void HourReminder()
    {
        if (!Flag(Pref("notifyHours"), true) || content?["treasureHours"] is not JsonArray hours) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var h in hours.OfType<JsonObject>())
        {
            if (!DateTimeOffset.TryParse((string)h["start"], out var start)) continue;
            var id = (string)h["start"];
            var soon = start - now;
            if (soon > TimeSpan.Zero && soon <= TimeSpan.FromMinutes(10) && notifiedHours.Add(id))
            {
                L.Set(Lang);
                var boosts = (h["boosts"] as JsonObject)?.Select(kv => $"{Kind(kv.Key)} ×{(double)kv.Value!:0.#}") ?? Array.Empty<string>();
                Tray.Notify(T($"Heure du trésor dans {Math.Ceiling(soon.TotalMinutes)} min", $"Treasure Hour in {Math.Ceiling(soon.TotalMinutes)} min", $"Hora del tesoro en {Math.Ceiling(soon.TotalMinutes)} min"), string.Join(" · ", boosts));
            }
        }
    }

    static string Kind(string k) => k switch
    {
        "outfit" => T("Tenue en or", "Gold outfit", "Atuendo dorado"),
        "pirate-shop" => T("Boutique pirate", "Pirate Shop", "Tienda pirata"),
        "car" => T("Véhicule", "Vehicle", "Vehículo"),
        _ => k,
    };

    static async Task CheckUpdate()
    {
        if ((string)Pref("update") == "off") { update = null; return; }
        try
        {
            var m = JsonNode.Parse(await Http.GetStringAsync(Site + "version.json"))!.AsObject();
            update = System.Version.TryParse((string)m["version"], out var v) && v > System.Version.Parse(Version) ? m : null;
            updateError = null;
        }
        catch (Exception ex) { Log("update check: " + ex.Message); }
    }

    static async Task InstallUpdate()
    {
        var m = update;
        if (m == null || updating) return;
        updating = true;
        try
        {
            var exe = Environment.ProcessPath!;
            var bytes = await Http.GetByteArrayAsync(new Uri(new Uri(Site), (string)m["url"]));
            var sha = Convert.ToHexString(SHA256.HashData(bytes));
            if (!sha.Equals((string)m["sha256"], StringComparison.OrdinalIgnoreCase) || bytes.Length < 1_000_000 || bytes[0] != 'M' || bytes[1] != 'Z')
                throw new InvalidDataException("checksum");
            File.WriteAllBytes(exe + ".new", bytes);
            TryDelete(exe + ".old");
            File.Move(exe, exe + ".old");
            File.Move(exe + ".new", exe);
            Log($"updated to {m["version"]}");
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & start \"\" \"{exe}\" --background") { CreateNoWindow = true, UseShellExecute = false });
            Quit();
        }
        catch (Exception ex)
        {
            updateError = ex.Message;
            Log("update: " + ex.Message);
            updating = false;
        }
    }

    static void Uninstall()
    {
        AutoStart = false;
        NativeOverlay.Stop();
        Tray.Remove();
        var exe = Environment.ProcessPath!;
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{DataDir}\" & del /f /q \"{exe}\" \"{exe}.old\"") { CreateNoWindow = true, UseShellExecute = false });
        Environment.Exit(0);
    }

    static bool AutoStart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Coffres") != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue("Coffres", $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("Coffres", false);
        }
    }

    // ---------------- http ----------------
    static void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            res.Headers["Cache-Control"] = "no-store";
            var path = req.Url!.AbsolutePath;
            if (req.HttpMethod == "POST")
            {
                if (req.Headers["X-Coffres"] != "1") { res.StatusCode = 403; return; }
                var body = JsonNode.Parse(new StreamReader(req.InputStream).ReadToEnd()) ?? new JsonObject();
                switch (path)
                {
                    case "/api/user":
                        lock (Gate) store["user"] = body["user"]?.DeepClone() ?? new JsonObject();
                        Save();
                        L.Set(Lang);
                        ThreadPool.QueueUserWorkItem(_ => { try { ApplyOverlay(); } catch { } });
                        break;
                    case "/api/folder":
                        var p = ((string)body["path"])?.Trim().Trim('"');
                        if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) { res.StatusCode = 400; return; }
                        lock (Gate) store["folder"] = p;
                        Save();
                        break;
                    case "/api/autostart":
                        AutoStart = (bool)body["on"];
                        break;
                    case "/api/open":
                        string f;
                        lock (Gate) f = (string)store["folder"];
                        Process.Start("explorer.exe", (string)body["what"] == "data" ? DataDir : Path.Exists(f) ? f : DataDir);
                        break;
                    case "/api/overlay":
                        if ((string)body["action"] == "toggle") NativeOverlay.Toggle();
                        if ((string)body["action"] == "lock") NativeOverlay.ToggleLock();
                        break;
                    case "/api/reanalyze":
                        lock (Gate) foreach (var (_, m) in store["matches"]!.AsObject()) m!["v"] = 0;
                        Save();
                        break;
                    case "/api/update":
                        if ((string)body["action"] == "check") CheckUpdate().Wait();
                        else ThreadPool.QueueUserWorkItem(_ => InstallUpdate().Wait());
                        break;
                    case "/api/uninstall":
                        Write(res, "{}", "application/json");
                        res.Close();
                        Uninstall();
                        break;
                    case "/api/quit":
                        Write(res, "{}", "application/json");
                        res.Close();
                        Quit();
                        break;
                    default:
                        res.StatusCode = 404; return;
                }
                Write(res, "{}", "application/json");
                return;
            }
            if (path == "/api/state")
            {
                string json;
                var notes = Notes.TryGetValue(Version, out var n) ? new JsonObject { ["fr"] = n.fr, ["en"] = n.en, ["es"] = n.es } : null;
                lock (Gate)
                    json = new JsonObject
                    {
                        ["mode"] = "exe", ["version"] = Version, ["notes"] = notes, ["folder"] = (string)store["folder"], ["defaultFolder"] = DefaultFolder,
                        ["folderOk"] = Directory.Exists((string)store["folder"]), ["dataDir"] = DataDir,
                        ["live"] = live, ["fortnite"] = fortnite, ["liveMatch"] = liveMatch?.DeepClone(),
                        ["done"] = done, ["total"] = total, ["current"] = current, ["autostart"] = AutoStart,
                        ["overlay"] = new JsonObject { ["running"] = NativeOverlay.IsRunning, ["visible"] = NativeOverlay.IsVisible, ["locked"] = NativeOverlay.IsLocked, ["hotkeys"] = NativeOverlay.HotkeyRegistered },
                        ["update"] = update?.DeepClone(), ["updateError"] = updateError, ["updating"] = updating,
                        ["user"] = store["user"]!.DeepClone(), ["matches"] = store["matches"]!.DeepClone(),
                    }.ToJsonString();
                Write(res, json, "application/json");
                return;
            }
            if (path is "/" or "/index.html" or "/licenses.txt")
            {
                var html = path != "/licenses.txt";
                using var s = typeof(Program).Assembly.GetManifestResourceStream(html ? "index.html" : "licenses.txt")!;
                using var r = new StreamReader(s);
                Write(res, r.ReadToEnd(), html ? "text/html; charset=utf-8" : "text/plain; charset=utf-8");
                return;
            }
            res.StatusCode = 404;
        }
        catch (Exception ex)
        {
            Log("http: " + ex.Message);
            try { res.StatusCode = 500; } catch { }
        }
        finally
        {
            try { res.Close(); } catch { }
        }
    }

    static void Write(HttpListenerResponse res, string text, string type)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        res.ContentType = type;
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes);
    }
}
