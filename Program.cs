using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HOI4AchievementPatcher;

// Lets Hearts of Iron IV grant achievements with checksum-changing mods, and optionally without Ironman.
//
// Two changes, each with a backup next to the original:
//  1. hoi4.exe: the startup code compares the game checksum with the vanilla one and stores the result in the
//     achievement manager (no match = no achievements). The `sete bl` after that compare becomes `mov bl, 1`.
//     This does not change the checksum itself, so multiplayer still works.
//  2. common/achievements.txt (only with --remove-ironman): every achievement's `possible` block requires
//     `is_ironman = yes`; those lines are commented out. The is_ironman trigger itself is left alone, so events
//     that check Ironman behave as before. The file is part of the game checksum, so this breaks multiplayer.
//
// Cheats (console commands), difficulty, custom difficulty, start date and game rules still block achievements.
// Only hoi4.exe and common/achievements.txt are touched; nothing else in the game folder is written.
internal static class Program
{
    private const string ExeName = "hoi4.exe";
    private const string AchievementsPath = "common/achievements.txt";
    private const string BackupSuffix = ".achpatch.bak";
    private const string IronmanMarker = "# is_ironman = yes  # removed by HOI4AchievementPatcher";

    // lea rcx,[rip+checksum]; call strcmp; test eax,eax; sete bl; call GetAchievementsManager;
    // movzx edx,bl; mov rcx,rax; call SetChecksumOk
    private static readonly int?[] ChecksumPattern = Parse(
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 85 C0 0F 94 C3 E8 ?? ?? ?? ?? 0F B6 D3 48 8B C8 E8");
    private static readonly int?[] ChecksumPatternPatched = Parse(
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 85 C0 B3 01 90 E8 ?? ?? ?? ?? 0F B6 D3 48 8B C8 E8");
    private const int SeteOffset = 14;
    private static readonly byte[] Patched = [0xB3, 0x01, 0x90]; // mov bl, 1; nop  (was sete bl)

    private static readonly Regex IronmanLine =
        new(@"^([ \t]*)is_ironman[ \t]*=[ \t]*yes[ \t]*$", RegexOptions.Multiline);

    private static int Main(string[] args)
    {
        var code = Run(args);
        // Started by double-click: keep the window open so the result can be read.
        if (args.Length == 0 && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to close.");
            Console.ReadKey(true);
        }
        return code;
    }

    private static int Run(string[] args)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "patch";
        if (command is "help" or "-h" or "--help" or "/?")
        {
            PrintHelp();
            return 0;
        }
        var removeIronman = args.Contains("--remove-ironman");
        var gameDir = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? FindGameDir();
        if (gameDir == null || !File.Exists(Path.Combine(gameDir, ExeName)))
        {
            Console.Error.WriteLine("Hearts of Iron IV not found. Pass the game folder: " +
                                    "HOI4AchievementPatcher patch \"D:\\...\\Hearts of Iron IV\"");
            return 2;
        }
        Console.WriteLine($"Game folder: {gameDir}");

        try
        {
            switch (command)
            {
                case "patch":
                    EnsureGameClosed(gameDir);
                    PatchExe(gameDir);
                    if (removeIronman) PatchAchievements(gameDir);
                    else Console.WriteLine("achievements.txt: left alone, Ironman still required (--remove-ironman changes that)");
                    Console.WriteLine("Done. Run the patcher again after every game update.");
                    return 0;
                case "restore":
                    EnsureGameClosed(gameDir);
                    Restore(Path.Combine(gameDir, ExeName));
                    Restore(Path.Combine(gameDir, AchievementsPath));
                    return 0;
                case "status":
                    Status(gameDir);
                    return 0;
                default:
                    PrintHelp();
                    return 2;
            }
        }
        catch (Exception e) when (e is PatchException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Error: " + e.Message);
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            HOI4AchievementPatcher [patch|restore|status] [game folder] [--remove-ironman]

              patch    (default) allow achievements with mods; a backup is kept
              restore  put the backed-up hoi4.exe and common/achievements.txt back
              status   show what is patched

              --remove-ironman  also allow achievements without Ironman. This changes the game checksum,
                                so you no longer match other players in multiplayer.
            """);
    }

    private static void PatchExe(string gameDir)
    {
        var path = Path.Combine(gameDir, ExeName);
        var data = File.ReadAllBytes(path);
        if (FindAll(data, ChecksumPatternPatched).Count == 1)
        {
            Console.WriteLine("hoi4.exe: already patched");
            return;
        }

        var matches = FindAll(data, ChecksumPattern).Where(m => PointsAtChecksum(data, m)).ToList();
        if (matches.Count != 1)
            throw new PatchException($"found {matches.Count} candidates for the mods check in hoi4.exe (expected 1). " +
                                     "This game version is not supported yet; nothing was changed.");

        Backup(path);
        Array.Copy(Patched, 0, data, matches[0] + SeteOffset, Patched.Length);
        File.WriteAllBytes(path, data);
        Console.WriteLine($"hoi4.exe: mods check patched at file offset 0x{matches[0] + SeteOffset:X}");
    }

    // The lea at the start of the match must load a 32-character hex string: the vanilla checksum.
    private static bool PointsAtChecksum(byte[] data, int match)
    {
        var pe = new PeImage(data);
        var leaRva = pe.OffsetToRva(match);
        if (leaRva == null) return false;
        var target = pe.RvaToOffset(leaRva.Value + 7 + BitConverter.ToInt32(data, match + 3));
        if (target == null || target.Value + 33 > data.Length) return false;
        for (var i = 0; i < 32; i++)
            if (!Uri.IsHexDigit((char)data[target.Value + i])) return false;
        return data[target.Value + 32] == 0;
    }

    private static void PatchAchievements(string gameDir)
    {
        var path = Path.Combine(gameDir, AchievementsPath);
        if (!File.Exists(path)) throw new PatchException($"{AchievementsPath} not found");
        var text = File.ReadAllText(path, Encoding.UTF8);
        var count = IronmanLine.Matches(text).Count;
        if (count == 0)
        {
            Console.WriteLine(text.Contains(IronmanMarker)
                ? "achievements.txt: already patched"
                : "achievements.txt: no Ironman requirement found, left alone");
            return;
        }
        Backup(path);
        File.WriteAllText(path, IronmanLine.Replace(text, "$1" + IronmanMarker), new UTF8Encoding(false));
        Console.WriteLine($"achievements.txt: Ironman requirement removed from {count} achievements");
    }

    private static void Status(string gameDir)
    {
        var data = File.ReadAllBytes(Path.Combine(gameDir, ExeName));
        var exe = FindAll(data, ChecksumPatternPatched).Count == 1 ? "patched"
            : FindAll(data, ChecksumPattern).Any(m => PointsAtChecksum(data, m)) ? "not patched"
            : "unknown game version";
        Console.WriteLine($"hoi4.exe: {exe}");

        var path = Path.Combine(gameDir, AchievementsPath);
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        var ach = IronmanLine.IsMatch(text) ? "Ironman required"
            : text.Contains(IronmanMarker) ? "patched" : "unknown";
        Console.WriteLine($"achievements.txt: {ach}");
    }

    // Only called on an unpatched file, so overwriting an old backup keeps it current after a game update.
    private static void Backup(string path)
    {
        var backup = path + BackupSuffix;
        File.Copy(path, backup, true);
        Console.WriteLine($"  backup: {backup}");
    }

    private static void Restore(string path)
    {
        var backup = path + BackupSuffix;
        if (!File.Exists(backup))
        {
            Console.WriteLine($"{Path.GetFileName(path)}: no backup, nothing to restore");
            return;
        }
        File.Copy(backup, path, true);
        File.Delete(backup);
        Console.WriteLine($"{Path.GetFileName(path)}: restored");
    }

    private static void EnsureGameClosed(string gameDir)
    {
        var exe = Path.GetFullPath(Path.Combine(gameDir, ExeName));
        foreach (var process in Process.GetProcessesByName("hoi4"))
        {
            string? path;
            try { path = process.MainModule?.FileName; }
            catch (Exception) { path = null; } // no access: assume it is this game
            if (path == null || string.Equals(Path.GetFullPath(path), exe, StringComparison.OrdinalIgnoreCase))
                throw new PatchException("Hearts of Iron IV is running. Close it and try again.");
        }
    }

    private static string? FindGameDir()
    {
        string[] steamDirs = [@"C:\Steam", @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam"];
        foreach (var steam in steamDirs)
        {
            var dir = Path.Combine(steam, @"steamapps\common\Hearts of Iron IV");
            if (File.Exists(Path.Combine(dir, ExeName))) return dir;
        }

        // Other Steam libraries are listed in libraryfolders.vdf.
        foreach (var steam in steamDirs)
        {
            var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var dir = Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), @"steamapps\common\Hearts of Iron IV");
                if (File.Exists(Path.Combine(dir, ExeName))) return dir;
            }
        }
        return null;
    }

    private static int?[] Parse(string pattern) =>
        pattern.Split(' ').Select(b => b == "??" ? (int?)null : Convert.ToInt32(b, 16)).ToArray();

    private static List<int> FindAll(byte[] data, int?[] pattern)
    {
        var result = new List<int>();
        var first = (byte)pattern[0]!.Value;
        for (var i = Array.IndexOf(data, first); i >= 0 && i <= data.Length - pattern.Length;
             i = Array.IndexOf(data, first, i + 1))
        {
            var ok = true;
            for (var j = 1; j < pattern.Length && ok; j++)
                ok = pattern[j] == null || data[i + j] == pattern[j];
            if (ok) result.Add(i);
        }
        return result;
    }

    private sealed class PatchException(string message) : Exception(message);

    // Just enough PE parsing to map file offsets to RVAs and back.
    private sealed class PeImage
    {
        private readonly List<(uint Rva, uint Size, uint Raw, uint RawSize)> _sections = [];

        public PeImage(byte[] data)
        {
            var pe = BitConverter.ToInt32(data, 0x3C);
            var count = BitConverter.ToUInt16(data, pe + 6);
            var optSize = BitConverter.ToUInt16(data, pe + 20);
            var table = pe + 24 + optSize;
            for (var i = 0; i < count; i++)
            {
                var s = table + i * 40;
                _sections.Add((BitConverter.ToUInt32(data, s + 12), BitConverter.ToUInt32(data, s + 8),
                    BitConverter.ToUInt32(data, s + 20), BitConverter.ToUInt32(data, s + 16)));
            }
        }

        public long? OffsetToRva(long offset)
        {
            foreach (var s in _sections)
                if (offset >= s.Raw && offset < s.Raw + s.RawSize) return s.Rva + (offset - s.Raw);
            return null;
        }

        public int? RvaToOffset(long rva)
        {
            foreach (var s in _sections)
                if (rva >= s.Rva && rva - s.Rva < s.RawSize) return (int)(s.Raw + (rva - s.Rva));
            return null;
        }
    }
}
