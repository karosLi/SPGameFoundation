using Unity.Mathematics;
using UnityEngine;

namespace SPF.Presentation.Sprites
{
    /// <summary>
    /// The global 2D light list for lit sprite batches (<see cref="SpriteBatch.SetLighting"/>): ambient plus up to
    /// <see cref="MaxLights"/> point lights, pushed to shaders once per frame. Fixed arrays, so a frame's light
    /// setup allocates nothing. Pick the lights that matter (nearest the camera) when a level has more.
    /// </summary>
    public static class SpriteLighting
    {
        public const int MaxLights = 8;

        static readonly Vector4[] s_Position = new Vector4[MaxLights];
        static readonly Vector4[] s_Color = new Vector4[MaxLights];
        static readonly int s_PositionId = Shader.PropertyToID("_SPFLightPos");
        static readonly int s_ColorId = Shader.PropertyToID("_SPFLightColor");
        static readonly int s_AmbientId = Shader.PropertyToID("_SPFAmbient");
        static Color s_Ambient = Color.white;

        public static int Count { get; private set; }

        /// <summary>Starts this frame's light list.</summary>
        public static void Begin(Color ambient)
        {
            s_Ambient = ambient;
            Count = 0;
        }

        /// <summary>Adds a point light; returns false when the list is full.</summary>
        /// <param name="height">Height above the sprite plane: low lights graze (strong relief), high ones flatten.</param>
        public static bool Add(float2 position, float radius, Color color, float intensity = 1f, float height = 1.2f)
        {
            if (Count >= MaxLights || radius <= 0f) return false;
            s_Position[Count] = new Vector4(position.x, position.y, height, 1f / (radius * radius));
            s_Color[Count] = new Vector4(color.r * intensity, color.g * intensity, color.b * intensity, 0f);
            Count++;
            return true;
        }

        /// <summary>Pushes the list to the shaders (call once per frame, before the batches draw).</summary>
        public static void Apply()
        {
            for (int i = Count; i < MaxLights; i++) s_Color[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(s_PositionId, s_Position);
            Shader.SetGlobalVectorArray(s_ColorId, s_Color);
            Shader.SetGlobalVector(s_AmbientId, new Vector4(s_Ambient.r, s_Ambient.g, s_Ambient.b, Count));
        }

        /// <summary>Light reaching a point with an upward normal (CPU mirror of the shader, for tests and gameplay).</summary>
        public static float3 Evaluate(float2 point, float3 normal)
        {
            float3 light = new float3(s_Ambient.r, s_Ambient.g, s_Ambient.b);
            for (int k = 0; k < Count; k++)
            {
                var p = s_Position[k];
                float3 d = new float3(p.x - point.x, p.y - point.y, p.z);
                float falloff = math.saturate(1f - math.lengthsq(d.xy) * p.w);
                light += new float3(s_Color[k].x, s_Color[k].y, s_Color[k].z) * (falloff * falloff * math.saturate(math.dot(normal, math.normalize(d))));
            }
            return light;
        }
    }
}
