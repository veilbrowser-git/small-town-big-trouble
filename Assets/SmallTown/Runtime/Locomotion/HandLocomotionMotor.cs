using System;
using UnityEngine;

namespace SmallTown.Locomotion
{
    /// <summary>
    /// Contact-based hand propulsion. Each step:
    ///   1. ride whatever surface the body or hands rest on (platform carry),
    ///   2. integrate gravity and momentum into a predicted displacement,
    ///   3. sweep each virtual hand toward its controller; a blocked hand pushes
    ///      the body the other way by exactly the distance it was blocked,
    ///   4. move the body capsule through the collide-and-slide solver,
    ///   5. re-resolve the hands against the body's final position,
    ///   6. record motion so letting go keeps a controlled amount of momentum.
    /// Hands moving through empty air are never blocked, so they never propel.
    /// The motor only translates the rig; it never rotates the view.
    /// </summary>
    public sealed class HandLocomotionMotor : MonoBehaviour
    {
        public struct RigSample
        {
            public Vector3 headLocal;
            public Vector3 leftLocal;
            public Vector3 rightLocal;
            public bool leftTracked;
            public bool rightTracked;
        }

        struct SafePosition
        {
            public Vector3 head;
            public float length;
        }

        [SerializeField] HandLocomotionSettings settings;
        [SerializeField] Transform rigRoot;
        [SerializeField] CapsuleCollider bodyProxy;
        [SerializeField] SphereCollider handProxy;

        readonly HandContact[] hands = { new HandContact(), new HandContact() };
        readonly Vector3[] lastHandLocal = new Vector3[2];
        readonly bool[] needsSeed = new bool[2];
        readonly bool[] pushBlocked = new bool[2];
        readonly Vector3[] pushNormal = new Vector3[2];
        readonly Collider[] pushCollider = new Collider[2];
        readonly SafePosition[] safePositions = new SafePosition[12];
        readonly KinematicBodySolver body = new KinematicBodySolver();
        readonly HandSweeper sweeper = new HandSweeper();
        VelocityWindow window;

        bool initialized;
        Vector3 velocity;
        Vector3 bodyHead;
        float bodyLength;
        float lastHeadLocalY;
        KinematicBodySolver.GroundInfo ground;
        Vector3 groundLocalPoint;
        Vector3 groundWorldPoint;
        Vector3 carryVelocity;
        bool hadSupport;
        bool hasPreviousSample;
        RigSample previousSample;
        int safeHead;
        int safeCount;
        float safeTimer;

        /// <summary>Raised when the body drops below the kill plane.</summary>
        public event Action FellOutOfWorld;

        public HandLocomotionSettings Settings => settings;
        public Vector3 Velocity => velocity;
        public bool IsGrounded => ground.grounded && ground.walkable;
        public Collider GroundCollider => ground.grounded ? ground.collider : null;
        public float BodyLength => bodyLength;
        public Vector3 BodyHead => bodyHead;
        public Vector3 BodyBottom => bodyHead + Vector3.down * bodyLength;
        public bool AnyHandSupporting => Supporting(0) || Supporting(1);

        /// <summary>Per-player overrides from calibration. Zero or less means use the settings asset.</summary>
        public float ArmReachOverride { get; set; }
        public float StandingLengthOverride { get; set; }

        public float ArmReach => ArmReachOverride > 0f ? ArmReachOverride : settings.maxArmReach;
        public float StandingLength => StandingLengthOverride > 0f ? StandingLengthOverride : settings.standingBodyLength;

        public HandContact GetHand(Hand hand) => hands[(int)hand];
        public Vector3 HandPosition(Hand hand) => hands[(int)hand].position;
        public bool IsHandTouching(Hand hand) => hands[(int)hand].touching;

        public void Configure(HandLocomotionSettings newSettings, Transform root, CapsuleCollider bodyShape, SphereCollider handShape)
        {
            settings = newSettings;
            rigRoot = root;
            bodyProxy = bodyShape;
            handProxy = handShape;
            initialized = false;
        }

        public void SetHandCanPush(Hand hand, bool canPush)
        {
            hands[(int)hand].canPush = canPush;
            if (!canPush) hands[(int)hand].latched = false;
        }

        const float LatchReach = 0.07f;

        public bool IsLatched(Hand hand) => hands[(int)hand].latched;

        /// <summary>
        /// Grips the climbable surface this hand is touching. A latched hand holds
        /// the body in every direction, so the player can hang, pull up and fling.
        /// Returns false if the hand is not on a climbable surface.
        /// </summary>
        public bool TryLatch(Hand hand)
        {
            HandContact contact = hands[(int)hand];
            if (contact.latched) return true;
            if (!contact.seeded || !contact.canPush || settings == null) return false;
            ApplySettings();
            // A little reach makes gripping forgiving: the hand snaps onto a climbable
            // surface that is within a few centimetres.
            if (!sweeper.Probe(contact.position, LatchReach, true, out Vector3 normal, out Collider surface, out float clearance)) return false;
            contact.position -= normal * (clearance - settings.skinWidth);
            contact.touching = true;
            contact.surface = surface;
            contact.normal = normal;
            contact.latched = true;
            contact.RememberSurface();
            return true;
        }

        public void Unlatch(Hand hand) => hands[(int)hand].latched = false;

        bool Supporting(int index) => hands[index].touching && hands[index].canPush;

        void ApplySettings()
        {
            if (window == null) window = new VelocityWindow(settings.velocityWindow);
            window.WindowSeconds = settings.velocityWindow;

            body.Mask = settings.surfaceMask;
            body.Radius = settings.bodyRadius;
            body.Skin = settings.skinWidth;
            body.StepHeight = settings.stepHeight;
            body.MaxGroundAngle = settings.maxGroundAngle;
            body.Proxy = bodyProxy;

            sweeper.Mask = settings.surfaceMask;
            sweeper.Radius = settings.handRadius;
            sweeper.Skin = settings.skinWidth;
            sweeper.Settings = settings;
            sweeper.Proxy = handProxy;
        }

        /// <summary>
        /// Forget all contact and momentum history. Call after anything that moves
        /// the rig from outside the solver: teleports, respawns, leaving a seat.
        /// </summary>
        public void ResetState(bool keepVelocity = false)
        {
            initialized = false;
            hasPreviousSample = false;
            if (!keepVelocity) velocity = Vector3.zero;
            carryVelocity = Vector3.zero;
            window?.Clear();
            hadSupport = false;
            ground = default;
            for (int i = 0; i < 2; i++)
            {
                hands[i].ClearContact();
                hands[i].seeded = false;
            }
        }

        /// <summary>The hands jumped (snap turn, tracking regained): re-seed them without producing a push.</summary>
        public void ReseedHands()
        {
            hasPreviousSample = false;
            for (int i = 0; i < 2; i++)
            {
                hands[i].ClearContact();
                hands[i].seeded = false;
            }
        }

        public void AddVelocity(Vector3 delta) => velocity += delta;

        public void SetVelocity(Vector3 value)
        {
            velocity = value;
            window?.Clear();
        }

        /// <summary>
        /// Advance the simulation by one rendered frame. Long frames are split into
        /// several steps, interpolating the tracked poses, so results stay
        /// consistent between 72, 90 and 120 Hz and through hitches.
        /// </summary>
        public void Simulate(float deltaTime, RigSample sample)
        {
            if (settings == null || rigRoot == null || deltaTime <= 0f) return;
            ApplySettings();
            Physics.SyncTransforms();

            // A recentre moves every tracked pose at once; do not interpolate across it.
            bool headJumped = hasPreviousSample &&
                (sample.headLocal - previousSample.headLocal).magnitude > settings.trackingJumpDistance;
            if (!hasPreviousSample || headJumped)
            {
                previousSample = sample;
                hasPreviousSample = true;
            }

            float clamped = Mathf.Min(deltaTime, 0.1f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(clamped / settings.maxStep - 1e-4f));
            float dt = clamped / steps;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                var stepSample = sample;
                stepSample.headLocal = Vector3.LerpUnclamped(previousSample.headLocal, sample.headLocal, t);
                stepSample.leftLocal = Vector3.LerpUnclamped(previousSample.leftLocal, sample.leftLocal, t);
                stepSample.rightLocal = Vector3.LerpUnclamped(previousSample.rightLocal, sample.rightLocal, t);
                Step(dt, stepSample);
            }
            previousSample = sample;
        }

        void Initialize(RigSample sample)
        {
            bodyHead = rigRoot.TransformPoint(sample.headLocal);
            lastHeadLocalY = sample.headLocal.y;
            bodyLength = StandingLength;
            if (!body.Fits(bodyHead, bodyLength)) bodyLength = settings.crouchBodyLength;
            Vector3 resolved = body.Depenetrate(bodyHead, bodyLength, out _);
            rigRoot.position += resolved - bodyHead;
            bodyHead = resolved;
            lastHandLocal[0] = sample.leftLocal;
            lastHandLocal[1] = sample.rightLocal;
            needsSeed[0] = needsSeed[1] = true;
            hands[0].seeded = hands[1].seeded = false;
            ground = body.ProbeGround(bodyHead, bodyLength, 0.03f);
            RememberGround();
            initialized = true;
        }

        void Step(float dt, RigSample sample)
        {
            if (!initialized) Initialize(sample);

            Vector3 headWorld = rigRoot.TransformPoint(sample.headLocal);

            // --- Tracking discontinuity: recentre, or tracking returning far from where it left.
            bool trackingJumped = (headWorld - bodyHead).magnitude > settings.trackingJumpDistance;
            if (trackingJumped)
            {
                rigRoot.position += bodyHead - headWorld;
                headWorld = bodyHead;
                needsSeed[0] = needsSeed[1] = true;
                lastHeadLocalY = sample.headLocal.y;
                window.Clear();
            }

            UpdateBodyLength(dt, sample.headLocal.y, headWorld);

            // --- 1. Platform carry.
            Vector3 carry = ComputeCarry();
            carryVelocity = Vector3.Lerp(carryVelocity, carry / dt, 0.5f);

            // --- 2. Gravity, friction, predicted displacement.
            bool handSupport = AnyHandSupporting;
            IntegrateVelocity(dt, handSupport);
            Vector3 predicted = velocity * dt + carry;

            // --- 3. Hands push the body.
            Vector3 shoulder = headWorld + Vector3.down * settings.shoulderDrop;
            Vector3 correctionSum = Vector3.zero;
            float weightSum = 0f;
            for (int i = 0; i < 2; i++)
            {
                HandContact hand = hands[i];
                bool tracked = i == 0 ? sample.leftTracked : sample.rightTracked;
                Vector3 local = i == 0 ? sample.leftLocal : sample.rightLocal;
                pushBlocked[i] = false;

                if (!tracked)
                {
                    hand.ClearContact();
                    hand.seeded = false;
                    needsSeed[i] = true;
                    continue;
                }

                float rawSpeed = (local - lastHandLocal[i]).magnitude / dt;
                if (!hand.seeded || !hand.wasTracked || rawSpeed > settings.handGlitchSpeed) needsSeed[i] = true;
                if (needsSeed[i]) continue;

                Vector3 target = ClampReach(rigRoot.TransformPoint(local) + predicted, shoulder + predicted);
                if (hand.latched)
                {
                    if (hand.surface == null)
                    {
                        hand.latched = false;
                    }
                    else
                    {
                        // Gripping: the body goes wherever keeps the hand on its anchor.
                        Vector3 pull = hand.position - target;
                        float pullWeight = pull.magnitude + 1e-4f;
                        correctionSum += pull * pullWeight;
                        weightSum += pullWeight;
                        pushBlocked[i] = true;
                        pushNormal[i] = hand.normal;
                        pushCollider[i] = hand.surface;
                        continue;
                    }
                }

                if (sweeper.Overlaps(hand.position)) sweeper.Depenetrate(ref hand.position, out _, out _);
                var sweep = sweeper.Sweep(hand.position, target, hand.canPush ? 1f : 0f);
                pushBlocked[i] = sweep.touching;
                pushNormal[i] = sweep.normal;
                pushCollider[i] = sweep.collider;
                if (sweep.touching && hand.canPush)
                {
                    Vector3 correction = sweep.position - target;
                    float weight = correction.magnitude + 1e-4f;
                    correctionSum += correction * weight;
                    weightSum += weight;
                }
            }
            Vector3 handCorrection = weightSum > 0f ? correctionSum / weightSum : Vector3.zero;

            // --- 4. Move the body. Sweeping from last step's resolved position also
            //        stops the player physically leaning or walking through walls.
            Vector3 from = body.Depenetrate(bodyHead, bodyLength, out _);
            Vector3 desired = headWorld + predicted + handCorrection;
            var move = body.Move(from, bodyLength, desired - from, (ground.grounded && ground.walkable) || handSupport);

            Vector3 rigDelta = move.head - headWorld;
            rigRoot.position += rigDelta;
            bodyHead = move.head;

            if (move.contactCount > 0) RemoveVelocityInto(move.contactNormal0);
            if (move.contactCount > 1) RemoveVelocityInto(move.contactNormal1);

            // --- 5. Resolve hands against the body's final position.
            Vector3 finalShoulder = bodyHead + Vector3.down * settings.shoulderDrop;
            for (int i = 0; i < 2; i++)
            {
                HandContact hand = hands[i];
                bool tracked = i == 0 ? sample.leftTracked : sample.rightTracked;
                Vector3 local = i == 0 ? sample.leftLocal : sample.rightLocal;
                lastHandLocal[i] = local;
                hand.wasTracked = tracked;

                if (!tracked)
                {
                    // Keep an untracked hand with the body so it reappears somewhere sensible.
                    hand.position += rigDelta;
                    continue;
                }

                Vector3 target = ClampReach(rigRoot.TransformPoint(local), finalShoulder);
                if (needsSeed[i])
                {
                    SeedHand(hand, finalShoulder, target);
                    needsSeed[i] = false;
                    continue;
                }

                if (hand.latched)
                {
                    // Stays on its anchor. Let go if the arm is stretched past what it can hold.
                    if ((hand.position - target).magnitude > settings.handUnstickDistance) SeedHand(hand, finalShoulder, target);
                    else hand.RememberSurface();
                    continue;
                }

                var sweep = sweeper.Sweep(hand.position, target, hand.canPush ? 1f : 0f);
                hand.position = sweep.position;
                // When the push was fully resolved the hand sits exactly on its target and
                // this second sweep has nothing left to hit; the contact from step 3 stands.
                hand.touching = sweep.touching || pushBlocked[i];
                hand.surface = sweep.touching ? sweep.collider : pushCollider[i];
                hand.normal = sweep.touching ? sweep.normal : pushNormal[i];

                if ((hand.position - target).magnitude > settings.handUnstickDistance) SeedHand(hand, finalShoulder, target);
                hand.RememberSurface();
            }

            // --- 6. Momentum bookkeeping.
            window.Add(rigDelta - carry, dt);
            bool support = AnyHandSupporting;
            if (support)
            {
                velocity = window.Average();
            }
            else if (handSupport)
            {
                velocity = ReleaseVelocity(window.Average());
            }

            ground = body.ProbeGround(bodyHead, bodyLength, 0.03f);
            RememberGround();

            bool supportedNow = support || (ground.grounded && ground.walkable);
            if (hadSupport && !supportedNow && settings.inheritPlatformVelocity)
            {
                velocity += carryVelocity;
                carryVelocity = Vector3.zero;
            }
            hadSupport = supportedNow;

            float speed = velocity.magnitude;
            if (speed > settings.maxSpeed) velocity *= settings.maxSpeed / speed;

            RecordSafePosition(dt);
            SyncProxies();

            if (bodyHead.y < settings.killPlaneY) FellOutOfWorld?.Invoke();
        }

        void UpdateBodyLength(float dt, float headLocalY, Vector3 headWorld)
        {
            float standing = StandingLength;
            float crouch = Mathf.Min(settings.crouchBodyLength, standing);
            float physical = headLocalY - lastHeadLocalY;
            lastHeadLocalY = headLocalY;

            if (physical < 0f && ground.grounded)
            {
                // The player is physically ducking: shorten the body instead of lifting the rig.
                bodyLength = Mathf.Max(crouch, bodyLength + physical);
            }
            else if (physical > 0f && bodyLength < standing)
            {
                bodyLength = Mathf.Min(standing, bodyLength + physical);
            }
            else if (!ground.grounded && bodyLength < standing)
            {
                float longer = Mathf.Min(standing, bodyLength + settings.standUpSpeed * dt);
                if (body.Fits(headWorld, longer)) bodyLength = longer;
            }
            bodyLength = Mathf.Clamp(bodyLength, crouch, standing);
        }

        Vector3 ComputeCarry()
        {
            Vector3 handCarry = Vector3.zero;
            int carriers = 0;
            for (int i = 0; i < 2; i++)
            {
                HandContact hand = hands[i];
                if (!hand.touching || hand.surface == null) continue;
                Vector3 delta = hand.SurfaceDelta();
                hand.position += delta;
                if (!hand.canPush) continue;
                handCarry += delta;
                carriers++;
            }

            Vector3 carry = carriers > 0 ? handCarry / carriers : Vector3.zero;
            if (ground.grounded && ground.collider != null)
            {
                carry = ground.collider.transform.TransformPoint(groundLocalPoint) - groundWorldPoint;
            }
            // Ignore floating point noise from static surfaces.
            return carry.sqrMagnitude < 1e-9f ? Vector3.zero : carry;
        }

        void RememberGround()
        {
            if (!ground.grounded || ground.collider == null) return;
            groundWorldPoint = ground.point;
            groundLocalPoint = ground.collider.transform.InverseTransformPoint(ground.point);
        }

        void IntegrateVelocity(float dt, bool handSupport)
        {
            Vector3 gravityStep = Vector3.down * (settings.gravity * dt);
            if (!handSupport && ground.grounded && ground.walkable)
            {
                // Resting on walkable ground: gravity only presses into the surface, so
                // the body does not creep down gentle slopes.
                Vector3 n = ground.normal;
                velocity += Vector3.Project(gravityStep, n);

                Vector3 normalPart = Vector3.Project(velocity, n);
                Vector3 tangent = velocity - normalPart;
                float speed = tangent.magnitude;
                if (speed > 1e-5f)
                {
                    float slowed = Mathf.Max(0f, speed - settings.groundDeceleration * dt) * Mathf.Exp(-settings.groundDamping * dt);
                    tangent *= slowed / speed;
                }
                velocity = tangent + normalPart;
            }
            else
            {
                velocity += gravityStep;
                if (!handSupport) velocity *= Mathf.Exp(-settings.airDamping * dt);
            }

            float total = velocity.magnitude;
            if (total > settings.maxSpeed) velocity *= settings.maxSpeed / total;
        }

        Vector3 ReleaseVelocity(Vector3 pushed)
        {
            float speed = pushed.magnitude;
            if (speed < 1e-4f) return Vector3.zero;

            Vector3 v = pushed * settings.propulsionMultiplier;
            if (v.y > 0f) v.y *= settings.jumpMultiplier;
            // Tiny releases fade out rather than nudging the player around.
            if (speed < settings.minLaunchSpeed) v *= speed / settings.minLaunchSpeed;
            float launch = v.magnitude;
            if (launch > settings.maxLaunchSpeed) v *= settings.maxLaunchSpeed / launch;
            return v;
        }

        void RemoveVelocityInto(Vector3 normal)
        {
            float into = Vector3.Dot(velocity, normal);
            if (into < 0f) velocity -= normal * into;
        }

        Vector3 ClampReach(Vector3 target, Vector3 shoulder)
        {
            Vector3 offset = target - shoulder;
            float reach = ArmReach;
            float distance = offset.magnitude;
            return distance > reach ? shoulder + offset * (reach / distance) : target;
        }

        void SeedHand(HandContact hand, Vector3 shoulder, Vector3 target)
        {
            // Start inside the body and glide outward, so a hand can never be seeded
            // on the far side of a wall.
            var sweep = sweeper.Sweep(shoulder, target, 0f);
            hand.position = sweep.position;
            hand.ClearContact();
            hand.seeded = true;
        }

        void RecordSafePosition(float dt)
        {
            safeTimer += dt;
            if (safeTimer < settings.safePositionInterval) return;
            if (!ground.grounded || !ground.walkable || velocity.sqrMagnitude > 4f) return;
            if (!body.Fits(bodyHead, bodyLength)) return;
            safeTimer = 0f;
            safePositions[safeHead] = new SafePosition { head = bodyHead, length = bodyLength };
            safeHead = (safeHead + 1) % safePositions.Length;
            if (safeCount < safePositions.Length) safeCount++;
        }

        /// <summary>
        /// Finds the most recent safe standing position that is at least
        /// <paramref name="minDistance"/> away from where the body is now.
        /// </summary>
        public bool TryGetSafePosition(float minDistance, out Vector3 head)
        {
            for (int i = 0; i < safeCount; i++)
            {
                int index = (safeHead - 1 - i + safePositions.Length) % safePositions.Length;
                SafePosition candidate = safePositions[index];
                if ((candidate.head - bodyHead).magnitude < minDistance) continue;
                if (!body.Fits(candidate.head, StandingLength) && !body.Fits(candidate.head, candidate.length)) continue;
                head = candidate.head;
                return true;
            }
            head = default;
            return false;
        }

        public void ClearSafePositions()
        {
            safeCount = 0;
            safeHead = 0;
            safeTimer = 0f;
        }

        void SyncProxies()
        {
            if (bodyProxy == null) return;
            float height = bodyLength + settings.bodyRadius;
            bodyProxy.radius = settings.bodyRadius;
            bodyProxy.height = height;
            bodyProxy.direction = 1;
            bodyProxy.center = Vector3.zero;
        }

        /// <summary>World-space centre of the body capsule, for the physics proxy to follow.</summary>
        public Vector3 BodyCentre => bodyHead + Vector3.up * (settings.bodyRadius - (bodyLength + settings.bodyRadius) * 0.5f);
    }
}
