using SPF.Contracts.Combat;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Runtime.Session;
using Unity.Mathematics;
using UnityEngine;

namespace SurvivorFoundation.Presentation
{
    /// <summary>Opt-in ground-plane authority diagnostics. Horde body/hurt circles coincide; cyan
    /// centers and pink circles show that identity. Weapon visual height does not enter horde damage.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class SvCollisionDebugOverlay : MonoBehaviour
    {
        public SessionHost Host;
        public bool ShowCollisionDebug;
        CombatDebugOverlay m_Overlay;
        WeaponRuntime m_Runtime;
        public int PrimitivesDrawn => m_Overlay?.Count ?? 0;
        public int SegmentsDrawn => m_Overlay?.Segments ?? 0;
        public int DroppedShapes => m_Overlay?.Dropped ?? 0;
        public long UploadedBytes => ShowCollisionDebug ? m_Overlay?.UploadedBytes ?? 0 : 0;
        void LateUpdate() => RenderFrame();
        public void RenderFrame()
        {
            if (!ShowCollisionDebug || Host == null || Host.Session == null) { StopTracing(); m_Overlay?.Clear(); return; }
            var session = Host.Session; session.Sync(); var world = session.World;
            if (!world.HasResource(SvWeapons.Key)) { StopTracing(); m_Overlay?.Clear(); return; }
            var weapons = world.Resource(SvWeapons.Key);
            if (m_Runtime != weapons) { StopTracing(); m_Runtime = weapons; }
            weapons.CollisionDebug.Enabled = true;
            if (m_Overlay == null) m_Overlay = new CombatDebugOverlay(RenderCapabilities.Detect());
            m_Overlay.Clear(); var state = world.Resource(SvKeys.Game); var config = world.Resource(SvKeys.Config);
            Add(CombatShapeKind.Circle, state.Invulnerable > 0 ? CombatShapeRole.Rejected : CombatShapeRole.Hurt, state.Hero, default, config.Settings.HeroRadius);
            Add(CombatShapeKind.Point, CombatShapeRole.Body, state.Hero, default, .06f);
            var positions = world.Column(SvKeys.Position); var info = world.Column(SvKeys.Info);
            int count = math.min(world.Table(SvKeys.Enemy).Count, 48);
            for (int i = 0; i < count; i++)
            {
                if (info[i].Has(EnemyFlags.Dead)) continue;
                Add(CombatShapeKind.Circle, CombatShapeRole.Hurt, positions[i], default, info[i].Radius);
                Add(CombatShapeKind.Point, CombatShapeRole.Body, positions[i], default, .06f);
            }
            for (int i = 0; i < weapons.Projectiles.Length; i++)
            {
                var p = weapons.Projectiles[i]; if (!p.Active) continue;
                Add(CombatShapeKind.Capsule, CombatShapeRole.Attack, p.Previous, p.Position, weapons.Profile(p.ContentId).Radius * p.Scale);
                Add(CombatShapeKind.Line, CombatShapeRole.Motion, p.Position, p.Position + new float2(0, p.Height));
                if(weapons.Profile(p.ContentId).Family==SPF.Contracts.Weapons.WeaponActionFamily.Draw)
                {
                    float2 visual=p.Position+new float2(0,p.Height);float radius=weapons.Profile(p.ContentId).Radius*p.Scale;
                    Add(CombatShapeKind.Point,CombatShapeRole.Motion,visual,default,.04f);
                    Add(CombatShapeKind.Line,CombatShapeRole.Attack,visual,visual+p.Direction*radius);
                }
            }
            var trace = weapons.CollisionDebug;
            for (int i = 0; i < trace.Count && i < 48; i++) DrawTrace(trace.Entries[i]);
            if (trace.HasAccepted && weapons.Tick - trace.LastAccepted.Tick <= 12) DrawTrace(trace.LastAccepted);
            m_Overlay.Draw();
        }
        void DrawTrace(CombatContactTrace trace)
        {
            Add(CombatShapeKind.Capsule, CombatShapeRole.Attack, trace.From, trace.To, trace.Radius);
            Add(CombatShapeKind.Point, trace.Reason == CombatContactReason.Accepted ? CombatShapeRole.Accepted : trace.Reason == CombatContactReason.Candidate ? CombatShapeRole.Motion : CombatShapeRole.Rejected, trace.Contact, default, .1f);
        }
        void Add(CombatShapeKind kind, CombatShapeRole role, float2 a, float2 b, float radius = 0)
            => m_Overlay.Add(new CombatDebugShape { Kind = kind, Role = role, A = a, B = b, Radius = radius });
        void StopTracing() { if (m_Runtime != null) { m_Runtime.CollisionDebug.Enabled = false; m_Runtime.CollisionDebug.Clear(); m_Runtime = null; } }
        void OnDisable() { StopTracing(); m_Overlay?.Clear(); }
        void OnDestroy() { StopTracing(); m_Overlay?.Dispose(); m_Overlay = null; }
    }
}
