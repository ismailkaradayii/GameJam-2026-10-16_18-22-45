using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Small bounded dust and pickup effects; uses the existing player's authoritative movement state.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(220)]
    public sealed class PlayerPolishEffects : MonoBehaviour
    {
        public FireRescuePlayer player;
        public ParticleSystem movementDust, pickupSparkles;
        public AudioSource pickupAudio;
        public AudioClip pickupSound;
        [Min(.5f)] public float walkingStepMetres = 3.2f, sprintStepMetres = 3.9f;
        [Range(0, 1)] public float dustOpacity = .17f;
        public int DustBursts { get; private set; }
        public int PickupBursts { get; private set; }
        readonly RaycastHit[] hits = new RaycastHit[12];
        bool wasGrounded;
        float stepRemaining, airborne;
        int foot;

        void LateUpdate()
        {
            var fp = player ? player.firstPerson : null;
            if (!fp || !movementDust) return;
            if (player.IsDriving || player.GameplaySuspended || player.CinematicLocked || Time.timeScale <= 0)
            { wasGrounded = fp.IsGrounded; airborne = 0; stepRemaining = 0; return; }
            bool grounded = fp.IsGrounded;
            if (!grounded) airborne += Time.deltaTime;
            else
            {
                if (!wasGrounded && airborne > .2f) EmitDust(10, 1.5f);
                airborne = 0;
                stepRemaining -= fp.CurrentSpeed * Time.deltaTime;
                if (fp.CurrentSpeed > 2 && stepRemaining <= 0)
                {
                    EmitDust(fp.IsSprinting ? 4 : 3, fp.IsSprinting ? 1.2f : .8f);
                    stepRemaining = fp.IsSprinting ? sprintStepMetres : walkingStepMetres;
                }
            }
            wasGrounded = grounded;
        }
        void EmitDust(int count, float scale)
        {
            var body = player.playerBody;
            Vector3 feet = body ? new Vector3(body.bounds.center.x, body.bounds.min.y, body.bounds.center.z) : transform.position;
            int found = Physics.RaycastNonAlloc(feet + Vector3.up * .5f, Vector3.down, hits, 2f, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit closest = default; float distance = float.PositiveInfinity;
            for (int i = 0; i < found; i++)
                if (!hits[i].transform.IsChildOf(player.transform) && hits[i].distance < distance) { closest = hits[i]; distance = hits[i].distance; }
            if (distance == float.PositiveInfinity || !(closest.collider is TerrainCollider)) return;
            if (!movementDust.isPlaying) movementDust.Play(false);
            DustBursts++; foot = 1 - foot;
            Vector3 point = closest.point + closest.normal * .035f + player.transform.right * (foot == 0 ? -.32f : .32f);
            for (int i = 0; i < count; i++) movementDust.Emit(new ParticleSystem.EmitParams
            {
                position = point + Random.insideUnitSphere * .08f,
                velocity = (Vector3.up * Random.Range(.2f, .5f) - player.transform.forward * Random.Range(.3f, .8f)) * scale,
                startSize = Random.Range(.2f, .4f) * scale, startLifetime = Random.Range(.3f, .65f),
                startColor = new Color(.38f, .31f, .25f, dustOpacity), rotation = Random.Range(-180f, 180f)
            }, 1);
        }
        public void PlayPickup(Vector3 point)
        {
            PickupBursts++;
            if (pickupSparkles && !pickupSparkles.isPlaying) pickupSparkles.Play(false);
            if (pickupSparkles) for (int i = 0; i < 14; i++) pickupSparkles.Emit(new ParticleSystem.EmitParams
            {
                position = point + Random.insideUnitSphere * .08f,
                velocity = Random.onUnitSphere * Random.Range(.6f, 1.6f) + Vector3.up * .4f,
                startSize = Random.Range(.025f, .07f), startLifetime = Random.Range(.18f, .4f),
                startColor = new Color(.3f, 1.7f, 2.2f, .8f)
            }, 1);
            if (pickupAudio && pickupSound) pickupAudio.PlayOneShot(pickupSound, .3f * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1)));
        }
    }
}
