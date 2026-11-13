using System;
using SmallTown.Interaction;
using SmallTown.Locomotion;
using TMPro;
using UnityEngine;

namespace SmallTown.UI
{
    /// <summary>
    /// Short playable lessons for movement, grabbing and storage. Progress is
    /// saved after every step, so an interrupted tutorial resumes where it
    /// stopped. It can be skipped or replayed at any time and never blocks the
    /// player from leaving.
    /// </summary>
    public sealed class TutorialController : MonoBehaviour
    {
        public enum Step
        {
            TouchGround,
            PushToMove,
            Jump,
            SnapTurn,
            PushOffWall,
            Grab,
            Throw,
            StoreInBag,
            TakeFromBag,
            Complete,
        }

        const string StepKey = "smalltown.tutorial.step";
        const string DoneKey = "smalltown.tutorial.done";

        /// <summary>Tests switch this off so they never touch a real player's progress.</summary>
        public static bool PersistenceEnabled = true;

        [SerializeField] PlayerRig rig;
        [SerializeField] HandGrabber leftGrabber;
        [SerializeField] HandGrabber rightGrabber;
        [SerializeField] StorageBag bag;
        [SerializeField] TMP_Text titleLabel;
        [SerializeField] TMP_Text bodyLabel;
        [SerializeField] TMP_Text progressLabel;
        [Min(0.5f)] public float moveDistance = 3f;
        [Min(0.1f)] public float jumpHeight = 0.35f;

        Step step;
        float moved;
        Vector3 lastBody;
        bool hasLastBody;
        float groundedBottomY;

        public event Action<Step> StepChanged;

        public Step Current => step;
        public bool IsComplete => step == Step.Complete;

        public void Configure(PlayerRig owner, HandGrabber left, HandGrabber right, StorageBag storage,
            TMP_Text title, TMP_Text body, TMP_Text progress)
        {
            rig = owner;
            leftGrabber = left;
            rightGrabber = right;
            bag = storage;
            titleLabel = title;
            bodyLabel = body;
            progressLabel = progress;
        }

        void OnEnable()
        {
            if (rig != null)
            {
                rig.Simulated += Tick;
                rig.HandTouched += OnHandTouched;
                rig.SnapTurned += OnSnapTurned;
            }
            Hook(leftGrabber, true);
            Hook(rightGrabber, true);
            if (bag != null)
            {
                bag.Stored += OnStored;
                bag.Taken += OnTaken;
            }

            step = Step.TouchGround;
            if (PersistenceEnabled)
            {
                if (PlayerPrefs.GetInt(DoneKey, 0) == 1) step = Step.Complete;
                else step = (Step)Mathf.Clamp(PlayerPrefs.GetInt(StepKey, 0), 0, (int)Step.Complete);
            }
            Show();
        }

        void OnDisable()
        {
            if (rig != null)
            {
                rig.Simulated -= Tick;
                rig.HandTouched -= OnHandTouched;
                rig.SnapTurned -= OnSnapTurned;
            }
            Hook(leftGrabber, false);
            Hook(rightGrabber, false);
            if (bag != null)
            {
                bag.Stored -= OnStored;
                bag.Taken -= OnTaken;
            }
        }

        void Hook(HandGrabber grabber, bool on)
        {
            if (grabber == null) return;
            if (on)
            {
                grabber.Grabbed += OnGrabbed;
                grabber.Released += OnReleased;
            }
            else
            {
                grabber.Grabbed -= OnGrabbed;
                grabber.Released -= OnReleased;
            }
        }

        /// <summary>Jumps straight to the end. Wired to the SKIP button.</summary>
        public void Skip() => SetStep(Step.Complete);

        /// <summary>Starts again from the first lesson. Wired to the REPLAY button.</summary>
        public void Restart()
        {
            if (PersistenceEnabled) PlayerPrefs.SetInt(DoneKey, 0);
            SetStep(Step.TouchGround);
        }

        void Advance(Step from)
        {
            if (step == from) SetStep(from + 1);
        }

        void SetStep(Step next)
        {
            step = next;
            moved = 0f;
            hasLastBody = false;
            if (PersistenceEnabled)
            {
                PlayerPrefs.SetInt(StepKey, (int)step);
                if (step == Step.Complete) PlayerPrefs.SetInt(DoneKey, 1);
                PlayerPrefs.Save();
            }
            if (rig != null && rig.Head != null) GameFeedback.Play(FeedbackSound.Success, rig.Head.position, 0.5f);
            Show();
            StepChanged?.Invoke(step);
        }

        void Tick(float deltaTime)
        {
            var motor = rig.Motor;
            if (motor == null) return;

            if (motor.IsGrounded) groundedBottomY = motor.BodyBottom.y;

            switch (step)
            {
                case Step.PushToMove:
                    if (hasLastBody && motor.AnyHandSupporting)
                    {
                        Vector3 delta = motor.BodyHead - lastBody;
                        delta.y = 0f;
                        moved += delta.magnitude;
                        if (moved >= moveDistance) Advance(Step.PushToMove);
                    }
                    lastBody = motor.BodyHead;
                    hasLastBody = true;
                    UpdateProgress();
                    break;
                case Step.Jump:
                    if (!motor.IsGrounded && !motor.AnyHandSupporting &&
                        motor.BodyBottom.y - groundedBottomY >= jumpHeight)
                    {
                        Advance(Step.Jump);
                    }
                    break;
                case Step.PushOffWall:
                    for (int i = 0; i < 2; i++)
                    {
                        HandContact hand = motor.GetHand((Hand)i);
                        Vector3 horizontal = motor.Velocity;
                        horizontal.y = 0f;
                        if (hand.touching && hand.canPush && Mathf.Abs(hand.normal.y) < 0.4f &&
                            Vector3.Dot(horizontal, hand.normal) > 0.8f)
                        {
                            Advance(Step.PushOffWall);
                        }
                    }
                    break;
            }
        }

        void OnHandTouched(Hand hand, Vector3 position, float speed)
        {
            if (step == Step.TouchGround && rig.Motor.GetHand(hand).normal.y > 0.7f) Advance(Step.TouchGround);
        }

        void OnSnapTurned(float degrees) => Advance(Step.SnapTurn);
        void OnGrabbed(HandGrabber grabber, GrabbableItem item) => Advance(Step.Grab);

        void OnReleased(HandGrabber grabber, GrabbableItem item)
        {
            if (step == Step.Throw && item.State == ItemState.Free && item.Body.linearVelocity.magnitude > 1.5f)
            {
                Advance(Step.Throw);
            }
        }

        void OnStored(GrabbableItem item) => Advance(Step.StoreInBag);
        void OnTaken(GrabbableItem item) => Advance(Step.TakeFromBag);

        void UpdateProgress()
        {
            if (progressLabel == null) return;
            progressLabel.text = step == Step.PushToMove
                ? Mathf.Min(moved, moveDistance).ToString("0.0") + " / " + moveDistance.ToString("0.0") + " m"
                : StepNumber();
        }

        string StepNumber() => step == Step.Complete ? "Done" : "Lesson " + ((int)step + 1) + " of " + (int)Step.Complete;

        void Show()
        {
            Describe(step, out string title, out string body);
            if (titleLabel != null) titleLabel.text = title;
            if (bodyLabel != null) bodyLabel.text = body;
            if (progressLabel != null) progressLabel.text = StepNumber();
        }

        public static void Describe(Step step, out string title, out string body)
        {
            switch (step)
            {
                case Step.TouchGround:
                    title = "Paws down";
                    body = "You have no legs. Your arms do the walking.\nReach down and press one paw flat on the ground.";
                    break;
                case Step.PushToMove:
                    title = "Push to move";
                    body = "Plant a paw ahead of you and drag it back along the ground. You slide the other way.\nSwap paws to keep going.";
                    break;
                case Step.Jump:
                    title = "Jump";
                    body = "Shove the ground down hard with both paws, then lift them away.\nThe harder the shove, the higher you go.";
                    break;
                case Step.SnapTurn:
                    title = "Turn";
                    body = "Flick the right thumbstick left or right to turn in steps.\nTurning your real body works too.";
                    break;
                case Step.PushOffWall:
                    title = "Use the walls";
                    body = "Put a paw on a wall and push. You bounce away from it.\nYellow striped surfaces can be gripped and climbed.";
                    break;
                case Step.Grab:
                    title = "Grab";
                    body = "Reach for a crate on the table and squeeze the grip.\nA paw that is carrying cannot push, so move with the other one.";
                    break;
                case Step.Throw:
                    title = "Throw";
                    body = "Swing your arm and let go of the grip mid-swing.";
                    break;
                case Step.StoreInBag:
                    title = "Pack your bag";
                    body = "Your satchel hangs on your left hip.\nHold an item inside it and let go.";
                    break;
                case Step.TakeFromBag:
                    title = "Unpack";
                    body = "Put an empty paw in the satchel and squeeze the grip to take the last item back out.";
                    break;
                default:
                    title = "You are ready";
                    body = "That is everything you need to get into trouble.\nSlap REPLAY to run the lessons again.";
                    break;
            }
        }
    }
}
