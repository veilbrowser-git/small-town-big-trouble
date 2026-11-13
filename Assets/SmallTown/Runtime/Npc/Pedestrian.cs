using SmallTown.Crime;
using UnityEngine;
using UnityEngine.AI;

namespace SmallTown.Npc
{
    /// <summary>
    /// A townsperson who wanders between points. If they see or hear a crime
    /// they run from it and, a few seconds later, phone it in. They can only
    /// report what they could actually notice: it has to be in range with no
    /// building in the way.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class Pedestrian : MonoBehaviour, IShootable
    {
        [SerializeField] NpcBody body;
        [SerializeField] Transform[] wanderPoints = new Transform[0];
        [Min(1f)] public float noticeRange = 14f;
        [Min(0.2f)] public float walkSpeed = 1.1f;
        [Min(0.2f)] public float fleeSpeed = 3f;

        NavMeshAgent agent;
        float waitUntil;
        float fleeUntil;
        float reportAt = -1f;
        CrimeEvent pending;
        int pointIndex;

        public bool Fleeing => Time.time < fleeUntil;
        public bool WillReport => reportAt >= 0f;

        public void Configure(NpcBody npcBody, Transform[] points)
        {
            body = npcBody;
            wanderPoints = points;
        }

        void Awake() => agent = GetComponent<NavMeshAgent>();

        void OnEnable()
        {
            CrimeEvents.Committed += OnCrime;
            pointIndex = Random.Range(0, Mathf.Max(1, wanderPoints.Length));
        }

        void OnDisable() => CrimeEvents.Committed -= OnCrime;

        void OnCrime(CrimeEvent crime)
        {
            Vector3 eyes = body != null ? body.EyePosition : transform.position + Vector3.up * 0.8f;
            Vector3 target = crime.position + Vector3.up * 0.5f;
            if ((target - eyes).magnitude > noticeRange) return;
            if (Physics.Linecast(eyes, target, 1 << GameLayers.World, QueryTriggerInteraction.Ignore)) return;
            Witness(crime);
        }

        public void OnShot(Vector3 point, Vector3 direction)
        {
            Witness(new CrimeEvent { kind = CrimeKind.Gunfire, position = point, wantedLevel = 2,
                offenderId = GameSession.Instance != null ? GameSession.Instance.LocalPlayerId : "" });
        }

        void Witness(CrimeEvent crime)
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;
            fleeUntil = Time.time + 8f;
            if (reportAt < 0f || crime.wantedLevel > pending.wantedLevel)
            {
                pending = crime;
                reportAt = Time.time + session.Balance.witnessReportDelay;
            }
            if (agent != null && agent.isOnNavMesh)
            {
                Vector3 away = transform.position - crime.position;
                away.y = 0f;
                Vector3 destination = transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : transform.forward) * 9f;
                if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 6f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
            }
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            if (reportAt >= 0f && Time.time >= reportAt && session != null)
            {
                reportAt = -1f;
                if (pending.offenderId == session.LocalPlayerId)
                {
                    session.Wanted.ReportCrime(pending.wantedLevel, pending.position);
                    HudToast.Show("A witness called the police!");
                }
            }

            if (agent == null || !agent.isOnNavMesh) return;
            if (Fleeing)
            {
                agent.speed = fleeSpeed;
                if (body != null) body.pose = NpcPose.HandsUp;
                return;
            }

            agent.speed = walkSpeed;
            if (body != null && body.pose == NpcPose.HandsUp) body.pose = NpcPose.Idle;
            if (wanderPoints.Length == 0) return;
            bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f;
            if (arrived && Time.time >= waitUntil)
            {
                pointIndex = (pointIndex + 1 + Random.Range(0, Mathf.Max(1, wanderPoints.Length - 1))) % wanderPoints.Length;
                agent.SetDestination(wanderPoints[pointIndex].position);
                waitUntil = Time.time + Random.Range(3f, 8f);
            }
        }
    }
}
