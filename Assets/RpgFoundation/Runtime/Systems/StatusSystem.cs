using SPF.Contracts;
using SPF.Contracts.Collections;
using SPF.Runtime.Scheduling;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace RpgFoundation.Systems
{
    /// <summary>
    /// Body (Burst, parallel): runs burn and poison down and queues their damage every half second as
    /// armour-ignoring hits, so status deaths go through the normal resolve (loot, XP, hero death).
    /// </summary>
    sealed class StatusSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.Body;

        public override void Declare(AccessDeclaration access) => access
            .Read(RpgKeys.Info).Read(RpgKeys.Position).Write(RpgKeys.Status).Write(RpgKeys.Hits);

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            return new StatusJob
            {
                Info = context.Column(RpgKeys.Info),
                Position = context.Column(RpgKeys.Position),
                Status = context.Column(RpgKeys.Status),
                Hits = context.Resource(RpgKeys.Hits).AsWriter(),
                DeltaTime = context.Time.DeltaTime,
            }.Schedule(context.Count(RpgKeys.Actor), 64, dependency);
        }

        [BurstCompile(CompileSynchronously = true)]
        struct StatusJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<ActorInfo> Info;
            [ReadOnly] public NativeArray<float2> Position;
            public NativeArray<StatusState> Status;
            public ParallelQueue<HitEvent>.Writer Hits;
            public float DeltaTime;

            public void Execute(int i)
            {
                var s = Status[i];
                if (!s.Burning && !s.Poisoned) return;
                if (Info[i].Has(ActorFlags.Dead)) { s.Burn = s.Poison = 0f; Status[i] = s; return; }
                s.Tick -= DeltaTime;
                if (s.Tick <= 1e-4f)
                {
                    s.Tick += StatusState.TickInterval;
                    float burn = s.Burning ? s.BurnDps * StatusState.TickInterval : 0f;
                    float poison = s.Poisoned ? s.PoisonDps * StatusState.TickInterval : 0f;
                    if (burn + poison > 0f)
                        Hits.TryAdd(new HitEvent
                        {
                            AttackerId = -1, TargetRow = i, Damage = burn + poison, Position = Position[i], IgnoreArmour = true,
                            Source = burn >= poison ? HitSource.Burn : HitSource.Poison,
                        });
                }
                s.Burn = math.max(s.Burn - DeltaTime, 0f);
                s.Poison = math.max(s.Poison - DeltaTime, 0f);
                if (!s.Burning) s.BurnDps = 0f;
                if (!s.Poisoned) s.PoisonDps = 0f;
                if (!s.Burning && !s.Poisoned) s.Tick = 0f;
                Status[i] = s;
            }
        }
    }
}
