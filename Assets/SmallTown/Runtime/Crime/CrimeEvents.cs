using System;
using UnityEngine;

namespace SmallTown.Crime
{
    public enum CrimeKind
    {
        Shoplifting,
        Gunfire,
        ArmedRobbery,
        AssaultOnOfficer,
    }

    public struct CrimeEvent
    {
        public CrimeKind kind;
        public Vector3 position;
        /// <summary>Profile ID of the player who did it. Only that player can become wanted for it.</summary>
        public string offenderId;
        public int wantedLevel;
    }

    /// <summary>
    /// Something illegal just happened in the world. This is not yet a police
    /// report: witnesses who notice it decide whether to call it in.
    /// </summary>
    public static class CrimeEvents
    {
        public static event Action<CrimeEvent> Committed;

        public static void Raise(CrimeKind kind, Vector3 position, int wantedLevel)
        {
            var session = GameSession.Instance;
            Committed?.Invoke(new CrimeEvent
            {
                kind = kind,
                position = position,
                wantedLevel = wantedLevel,
                offenderId = session != null ? session.LocalPlayerId : "",
            });
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Committed = null;
    }

    /// <summary>Anything a bullet can affect.</summary>
    public interface IShootable
    {
        void OnShot(Vector3 point, Vector3 direction);
    }
}
