using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ECommons.DalamudServices;
using Microsoft.Data.Sqlite;

namespace AutoDeepDungeon.IPC;

public sealed class PalacePalReader : IIpcSubscriber, IDisposable
{
    public string Name => "PalacePal";

    public const int TypeTrap  = 1;
    public const int TypeHoard = 2;

    // PalacePal's canonical DB file name. Older plugin versions might have used others;
    // we prefer this exact name and only fall back to pattern matching if it's missing.
    private const string CanonicalDbName = "palace-pal.data.sqlite3";
    private const string LocationsTable = "Locations";

    public string? DatabasePath { get; private set; }
    public string? LastError { get; private set; }
    public bool IsReady => DatabasePath != null && File.Exists(DatabasePath);

    // Per-territory cache. PalacePal writes to the DB in the background; our planner only
    // needs a per-floor snapshot so we cache aggressively and invalidate on territory change
    // or manual reload. Failures are cached too so we don't spam the log once per frame.
    private readonly ConcurrentDictionary<uint, IReadOnlyList<Location>> cache = new();
    private readonly ConcurrentDictionary<uint, string> failureCache = new();

    public PalacePalReader()
    {
        ResolveDatabasePath();
    }

    private void ResolveDatabasePath()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "XIVLauncher", "pluginConfigs", "PalacePal");
            if (!Directory.Exists(dir))
            {
                LastError = $"PalacePal plugin config folder not found: {dir}";
                return;
            }

            var canonical = Path.Combine(dir, CanonicalDbName);
            if (File.Exists(canonical))
            {
                DatabasePath = canonical;
            }
            else
            {
                // Last-resort fallback: any *.data.sqlite3 that isn't a daily backup snapshot.
                var candidates = Directory.GetFiles(dir, "*.data.sqlite3")
                    .Where(p => !Path.GetFileName(p).StartsWith("backup-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray();
                if (candidates.Length == 0)
                {
                    LastError = $"Canonical {CanonicalDbName} not found and no non-backup *.data.sqlite3 in {dir}";
                    return;
                }
                DatabasePath = candidates[0];
            }

            Svc.Log.Information($"PalacePal DB resolved: {DatabasePath}");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Svc.Log.Warning($"PalacePal path resolution failed: {ex.Message}");
        }
    }

    public sealed record Location(uint TerritoryType, int Type, Vector3 Position);

    /// <summary>
    /// Caches results per territory. First call for a new territory hits SQLite; subsequent
    /// calls are O(1). Use <see cref="InvalidateCache"/> after PalacePal writes new data.
    /// </summary>
    public IReadOnlyList<Location> QueryByTerritory(uint territoryType)
    {
        if (cache.TryGetValue(territoryType, out var hit))
            return hit;

        if (DatabasePath == null)
            return Array.Empty<Location>();

        if (failureCache.ContainsKey(territoryType))
            return Array.Empty<Location>();

        try
        {
            var connString = BuildConnString();
            using var conn = new SqliteConnection(connString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"SELECT TerritoryType, Type, X, Y, Z FROM {LocationsTable} " +
                "WHERE TerritoryType = $tt AND Type IN (1, 2)";
            cmd.Parameters.AddWithValue("$tt", territoryType);

            var results = new List<Location>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var tt = (uint)reader.GetInt64(0);
                var type = reader.GetInt32(1);
                var x = (float)reader.GetDouble(2);
                var y = (float)reader.GetDouble(3);
                var z = (float)reader.GetDouble(4);
                results.Add(new Location(tt, type, new Vector3(x, y, z)));
            }
            var snapshot = (IReadOnlyList<Location>)results;
            cache[territoryType] = snapshot;
            return snapshot;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            failureCache[territoryType] = ex.Message;
            Svc.Log.Warning($"PalacePal query failed for territory {territoryType}: {ex.Message}");
            return Array.Empty<Location>();
        }
    }

    public int CountAllRows()
    {
        if (DatabasePath == null) return 0;
        try
        {
            using var conn = new SqliteConnection(BuildConnString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {LocationsTable} WHERE Type IN (1, 2)";
            var result = cmd.ExecuteScalar();
            return result == null ? 0 : Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return 0;
        }
    }

    public void InvalidateCache()
    {
        cache.Clear();
        failureCache.Clear();
    }

    private string BuildConnString() => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Shared,
    }.ToString();

    public void Dispose()
    {
        cache.Clear();
        failureCache.Clear();
    }
}
