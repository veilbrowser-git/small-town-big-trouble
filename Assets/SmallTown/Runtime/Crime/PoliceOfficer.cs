using SmallTown.Economy;
using SmallTown.Npc;
using UnityEngine;
using UnityEngine.AI;

namespace SmallTown.Crime
{
    public enum OfficerState
    {
        Inactive,
        Patrol,
        Investigate,
        Pursue,
        Search,
        GiveUp,
        Stunned,
    }

    /// <summary>
    /// One police officer. Knows only what it can see: a suspect is visible if
    /// they are in range, inside the view cone (or very close), and no world
    /// geometry is in the way. Lose sight and the officer goes to the last
    /// place it saw you, pokes around, and eventually gives up. Suspects on
    /// rooftops are followed to the nearest point the officer can actually
    /// reach, where it waits and watches.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class PoliceOfficer : MonoBehaviour, IShootable
    {
        const int SightBlockers = 1 << GameLayers.World;

        [SerializeField] NpcBody body;
        NavMeshAgent agent;
        PoliceDispatcher dispatcher;
        Transform[] patrolPoints;
        Vector3 home;
        Vector3 focus;
        float stateTime;
        float unseenTime;
        float arrestTime;
        float repathAt;
        float lookAngle;
        int patrolIndex;
        int searchStops;
        float stunUntil;
        OfficerState afterStun;

        /// <summary>The player this officer is currently dealing with, chosen by the dispatcher.</summary>
        public PlayerState Focus { get; set; }

        public OfficerState State { get; private set; } = OfficerState.Inactive;
        public bool SeesSuspect { get; private set; }
        public NpcBody Body => body;
        public NavMeshAgent Agent => agent;

        public void Configure(NpcBody npcBody) => body = npcBody;

        void Awake() => agent = GetComponent<NavMeshAgent>();

        public void Setup(PoliceDispatcher owner, Transform[] patrol, Vector3 homePosition)
        {
            dispatcher = owner;
            patrolPoints = patrol;
            home = homePosition;
        }

        // ------------------------------------------------------------------ Orders

        public void Activate(Vector3 position)
        {
            gameObject.SetActive(true);
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            agent.enabled = true;
            if (body != null) body.SetPresent(true);
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) position = hit.position;
            agent.Warp(position);
            SetState(OfficerState.Patrol);
        }

        public void Deactivate()
        {
            // Off duty: the object stays in the scene (it is a shared network object) but
            // is invisible, untouchable and does not think.
            SeesSuspect = false;
            Focus = null;
            State = OfficerState.Inactive;
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            if (body != null) body.SetPresent(false);
        }

        public void StartPatrol() => SetState(OfficerState.Patrol);

        public void Investigate(Vector3 position)
        {
            if (State == OfficerState.Inactive || State == OfficerState.Stunned || State == OfficerState.Pursue) return;
            focus = position;
            SetState(OfficerState.Investigate);
        }

        /// <summary>Stand down: walk home, then either resume the beat or go off duty.</summary>
        public void GiveUp()
        {
            if (State == OfficerState.Inactive || State == OfficerState.Stunned) return;
            SetState(OfficerState.GiveUp);
        }

        public void OnShot(Vector3 point, Vector3 direction, PlayerState shooter)
        {
            if (State == OfficerState.Inactive) return;
            GameBalance balance = GameSession.Instance.Balance;
            if (State != OfficerState.Stunned) afterStun = State == OfficerState.Patrol ? OfficerState.Search : State;
            stunUntil = Time.time + balance.stunSeconds;
            focus = transform.position;
            SetState(OfficerState.Stunned);
            // Shooting an officer is the fastest way to the top wanted level.
            if (shooter == null) shooter = GameSession.Instance.Local;
            CrimeEvents.Raise(CrimeKind.AssaultOnOfficer, transform.position, 3, shooter);
            shooter.Wanted.ReportCrime(3, transform.position);
        }

        // ------------------------------------------------------------------ Senses

        /// <summary>Line-of-sight test from this officer's eyes to a point.</summary>
        public bool CanSee(Vector3 point, GameBalance balance)
        {
            Vector3 eyes = body != null ? body.EyePosition : transform.position + Vector3.up * 0.8f;
            Vector3 toPoint = point - eyes;
            float distance = toPoint.magnitude;
            if (distance > balance.sightRange) return false;
            if (distance > balance.alwaysNoticeRange)
            {
                Vector3 flat = new Vector3(toPoint.x, 0f, toPoint.z);
                if (Vector3.Angle(transform.forward, flat) > balance.sightAngle * 0.5f) return false;
            }
            return !Physics.Linecast(eyes, point, SightBlockers, QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ Behaviour

        /// <summary>Runs one step of behaviour. Called by the dispatcher.</summary>
        public void Tick(float deltaTime, bool suspectWanted, Vector3 suspectHead, Vector3 suspectFeet, int wantedLevel)
        {
            if (State == OfficerState.Inactive) return;
            GameBalance balance = GameSession.Instance.Balance;
            stateTime += deltaTime;

            bool visible = State != OfficerState.Stunned && CanSee(suspectHead, balance);
            SeesSuspect = suspectWanted && visible;

            if (SeesSuspect && State != OfficerState.Pursue && State != OfficerState.Stunned)
            {
                SetState(OfficerState.Pursue);
            }

            switch (State)
            {
                case OfficerState.Patrol:
                    agent.speed = balance.officerPatrolSpeed;
                    if (patrolPoints != null && patrolPoints.Length > 0)
                    {
                        if (Arrived(0.6f) || stateTime < deltaTime * 1.5f)
                        {
                            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
                            GoTo(patrolPoints[patrolIndex].position);
                        }
                    }
                    break;

                case OfficerState.Investigate:
                    agent.speed = balance.ChaseSpeedFor(wantedLevel);
                    GoTo(focus);
                    if (Arrived(1.2f))
                    {
                        LookAround(deltaTime);
                        if (stateTime > 4f && lookAngle > 300f) SetState(OfficerState.Search);
                    }
                    if (!suspectWanted && stateTime > 10f) SetState(OfficerState.GiveUp);
                    break;

                case OfficerState.Pursue:
                    agent.speed = balance.ChaseSpeedFor(wantedLevel);
                    if (!suspectWanted)
                    {
                        SetState(OfficerState.GiveUp);
                        break;
                    }
                    if (visible)
                    {
                        unseenTime = 0f;
                        focus = suspectFeet;
                        // Aim for the nearest reachable ground to the suspect. If they are on
                        // a roof this is the pavement below them.
                        if (Time.time >= repathAt)
                        {
                            repathAt = Time.time + 0.25f;
                            GoTo(suspectFeet);
                        }
                        Face(suspectFeet, deltaTime);
                        TryArrest(deltaTime, suspectFeet, balance);
                    }
                    else
                    {
                        arrestTime = 0f;
                        unseenTime += deltaTime;
                        GoTo(focus);
                        if (unseenTime > 1.5f) SetState(OfficerState.Search);
                    }
                    break;

                case OfficerState.Search:
                    agent.speed = balance.officerPatrolSpeed * 1.5f;
                    if (Arrived(1f))
                    {
                        LookAround(deltaTime);
                        if (lookAngle > 200f)
                        {
                            lookAngle = 0f;
                            searchStops++;
                            Vector2 offset = Random.insideUnitCircle * 7f;
                            GoTo(focus + new Vector3(offset.x, 0f, offset.y));
                        }
                    }
                    if (stateTime > balance.searchSeconds || !suspectWanted) SetState(OfficerState.GiveUp);
                    break;

                case OfficerState.GiveUp:
                    agent.speed = balance.officerPatrolSpeed * 1.4f;
                    GoTo(home);
                    if (Arrived(1.2f) || stateTime > 40f)
                    {
                        if (dispatcher != null) dispatcher.OnOfficerHome(this);
                        else SetState(OfficerState.Patrol);
                    }
                    break;

                case OfficerState.Stunned:
                    agent.isStopped = true;
                    if (Time.time >= stunUntil)
                    {
                        agent.isStopped = false;
                        SetState(afterStun == OfficerState.Stunned ? OfficerState.Search : afterStun);
                    }
                    break;
            }

            UpdatePose(suspectFeet);
        }

        void TryArrest(float deltaTime, Vector3 suspectFeet, GameBalance balance)
        {
            Vector3 delta = suspectFeet - transform.position;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            bool close = horizontal <= balance.arrestRange && Mathf.Abs(delta.y) <= 1.0f;
            arrestTime = close ? arrestTime + deltaTime : Mathf.Max(0f, arrestTime - deltaTime * 2f);
            if (arrestTime >= balance.arrestSeconds && dispatcher != null)
            {
                arrestTime = 0f;
                dispatcher.RequestArrest(this);
            }
        }

        void UpdatePose(Vector3 suspectFeet)
        {
            if (body == null) return;
            switch (State)
            {
                case OfficerState.Stunned:
                    body.pose = NpcPose.Dizzy;
                    break;
                case OfficerState.Pursue when arrestTime > 0f:
                    body.pose = NpcPose.Reach;
                    body.reachTarget = suspectFeet + Vector3.up * 0.5f;
                    break;
                default:
                    body.pose = NpcPose.Idle;
                    break;
            }
        }

        void SetState(OfficerState next)
        {
            State = next;
            stateTime = 0f;
            unseenTime = 0f;
            arrestTime = 0f;
            lookAngle = 0f;
            searchStops = 0;
            repathAt = 0f;
            if (agent != null && agent.isOnNavMesh) agent.isStopped = next == OfficerState.Stunned;
        }

        void GoTo(Vector3 position)
        {
            if (agent == null || !agent.isOnNavMesh) return;
            // A destination off the mesh (a rooftop, mid-air) resolves to the closest
            // point the officer can stand on.
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 6f, NavMesh.AllAreas)) position = hit.position;
            if ((agent.destination - position).sqrMagnitude > 0.04f) agent.SetDestination(position);
        }

        bool Arrived(float tolerance)
        {
            if (agent == null || !agent.isOnNavMesh || agent.pathPending) return false;
            return agent.remainingDistance <= Mathf.Max(tolerance, agent.stoppingDistance + 0.05f);
        }

        void LookAround(float deltaTime)
        {
            float step = 110f * deltaTime;
            lookAngle += step;
            transform.Rotate(0f, step, 0f, Space.World);
        }

        void Face(Vector3 point, float deltaTime)
        {
            Vector3 flat = point - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            Quaternion target = Quaternion.LookRotation(flat.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, 420f * deltaTime);
        }
    }
}
