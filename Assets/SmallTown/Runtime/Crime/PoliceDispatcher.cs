using System.Collections.Generic;
using SmallTown.Economy;
using UnityEngine;

namespace SmallTown.Crime
{
    /// <summary>
    /// Decides how much police the town sends and what each officer is told.
    /// Runs on the host only. Level 1: the local beat cop looks into it.
    /// Level 2: three officers and the patrol car. Level 3: five officers and a
    /// roadblock on the main street. The response is sized to the most wanted
    /// player, but every player's wanted level is tracked separately, and an
    /// officer only ever chases a wanted player it can actually see.
    /// </summary>
    public sealed class PoliceDispatcher : MonoBehaviour
    {
        [SerializeField] PoliceOfficer officerPrefab;
        [SerializeField] Transform[] spawnPoints = new Transform[0];
        [SerializeField] Transform[] patrolPoints = new Transform[0];
        [SerializeField] PatrolCar patrolCar;
        [SerializeField] GameObject[] roadblocks = new GameObject[0];
        [SerializeField] ArrestController arrest;
        [Min(1)] public int poolSize = 5;

        readonly List<PoliceOfficer> officers = new List<PoliceOfficer>();
        readonly HashSet<PlayerState> hooked = new HashSet<PlayerState>();
        GameSession session;
        float sirenAt;
        int strength;

        public IReadOnlyList<PoliceOfficer> Officers => officers;
        /// <summary>The highest wanted level in the session; it sets the size of the response.</summary>
        public int Strength => strength;
        public int ActiveOfficers
        {
            get
            {
                int count = 0;
                foreach (PoliceOfficer officer in officers)
                {
                    if (officer.State != OfficerState.Inactive) count++;
                }
                return count;
            }
        }

        public void Configure(PoliceOfficer prefab, Transform[] spawns, Transform[] patrol, PatrolCar car, GameObject[] blocks, ArrestController arrestController)
        {
            officerPrefab = prefab;
            spawnPoints = spawns;
            patrolPoints = patrol;
            patrolCar = car;
            roadblocks = blocks;
            arrest = arrestController;
        }

        void Start()
        {
            if (!Net.IsHost)
            {
                enabled = false;
                return;
            }
            session = GameSession.Instance;
            session.PlayerRemoved += OnPlayerRemoved;
            CrimeEvents.Committed += OnCrimeCommitted;

            for (int i = 0; i < poolSize; i++)
            {
                Transform spawn = spawnPoints[i % spawnPoints.Length];
                PoliceOfficer officer = Net.Spawn(officerPrefab, spawn.position, spawn.rotation);
                officer.name = "Officer " + (i + 1);
                officer.Setup(this, patrolPoints, spawn.position);
                officer.Deactivate();
                officers.Add(officer);
            }
            foreach (GameObject block in roadblocks)
            {
                if (block != null) block.SetActive(false);
            }
            // One officer always walks the beat.
            officers[0].Activate(spawnPoints[0].position);
        }

        void OnDestroy()
        {
            if (session != null) session.PlayerRemoved -= OnPlayerRemoved;
            CrimeEvents.Committed -= OnCrimeCommitted;
        }

        void Hook(PlayerState player)
        {
            if (!hooked.Add(player)) return;
            player.Wanted.LevelChanged += level => OnLevelChanged(player, level);
            player.Wanted.CrimeReported += (level, position) => OnCrimeReported(position);
            player.Wanted.Escaped += level => Refresh();
        }

        void OnPlayerRemoved(PlayerState player)
        {
            hooked.Remove(player);
            foreach (PoliceOfficer officer in officers)
            {
                if (officer.Focus == player) officer.Focus = null;
            }
            Refresh();
        }

        void Update()
        {
            if (session == null) return;
            IReadOnlyList<PlayerState> players = session.Players;
            for (int i = 0; i < players.Count; i++) Hook(players[i]);
            GameBalance balance = session.Balance;
            float now = Time.time;

            foreach (PoliceOfficer officer in officers)
            {
                if (officer.State == OfficerState.Inactive) continue;
                PlayerState suspect = ChooseSuspect(officer, players, balance);
                officer.Focus = suspect;
                if (suspect != null)
                {
                    officer.Tick(Time.deltaTime, true, suspect.Head, suspect.Feet, suspect.Wanted.Level);
                }
                else
                {
                    officer.Tick(Time.deltaTime, false, officer.transform.position + Vector3.up * 100f, Vector3.zero, 0);
                }
            }

            // Each wanted player escapes on their own clock: it runs only while no officer can see them.
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState player = players[i];
                if (player.Busy && now >= player.BusyUntil) player.Busy = false;
                if (!player.Wanted.IsWanted || player.Busy) continue;
                bool seen = false;
                foreach (PoliceOfficer officer in officers)
                {
                    if (officer.State == OfficerState.Inactive || officer.State == OfficerState.Stunned) continue;
                    if (officer.CanSee(player.Head, balance))
                    {
                        seen = true;
                        break;
                    }
                }
                player.Wanted.Tick(Time.deltaTime, seen, player.Feet);
            }

            if (strength >= 2 && now >= sirenAt)
            {
                sirenAt = now + 1.6f;
                Vector3 source = patrolCar != null ? patrolCar.transform.position : transform.position;
                GameFeedback.PlayShared(FeedbackSound.Siren, source, 0.7f);
            }
        }

        /// <summary>The wanted player this officer should deal with: one it can see if possible, else the nearest.</summary>
        PlayerState ChooseSuspect(PoliceOfficer officer, IReadOnlyList<PlayerState> players, GameBalance balance)
        {
            PlayerState best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerState player = players[i];
                if (!player.Wanted.IsWanted || player.Busy) continue;
                float distance = (player.Feet - officer.transform.position).sqrMagnitude;
                bool visible = officer.State != OfficerState.Stunned && officer.CanSee(player.Head, balance);
                // Seeing someone always beats merely knowing someone is out there.
                float score = visible ? distance : distance + 1e6f;
                // Stick with the current suspect unless another is clearly a better choice.
                if (player == officer.Focus) score *= 0.6f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = player;
                }
            }
            return best;
        }

        void OnCrimeCommitted(CrimeEvent crime)
        {
            // An officer who sees a crime happen reports it at once.
            if (session == null || crime.offender == null) return;
            foreach (PoliceOfficer officer in officers)
            {
                if (officer.State == OfficerState.Inactive || officer.State == OfficerState.Stunned) continue;
                if (!officer.CanSee(crime.position + Vector3.up * 0.5f, session.Balance)) continue;
                crime.offender.Wanted.ReportCrime(crime.wantedLevel, crime.position);
                return;
            }
        }

        void OnCrimeReported(Vector3 position)
        {
            foreach (PoliceOfficer officer in officers) officer.Investigate(position);
        }

        void OnLevelChanged(PlayerState player, int level)
        {
            if (level > 0)
            {
                player.Tell(level == 1 ? "WANTED: a cop is looking for you." : level == 2 ? "WANTED: squad and patrol car inbound!" : "WANTED: every cop in town. Roadblocks are up!");
            }
            Refresh(player);
        }

        /// <summary>Sizes the response to the most wanted player in the session.</summary>
        void Refresh(PlayerState trigger = null)
        {
            int highest = 0;
            PlayerState mostWanted = null;
            foreach (PlayerState player in session.Players)
            {
                if (player.Wanted.Level > highest)
                {
                    highest = player.Wanted.Level;
                    mostWanted = player;
                }
            }
            bool grew = highest > strength;
            strength = highest;

            if (highest == 0)
            {
                foreach (PoliceOfficer officer in officers) officer.GiveUp();
                if (patrolCar != null) patrolCar.Return();
                SetRoadblocks(false);
                return;
            }

            GameBalance balance = session.Balance;
            Vector3 lastKnown = (trigger != null && trigger.Wanted.IsWanted ? trigger : mostWanted).Wanted.LastKnownPosition;
            int needed = Mathf.Min(officers.Count, balance.OfficersFor(highest));
            int active = ActiveOfficers;
            for (int i = 0; i < officers.Count && active < needed; i++)
            {
                if (officers[i].State != OfficerState.Inactive) continue;
                officers[i].Activate(spawnPoints[i % spawnPoints.Length].position);
                active++;
            }
            if (grew || trigger != null)
            {
                foreach (PoliceOfficer officer in officers) officer.Investigate(lastKnown);
            }

            if (patrolCar != null)
            {
                if (highest >= 2) patrolCar.Dispatch(lastKnown);
                else patrolCar.Return();
            }
            SetRoadblocks(highest >= 3);
        }

        void SetRoadblocks(bool on)
        {
            foreach (GameObject block in roadblocks)
            {
                if (block != null && block.activeSelf != on) block.SetActive(on);
            }
        }

        public void RequestArrest(PoliceOfficer by)
        {
            PlayerState suspect = by.Focus ?? session.Local;
            if (arrest == null || suspect.Busy || !suspect.Wanted.IsWanted) return;
            arrest.Arrest(suspect);
        }

        /// <summary>An officer finished walking home after standing down.</summary>
        public void OnOfficerHome(PoliceOfficer officer)
        {
            bool beatCop = officers.Count > 0 && officers[0] == officer;
            if (beatCop || strength > 0) officer.StartPatrol();
            else officer.Deactivate();
        }
    }
}
