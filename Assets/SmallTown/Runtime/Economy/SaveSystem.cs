using System;
using System.IO;
using UnityEngine;

namespace SmallTown.Economy
{
    public enum LoadStatus
    {
        Loaded,
        NewProfile,
        Migrated,
        RecoveredFromBackup,
        /// <summary>Main file and backup were both unreadable; they were set aside and a new profile started.</summary>
        ResetAfterCorruption,
        /// <summary>The file came from a newer game version; it was set aside untouched and a new profile started.</summary>
        ResetNewerVersion,
    }

    /// <summary>
    /// Versioned JSON save files with a backup. Writes go to a temporary file
    /// first, so a crash mid-save can never destroy the last good save. The
    /// class only knows about a folder and a slot name; swapping it for an
    /// online backend later means replacing this one class.
    /// </summary>
    public sealed class SaveSystem
    {
        readonly string directory;
        readonly string slot;

        public string MainPath => Path.Combine(directory, slot + ".json");
        public string BackupPath => Path.Combine(directory, slot + ".bak.json");
        string TempPath => Path.Combine(directory, slot + ".tmp.json");

        public SaveSystem(string saveDirectory, string slotName = "profile")
        {
            directory = saveDirectory;
            slot = slotName;
        }

        public PlayerProfile Load(int startingMoney, out LoadStatus status)
        {
            if (!File.Exists(MainPath) && !File.Exists(BackupPath))
            {
                status = LoadStatus.NewProfile;
                return PlayerProfile.CreateNew(startingMoney);
            }

            if (TryRead(MainPath, out PlayerProfile profile, out bool newer, out bool migrated))
            {
                status = migrated ? LoadStatus.Migrated : LoadStatus.Loaded;
                return profile;
            }
            if (newer)
            {
                SetAside(MainPath, "newer");
                status = LoadStatus.ResetNewerVersion;
                return PlayerProfile.CreateNew(startingMoney);
            }
            if (TryRead(BackupPath, out profile, out _, out _))
            {
                SetAside(MainPath, "corrupt");
                status = LoadStatus.RecoveredFromBackup;
                return profile;
            }

            SetAside(MainPath, "corrupt");
            SetAside(BackupPath, "corrupt");
            status = LoadStatus.ResetAfterCorruption;
            return PlayerProfile.CreateNew(startingMoney);
        }

        public bool Save(PlayerProfile profile)
        {
            try
            {
                Directory.CreateDirectory(directory);
                profile.version = PlayerProfile.CurrentVersion;
                File.WriteAllText(TempPath, JsonUtility.ToJson(profile, true));
                if (File.Exists(MainPath))
                {
                    File.Copy(MainPath, BackupPath, true);
                    File.Delete(MainPath);
                }
                File.Move(TempPath, MainPath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SmallTown] Save failed: " + e.Message);
                return false;
            }
        }

        public void DeleteAll()
        {
            foreach (string path in new[] { MainPath, BackupPath, TempPath })
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        bool TryRead(string path, out PlayerProfile profile, out bool newerVersion, out bool migrated)
        {
            profile = null;
            newerVersion = false;
            migrated = false;
            try
            {
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return false;
                var loaded = JsonUtility.FromJson<PlayerProfile>(json);
                if (loaded == null || !json.Contains("\"version\"")) return false;
                if (loaded.version > PlayerProfile.CurrentVersion)
                {
                    newerVersion = true;
                    return false;
                }
                if (loaded.version < PlayerProfile.CurrentVersion)
                {
                    Migrate(loaded);
                    migrated = true;
                }
                loaded.Sanitize();
                profile = loaded;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Upgrades older save layouts step by step.</summary>
        static void Migrate(PlayerProfile profile)
        {
            if (profile.version < 2)
            {
                // Version 1 had no lifetime earnings counter; start it from what the player holds.
                profile.lifetimeEarnings = Math.Max(0, profile.wallet) + Math.Max(0, profile.bank);
            }
            profile.version = PlayerProfile.CurrentVersion;
        }

        void SetAside(string path, string label)
        {
            try
            {
                if (!File.Exists(path)) return;
                string target = path + "." + label + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                if (File.Exists(target)) File.Delete(target);
                File.Move(path, target);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SmallTown] Could not set aside " + path + ": " + e.Message);
            }
        }
    }
}
