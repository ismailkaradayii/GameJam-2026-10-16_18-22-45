using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Follows a TextMesh dynamic font atlas without changing its shared material asset.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(TextMesh), typeof(MeshRenderer))]
    public sealed class WorldLetteringFontAtlas : MonoBehaviour
    {
        static readonly int MainTexture = Shader.PropertyToID("_MainTex");
        TextMesh text;
        MeshRenderer target;
        MaterialPropertyBlock properties;
        bool subscribed, binding;

        void OnEnable()
        {
            text = GetComponent<TextMesh>();
            target = GetComponent<MeshRenderer>();
            if (!subscribed) { Font.textureRebuilt += FontRebuilt; subscribed = true; }
            RefreshAtlas();
        }

        /// <summary>Explicit refresh after authoring the font or text; ordinary runtime work is event driven.</summary>
        public void RefreshAtlas() => Bind(true);

        void FontRebuilt(Font font)
        {
            if (text && font == text.font) Bind(false);
        }

        void Bind(bool requestCharacters)
        {
            if (binding || !isActiveAndEnabled || !text || !target || !text.font) return;
            binding = true;
            try
            {
                Font font = text.font;
                // Requesting can synchronously raise textureRebuilt. The guard suppresses that
                // nested call; the binding below reads the atlas after the rebuild has finished.
                if (requestCharacters && font.dynamic)
                    font.RequestCharactersInTexture(text.text, Mathf.Max(0, text.fontSize), text.fontStyle);
                Texture atlas = font.material ? font.material.mainTexture : null;
                if (!atlas || !target) return;
                if (properties == null) properties = new MaterialPropertyBlock();
                target.GetPropertyBlock(properties);
                properties.SetTexture(MainTexture, atlas);
                target.SetPropertyBlock(properties);
            }
            finally { binding = false; }
        }

        void OnDisable() => Unsubscribe();
        void OnDestroy() => Unsubscribe();
        void Unsubscribe()
        {
            if (!subscribed) return;
            Font.textureRebuilt -= FontRebuilt;
            subscribed = false;
            // Keep the last valid property block while disabled; no asset or other property
            // is modified, and re-enabling binds the then-current atlas again.
        }
    }
}
