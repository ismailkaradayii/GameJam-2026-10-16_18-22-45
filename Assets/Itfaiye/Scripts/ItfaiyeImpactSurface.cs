using UnityEngine;

namespace Itfaiye
{
    public enum ItfaiyeSurfaceKind { Concrete, Metal, Wood, Dirt }

    [DisallowMultipleComponent]
    [AddComponentMenu("Itfaiye/Impact Surface")]
    public sealed class ItfaiyeImpactSurface : MonoBehaviour
    {
        [Tooltip("Overrides automatic material-name detection for bullet impacts on this object and its children.")]
        public ItfaiyeSurfaceKind surface = ItfaiyeSurfaceKind.Concrete;
    }
}
