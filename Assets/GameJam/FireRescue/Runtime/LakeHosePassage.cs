using System.Collections.Generic;
using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>The clear intake opening through an authored lake fence.</summary>
    public sealed class LakeHosePassage : MonoBehaviour
    {
        public Transform dryGuide, wetGuide, intakeTarget;
        [Min(.1f)] public float intakeRadius = 5f;
        static readonly List<LakeHosePassage> Active = new List<LakeHosePassage>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetPassages() => Active.Clear();
        void OnEnable() { if(!Active.Contains(this))Active.Add(this); }
        void OnDisable() => Active.Remove(this);

        public static LakeHosePassage Find(Vector3 intake)
        {
            LakeHosePassage nearest=null;float distance=float.PositiveInfinity;
            foreach(var passage in Active)
            {
                if(!passage||!passage.isActiveAndEnabled||!passage.dryGuide||!passage.wetGuide||!passage.intakeTarget)continue;
                float d=(passage.intakeTarget.position-intake).sqrMagnitude;
                if(d>passage.intakeRadius*passage.intakeRadius||d>=distance)continue;
                distance=d;nearest=passage;
            }
            return nearest;
        }
        public float Length(Vector3 start,Vector3 end) => Vector3.Distance(start,dryGuide.position)+
            Vector3.Distance(dryGuide.position,wetGuide.position)+Vector3.Distance(wetGuide.position,end);
    }
}
