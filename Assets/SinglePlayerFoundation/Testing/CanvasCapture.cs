using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SPF.Testing
{
    /// <summary>Bounded graphics-test fixture: routes every canvas under a game into its actual camera
    /// target. Captures are unmodified GPU readbacks. It never changes Screen/PlayerSettings or simulation.</summary>
    public sealed class CanvasCapture : IDisposable
    {
        readonly Camera m_Camera;
        readonly RenderTexture m_PreviousTarget;
        readonly float m_PreviousAspect;
        readonly Canvas[] m_Canvases;
        readonly RenderMode[] m_Modes;
        readonly Camera[] m_Cameras;
        readonly float[] m_Distances;
        readonly CanvasScaler[] m_Scalers;
        readonly CanvasScaler.ScaleMode[] m_ScaleModes;
        readonly float[] m_Scales;
        readonly RenderTexture m_Target;
        readonly Texture2D m_Read;
        public int Width { get; }
        public int Height { get; }
        public Color32[] Pixels { get; private set; }
        public int CanvasCount => m_Canvases.Length;
        public RenderTexture Target => m_Target;

        public CanvasCapture(GameObject gameRoot, Camera camera, int width, int height)
        {
            Width = width; Height = height; m_Camera = camera;
            m_PreviousTarget = camera.targetTexture; m_PreviousAspect = camera.aspect;
            m_Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            m_Target.Create(); m_Read = new Texture2D(width, height, TextureFormat.RGBA32, false);
            camera.targetTexture = m_Target; camera.aspect = width / (float)height;
            m_Canvases = gameRoot.transform.GetComponentsInChildren<Canvas>(true);
            m_Modes = new RenderMode[m_Canvases.Length]; m_Cameras = new Camera[m_Canvases.Length]; m_Distances = new float[m_Canvases.Length];
            m_Scalers = new CanvasScaler[m_Canvases.Length]; m_ScaleModes = new CanvasScaler.ScaleMode[m_Canvases.Length]; m_Scales = new float[m_Canvases.Length];
            for (int i = 0; i < m_Canvases.Length; i++)
            {
                var canvas = m_Canvases[i]; m_Modes[i] = canvas.renderMode; m_Cameras[i] = canvas.worldCamera; m_Distances[i] = canvas.planeDistance;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + 1f;
                var scaler = m_Scalers[i] = canvas.GetComponent<CanvasScaler>();
                if (scaler == null) continue;
                m_ScaleModes[i] = scaler.uiScaleMode; m_Scales[i] = scaler.scaleFactor;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = width >= height ? height / 720f : width / 720f;
                canvas.scaleFactor = scaler.scaleFactor;
            }
        }

        public Rect RectOf(GameObject go)
        {
            var corners = new Vector3[4]; ((RectTransform)go.transform).GetWorldCorners(corners);
            var a = m_Camera.WorldToScreenPoint(corners[0]); var b = m_Camera.WorldToScreenPoint(corners[2]);
            return new Rect(a.x, a.y, b.x - a.x, b.y - a.y);
        }

        public PointerEventData Pointer(GameObject target, int id)
        {
            var rect = RectOf(target);
            return new PointerEventData(EventSystem.current) { pointerId = id, position = rect.center, pressPosition = rect.center, pointerPress = target, button = PointerEventData.InputButton.Left };
        }

        public GameObject FirstHit(GameObject target)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(Pointer(target, 1), hits);
            return hits.Count > 0 ? hits[0].gameObject : null;
        }

        public IEnumerator Save(string name, Rect safeArea, params GameObject[] controls)
        {
            Canvas.ForceUpdateCanvases();
            // Batchmode-safe: read the target after normal camera rendering, not WaitForEndOfFrame.
            yield return null; yield return null;
            var previous = RenderTexture.active;
            try { RenderTexture.active = m_Target; m_Read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); m_Read.Apply(false); }
            finally { RenderTexture.active = previous; }
            Pixels = m_Read.GetPixels32();
            string dir = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots", "MobileHud");
            Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, name + ".png"), m_Read.EncodeToPNG());
            var metadata = new StringBuilder();
            metadata.AppendLine("Unmodified camera + all game canvases GPU readback.");
            metadata.AppendLine("Viewport " + Width + " x " + Height + "; safe pixels " + RectText(safeArea));
            foreach (var canvas in m_Canvases) metadata.AppendLine("Canvas " + canvas.name + ": " + canvas.renderMode + ", active=" + canvas.isActiveAndEnabled + ", capture camera=" + (canvas.worldCamera == m_Camera));
            foreach (var control in controls) metadata.AppendLine(control.name + " projected bounds " + RectText(RectOf(control)) + "; first raycast=" + (FirstHit(control) == control));
            File.WriteAllText(Path.Combine(dir, name + ".txt"), metadata.ToString());
        }

        static string RectText(Rect rect) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:F1}, {1:F1}, {2:F1}, {3:F1})", rect.x, rect.y, rect.width, rect.height);

        public int BrightPixels(Rect area)
        {
            int n = 0;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(area.xMin)), x1 = Mathf.Min(Width, Mathf.CeilToInt(area.xMax));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(area.yMin)), y1 = Mathf.Min(Height, Mathf.CeilToInt(area.yMax));
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
            { var p = Pixels[y * Width + x]; if (p.g > 145 && p.b > 145) n++; }
            return n;
        }

        public void Dispose()
        {
            for (int i = 0; i < m_Canvases.Length; i++)
            {
                var canvas = m_Canvases[i]; if (canvas == null) continue;
                canvas.renderMode = m_Modes[i]; canvas.worldCamera = m_Cameras[i]; canvas.planeDistance = m_Distances[i];
                if (m_Scalers[i] != null) { m_Scalers[i].uiScaleMode = m_ScaleModes[i]; m_Scalers[i].scaleFactor = m_Scales[i]; }
            }
            if (m_Camera != null) { m_Camera.targetTexture = m_PreviousTarget; m_Camera.aspect = m_PreviousAspect; }
            m_Target.Release(); Object.Destroy(m_Target); Object.Destroy(m_Read);
        }
    }
}
