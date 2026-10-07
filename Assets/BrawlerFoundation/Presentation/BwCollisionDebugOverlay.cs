using SPF.Contracts.Combat;
using SPF.L2.Weapons;
using SPF.Presentation;
using SPF.Presentation.Combat;
using SPF.Runtime.Session;
using Unity.Mathematics;
using UnityEngine;

namespace BrawlerFoundation.Presentation
{
    /// <summary>Opt-in authoritative weapon collision diagnostics. Cyan=body footprint, pink=hurt
    /// footprint/height, amber=attack sweep, green=accepted contact, red=rejection. Not saved.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class BwCollisionDebugOverlay : MonoBehaviour
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
            if (!ShowCollisionDebug || Host == null || Host.Session == null)
            { StopTracing(); m_Overlay?.Clear(); return; }
            var session = Host.Session; session.Sync(); var world = session.World;
            if (!world.HasResource(BwWeapons.Key) || !world.HasResource(BwBeltKeys.State)) { StopTracing(); m_Overlay?.Clear(); return; }
            var weapons = world.Resource(BwWeapons.Key);
            if (m_Runtime != weapons) { StopTracing(); m_Runtime = weapons; }
            weapons.CollisionDebug.Enabled = true;
            if (m_Overlay == null) m_Overlay = new CombatDebugOverlay(RenderCapabilities.Detect());
            m_Overlay.Clear();
            var ground = world.Column(BwBeltKeys.Ground); var motion = world.Column(BwBeltKeys.Motion); var info = world.Column(BwKeys.Info);
            int count = math.min(world.Table(BwKeys.Fighter).Count, 48);
            for (int i = 0; i < count; i++)
            {
                if (info[i].State == FighterState.KO) continue;
                Add(CombatShapeKind.Box, CombatShapeRole.Hurt, BwBeltRules.Project(ground[i], 0), new float2(BwRules.BodyHalfWidth, BwBeltRules.BodyDepth * BwBeltRules.DepthProjection));
                Add(CombatShapeKind.Circle, CombatShapeRole.Body, BwBeltRules.Project(ground[i], 0), default, BwRules.BodyHalfWidth);
                Add(CombatShapeKind.Box, CombatShapeRole.Hurt, BwBeltRules.Project(ground[i], motion[i].Height + (BwRules.HurtBottom + BwRules.HurtTop) * .5f), new float2(BwRules.BodyHalfWidth, (BwRules.HurtTop - BwRules.HurtBottom) * .5f));
            }
            for (int i = 0; i < weapons.Projectiles.Length; i++)
            {
                var p = weapons.Projectiles[i]; if (!p.Active) continue;
                float radius = weapons.Profile(p.ContentId).Radius * p.Scale;
                Add(CombatShapeKind.Capsule, CombatShapeRole.Attack, BwBeltRules.Project(p.Previous, p.Height), BwBeltRules.Project(p.Position, p.Height), radius);
                Add(CombatShapeKind.Point, CombatShapeRole.Motion, BwBeltRules.Project(p.Position, p.Height), default, .04f);
                if(weapons.Profile(p.ContentId).Family==SPF.Contracts.Weapons.WeaponActionFamily.Draw)
                    Add(CombatShapeKind.Line, CombatShapeRole.Attack, BwBeltRules.Project(p.Position,p.Height), BwBeltRules.Project(p.Position+p.Direction*radius,p.Height));
            }
            var trace = weapons.CollisionDebug;
            for (int i = 0; i < trace.Count && i < 48; i++) DrawTrace(trace.Entries[i]);
            if (trace.HasAccepted && weapons.Tick - trace.LastAccepted.Tick <= 12) DrawTrace(trace.LastAccepted);
            m_Overlay.Draw();
        }
        void DrawTrace(CombatContactTrace trace)
        {
            bool accepted = trace.Reason == CombatContactReason.Accepted;
            Add(CombatShapeKind.Capsule, CombatShapeRole.Attack, BwBeltRules.Project(trace.From, trace.Height), BwBeltRules.Project(trace.To, trace.Height), trace.Radius);
            Add(CombatShapeKind.Point, accepted ? CombatShapeRole.Accepted : trace.Reason == CombatContactReason.Candidate ? CombatShapeRole.Motion : CombatShapeRole.Rejected,
                BwBeltRules.Project(trace.Contact, trace.Height), default, .1f);
        }
        void Add(CombatShapeKind kind, CombatShapeRole role, float2 a, float2 b, float radius = 0)
            => m_Overlay.Add(new CombatDebugShape { Kind = kind, Role = role, A = a, B = b, Radius = radius, Scale = new float2(1, BwBeltRules.DepthProjection) });
        void StopTracing() { if (m_Runtime != null) { m_Runtime.CollisionDebug.Enabled = false; m_Runtime.CollisionDebug.Clear(); m_Runtime = null; } }
        void OnDisable() { StopTracing(); m_Overlay?.Clear(); }
        void OnDestroy() { StopTracing(); m_Overlay?.Dispose(); m_Overlay = null; }
    }
}
