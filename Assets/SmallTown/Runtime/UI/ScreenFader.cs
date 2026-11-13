using System.Collections;
using UnityEngine;

namespace SmallTown.UI
{
    /// <summary>Fades the view to black and back for transitions such as an arrest. A quad just in front of the eyes.</summary>
    public sealed class ScreenFader : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] Renderer quad;
        MaterialPropertyBlock block;

        public static ScreenFader Instance { get; private set; }
        public float Alpha { get; private set; }

        public void Configure(Renderer fadeQuad) => quad = fadeQuad;

        void Awake()
        {
            Instance = this;
            Apply(0f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public IEnumerator Fade(float target, float seconds)
        {
            float start = Alpha;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                Apply(Mathf.Lerp(start, target, t / seconds));
                yield return null;
            }
            Apply(target);
        }

        public void Apply(float alpha)
        {
            Alpha = Mathf.Clamp01(alpha);
            if (quad == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            quad.enabled = Alpha > 0.001f;
            block.SetColor(BaseColor, new Color(0f, 0f, 0f, Alpha));
            quad.SetPropertyBlock(block);
        }
    }
}
