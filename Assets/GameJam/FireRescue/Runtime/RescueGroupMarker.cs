using UnityEngine;

namespace GameJam.FireRescue
{
    [DisallowMultipleComponent]
    public sealed class RescueGroupMarker : MonoBehaviour
    {
        public EvacuationMission mission;
        public TextMesh symbol;
        public Renderer backdrop;
        public float height = 7f;
        public int CurrentGroup { get; private set; } = -1;
        public bool IsVisible => symbol && symbol.gameObject.activeInHierarchy;

        void LateUpdate()
        {
            CurrentGroup = mission && mission.IsActive && !mission.IsComplete ? mission.NextRecruitGroup : -1;
            bool visible = CurrentGroup >= 0 && mission.PlayerTransform;
            if (symbol) symbol.gameObject.SetActive(visible);
            if (backdrop) backdrop.enabled = visible;
            if (!visible) return;

            Vector3 center = Vector3.zero;
            int count = 0;
            foreach (var resident in mission.residents)
            {
                if (!resident || resident.rescueGroup != CurrentGroup || resident.IsCalled || resident.IsDead || resident.IsSafe) continue;
                center += resident.transform.position;
                count++;
            }
            if (count == 0) return;
            transform.position = center / count + Vector3.up * (height + Mathf.Sin(Time.time * 2.5f) * .18f);
            var camera = mission.player ? mission.player.viewCamera : null;
            if (!camera) return;
            Vector3 facing = transform.position - camera.transform.position;
            if (facing.sqrMagnitude > .01f) transform.rotation = Quaternion.LookRotation(facing);
            float scale = Mathf.Clamp(facing.magnitude * .025f, 1f, 2.5f);
            transform.localScale = Vector3.one * scale;
        }

        void OnDisable()
        {
            CurrentGroup = -1;
            if (symbol) symbol.gameObject.SetActive(false);
            if (backdrop) backdrop.enabled = false;
        }
    }
}
