using System;
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

    // ClientLocation.Type values per plan.
    public const int TypeTrap  = 1;
    public const int TypeHoard = 2;

    public string? DatabasePath { get; private set; }
    public string? LastError { get; private set; }
    public bool IsReady => DatabasePath != null && File.Exists(DatabasePath);

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

            // Per-account DB file: {account}.data.sqlite3. Pick the most recently modified.
            var candidates = Directory.GetFiles(dir, "*.data.sqlite3");
            if (candidates.Length == 0)
            {
                LastError = $"No *.data.sqlite3 files in {dir}";
                return;
            }

            DatabasePath = candidates.OrderByDescending(File.GetLastWriteTimeUtc).First();
            Svc.Log.Information($"PalacePal DB resolved: {DatabasePath}");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Svc.Log.Warning($"PalacePal path resolution failed: {ex.Message}");
        }
    }

    public sealed record Location(uint TerritoryType, int Type, Vector3 Position);

    public IReadOnlyList<Location> QueryByTerritory(uint territoryType)
    {
        if (DatabasePath == null) return Array.Empty<Location>();

        try
        {
            var connString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
            }.ToString();

            using var conn = new SqliteConnection(connString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT TerritoryType, Type, X, Y, Z FROM ClientLocation " +
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
            return results;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Svc.Log.Warning($"PalacePal query failed for territory {territoryType}: {ex.Message}");
            return Array.Empty<Location>();
        }
    }

    public int CountAllRows()
    {
        if (DatabasePath == null) return 0;
        try
        {
            var connString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
            }.ToString();

            using var conn = new SqliteConnection(connString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ClientLocation WHERE Type IN (1, 2)";
            var result = cmd.ExecuteScalar();
            return result == null ? 0 : System.Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return 0;
        }
    }

    public void Dispose()
    {
        // SqliteConnection instances are per-query via using; nothing to dispose here.
    }
}
