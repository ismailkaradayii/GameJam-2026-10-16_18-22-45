using UnityEngine;

namespace Itfaiye
{
    // One timeline drives the hand, magazine and slide, so contact never depends on
    // separate animation timers or the locomotion Animator's current transition.
    public struct ItfaiyePistolReloadPose
    {
        public const float InsertedAt = .66f;
        public const float SlidePullStarts = .815f;
        public const float SlideReleasedAt = .895f;
        public const float ManualSlidePull = 1.25f;
        public static readonly Vector3 MagazineWristOffset = new Vector3(0f, -.023f, -.083f);
        public static readonly Quaternion MagazineHandRotation = Quaternion.LookRotation(Vector3.left, Vector3.down);
        public Vector3 leftWrist, magazineOffset;
        public Quaternion leftRotation, magazineRotation;
        public float weaponWeight, slideTilt, magazineGrip, slideGrip, openHand, slidePull;
        public bool originalVisible, replacementVisible;

        public static float Ease(float start, float end, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));

        public static ItfaiyePistolReloadPose Sample(float t, Vector3 magazineBase, bool slideLocked, ItfaiyeFirstPersonHands hands)
        {
            Vector3 slideWrist = hands.slideGripPosition;
            Quaternion slideHandRotation = hands.SlideGripRotation;
            var pose = new ItfaiyePistolReloadPose();
            float initialSlide = slideLocked ? 1f : 0f;
            var slideStart = slideWrist + Vector3.back * (.032f * initialSlide);
            pose.slidePull = initialSlide;
            var extracted = new Vector3(0f, -.145f, 0f);
            var pocket = new Vector3(-.08f, -.25f, -.15f);
            pose.weaponWeight = Ease(0f, .12f, t) * (1f - Ease(.925f, 1f, t));
            // Keep the slide exposed until the support hand has released it.
            pose.slideTilt = Ease(.68f, .78f, t) * (1f - Ease(.935f, 1f, t));
            pose.originalVisible = t < .40f || t >= InsertedAt;
            pose.replacementVisible = t >= .44f && t < InsertedAt;
            if (t < .18f) pose.magazineOffset = Vector3.zero;
            else if (t < .31f) pose.magazineOffset = Vector3.Lerp(Vector3.zero, extracted, Ease(.18f, .31f, t));
            else if (t < .40f) pose.magazineOffset = Vector3.Lerp(extracted, pocket, Ease(.31f, .40f, t));
            else if (t < .46f) pose.magazineOffset = pocket;
            else if (t < .56f) pose.magazineOffset = Vector3.Lerp(pocket, extracted, Ease(.46f, .56f, t));
            else pose.magazineOffset = Vector3.Lerp(extracted, Vector3.zero, Ease(.56f, InsertedAt, t));
            float magazineTurn = Ease(.31f, .40f, t) * (1f - Ease(.46f, .56f, t));
            pose.magazineRotation = Quaternion.Euler(8f * magazineTurn, 0f, -18f * magazineTurn);
            var magazineWrist = magazineBase + pose.magazineOffset + pose.magazineRotation * MagazineWristOffset;
            var magazineRotation = pose.magazineRotation * MagazineHandRotation;
            var support = ItfaiyeFirstPersonHands.LeftGrip;
            var supportRotation = ItfaiyeFirstPersonHands.LeftGripRotation;
            var approach = new Vector3(-.075f, -.125f, -.155f);
            var clear = new Vector3(-.120f, .045f, -.155f);
            var overSlide = slideStart + new Vector3(-.012f, .025f, -.006f);
            var turnRotation = Quaternion.Slerp(MagazineHandRotation, slideHandRotation, .7f);

            if (t < .08f)
            {
                pose.leftWrist = Vector3.Lerp(support, approach, Ease(0f, .08f, t));
                pose.leftRotation = Quaternion.Slerp(supportRotation, MagazineHandRotation, Ease(0f, .16f, t));
                pose.openHand = Ease(0f, .06f, t);
            }
            else if (t < .18f)
            {
                pose.leftWrist = Vector3.Lerp(approach, magazineWrist, Ease(.08f, .16f, t));
                pose.leftRotation = Quaternion.Slerp(supportRotation, MagazineHandRotation, Ease(0f, .16f, t));
                pose.openHand = 1f - Ease(.12f, .18f, t);
                pose.magazineGrip = Ease(.12f, .18f, t);
            }
            else if (t < .68f)
            {
                pose.leftWrist = magazineWrist;
                pose.leftRotation = magazineRotation;
                pose.magazineGrip = 1f;
            }
            else if (t < .74f)
            {
                pose.leftWrist = Vector3.Lerp(magazineWrist, clear, Ease(.68f, .74f, t));
                pose.leftRotation = Quaternion.Slerp(MagazineHandRotation, turnRotation, Ease(.68f, .74f, t));
                pose.magazineGrip = 1f - Ease(.68f, .72f, t);
                pose.openHand = Ease(.68f, .72f, t);
            }
            else if (t < .785f)
            {
                pose.leftWrist = Vector3.Lerp(clear, overSlide, Ease(.74f, .785f, t));
                pose.leftRotation = Quaternion.Slerp(turnRotation, slideHandRotation, Ease(.74f, .785f, t));
                pose.openHand = 1f;
            }
            else if (t < SlidePullStarts)
            {
                // Seat the palm and close the fingers before the slide starts moving.
                pose.leftWrist = Vector3.Lerp(overSlide, slideStart, Ease(.785f, .81f, t));
                pose.leftRotation = slideHandRotation;
                pose.slideGrip = Ease(.79f, .81f, t);
                pose.openHand = 1f - pose.slideGrip;
            }
            else if (t < SlideReleasedAt)
            {
                pose.slidePull = Mathf.Lerp(initialSlide, ManualSlidePull, Ease(SlidePullStarts, .865f, t));
                pose.leftWrist = slideWrist + Vector3.back * (.032f * pose.slidePull);
                pose.leftRotation = slideHandRotation;
                pose.slideGrip = 1f;
            }
            else if (t < .935f)
            {
                pose.slidePull = ManualSlidePull * (1f - Ease(SlideReleasedAt, .91f, t));
                pose.leftWrist = slideWrist + Vector3.back * (.032f * ManualSlidePull) + new Vector3(-.04f, .035f, -.02f) * Ease(SlideReleasedAt, .935f, t);
                pose.leftRotation = slideHandRotation;
                pose.openHand = Ease(SlideReleasedAt, .91f, t);
                pose.slideGrip = 1f - pose.openHand;
            }
            else
            {
                pose.slidePull = 0f;
                var release = slideWrist + new Vector3(-.04f, .035f, -.02f - .032f * ManualSlidePull);
                pose.leftWrist = Vector3.Lerp(release, support, Ease(.935f, 1f, t));
                pose.leftRotation = Quaternion.Slerp(slideHandRotation, supportRotation, Ease(.935f, 1f, t));
                pose.openHand = 1f - Ease(.95f, 1f, t);
            }
            return pose;
        }
    }
}
