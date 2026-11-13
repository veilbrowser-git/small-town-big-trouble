using System;
using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>
    /// Per-device comfort calibration. Stored in PlayerPrefs because it describes
    /// the person wearing the headset, not their progress in the game.
    /// </summary>
    [Serializable]
    public sealed class RigCalibration
    {
        const string Key = "smalltown.calibration.v1";

        /// <summary>Tests switch this off so they never read or overwrite a real player's calibration.</summary>
        public static bool PersistenceEnabled = true;

        /// <summary>Zero means "use the default from the settings asset".</summary>
        public float standingLength;
        public float armReach;

        public static RigCalibration Load()
        {
            if (!PersistenceEnabled || !PlayerPrefs.HasKey(Key)) return new RigCalibration();
            try
            {
                var loaded = JsonUtility.FromJson<RigCalibration>(PlayerPrefs.GetString(Key));
                return loaded ?? new RigCalibration();
            }
            catch (Exception)
            {
                return new RigCalibration();
            }
        }

        public void Save()
        {
            if (!PersistenceEnabled) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
