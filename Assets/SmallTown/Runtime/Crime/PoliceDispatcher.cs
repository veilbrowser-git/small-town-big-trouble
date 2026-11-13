using System.Collections.Generic;
using SmallTown.Economy;
using SmallTown.Locomotion;
using UnityEngine;

namespace SmallTown.Crime
{
    /// <summary>
    /// Decides how much police the town sends and what each officer is told.
    /// Host-authoritative in co-op. Level 1: the local beat cop looks into it.
    /// Level 2: three officers and the patrol car. Level 3: five officers and a
    /// roadblock on the main street. The dispatcher only ever knows where a
    /// suspect is from reports and from what officers currently see.
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
        WantedTracker wanted;
        float sirenAt;

        public IReadOnlyList<PoliceOfficer> Officers => officers;
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
            GameSession session = GameSession.Instance;
            wanted = session.Wanted;
            wanted.LevelChanged += OnLevelChanged;
            wanted.CrimeReported += OnCrimeReported;
            wanted.Escaped += OnEscaped;
            CrimeEvents.Committed += OnCrimeCommitted;

            for (int i = 0; i < poolSize; i++)
            {
                Transform spawn = spawnPoints[i % spawnPoints.Length];
                PoliceOfficer officer = Instantiate(officerPrefab, spawn.position, spawn.rotation, transform);
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
            if (wanted != null)
            {
                wanted.LevelChanged -= OnLevelChanged;
                wanted.CrimeReported -= OnCrimeReported;
                wanted.Escaped -= OnEscaped;
            }
            CrimeEvents.Committed -= OnCrimeCommitted;
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            if (session == null || wanted == null) return;
            PlayerRig rig = session.Rig;
            Vector3 head = rig.Head.position;
            Vector3 feet = rig.Motor != null && rig.Mode == MovementMode.HandLocomotion ? rig.Motor.BodyBottom : head + Vector3.down * 0.8f;
            bool busy = arrest != null && arrest.InProgress;

            bool seen = false;
            foreach (PoliceOfficer officer in officers)
            {
                if (officer.State == OfficerState.Inactive) continue;
                officer.Tick(Time.deltaTime, wanted.IsWanted && !busy, head, feet, wanted.Level);
                seen |= officer.SeesSuspect;
            }
            if (!busy) wanted.Tick(Time.deltaTime, seen, feet);

            if (wanted.Level >= 2 && Time.time >= sirenAt)
            {
                sirenAt = Time.time + 1.6f;
                Vector3 source = patrolCar != null ? patrolCar.transform.position : transform.position;
                GameFeedback.Play(FeedbackSound.Siren, source, 0.7f);
            }
        }

        void OnCrimeCommitted(CrimeEvent crime)
        {
            // An officer who sees a crime happen reports it at once.
            GameSession session = GameSession.Instance;
            if (session == null || crime.offenderId != session.LocalPlayerId) return;
            foreach (PoliceOfficer officer in officers)
            {
                if (officer.State == OfficerState.Inactive || officer.State == OfficerState.Stunned) continue;
                if (!officer.CanSee(crime.position + Vector3.up * 0.5f, session.Balance)) continue;
                wanted.ReportCrime(crime.wantedLevel, crime.position);
                return;
            }
        }

        void OnCrimeReported(int level, Vector3 position)
        {
            foreach (PoliceOfficer officer in officers) officer.Investigate(position);
        }

        void OnLevelChanged(int level)
        {
            GameBalance balance = GameSession.Instance.Balance;
            if (level == 0)
            {
                foreach (PoliceOfficer officer in officers) officer.GiveUp();
                if (patrolCar != null) patrolCar.Return();
                foreach (GameObject block in roadblocks)
                {
                    if (block != null) block.SetActive(false);
                }
                return;
            }

            HudToast.Show(level == 1 ? "WANTED: a cop is looking for you." : level == 2 ? "WANTED: squad and patrol car inbound!" : "WANTED: every cop in town. Roadblocks are up!");
            int needed = Mathf.Min(officers.Count, balance.OfficersFor(level));
            int active = ActiveOfficers;
            for (int i = 0; i < officers.Count && active < needed; i++)
            {
                if (officers[i].State != OfficerState.Inactive) continue;
                officers[i].Activate(spawnPoints[i % spawnPoints.Length].position);
                active++;
            }
            foreach (PoliceOfficer officer in officers) officer.Investigate(wanted.LastKnownPosition);

            if (level >= 2 && patrolCar != null) patrolCar.Dispatch(wanted.LastKnownPosition);
            if (level >= 3)
            {
                foreach (GameObject block in roadblocks)
                {
                    if (block != null) block.SetActive(true);
                }
            }
        }

        void OnEscaped(int fromLevel)
        {
            foreach (PoliceOfficer officer in officers) officer.GiveUp();
        }

        public void RequestArrest(PoliceOfficer by)
        {
            if (arrest == null || arrest.InProgress || !wanted.IsWanted) return;
            arrest.Arrest();
        }

        /// <summary>An officer finished walking home after standing down.</summary>
        public void OnOfficerHome(PoliceOfficer officer)
        {
            bool beatCop = officers.Count > 0 && officers[0] == officer;
            if (beatCop || wanted.IsWanted) officer.StartPatrol();
            else officer.Deactivate();
        }
    }
}
