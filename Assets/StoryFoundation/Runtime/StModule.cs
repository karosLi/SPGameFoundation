using System.Collections.Generic;
using System.IO;
using SPF.Contracts;
using SPF.L2.Narrative;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;
using UnityEngine;

namespace StoryFoundation
{
    public enum StCommandKind : byte { Start, Advance, Choose, Menu }

    public struct StCommand
    {
        public StCommandKind Kind;
        public int Choice;
    }

    public enum Expression : byte { Neutral, Smile, Worried, Sad, Surprised }

    /// <summary>
    /// The story state inside the simulation: the dialogue runner (program counter + variables) and the scene the
    /// script's events set up. One tick per player action (manual clock), so every step can be undone or saved.
    /// </summary>
    public sealed class StState : ISnapshotResource, IResettableResource
    {
        public readonly DialogueRunner Runner;
        public readonly Queue<StCommand> Commands = new Queue<StCommand>();
        public bool InStory;
        public byte Background;          // 0 square, 1 bridge
        public bool MiraShown;
        public Expression Face;
        public int Lanterns;             // lit lanterns (0..3)
        public int Version;

        public StState(DialogueGraph graph) => Runner = new DialogueRunner(graph);

        public void Send(StCommandKind kind, int choice = 0) => Commands.Enqueue(new StCommand { Kind = kind, Choice = choice });

        public void OnReset()
        {
            Commands.Clear();
            InStory = false;
            Version++;
        }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write(InStory); w.Write(Background); w.Write(MiraShown); w.Write((byte)Face); w.Write(Lanterns);
            Runner.Write(w);
            w.Write(Commands.Count);
            foreach (var c in Commands) { w.Write((byte)c.Kind); w.Write(c.Choice); }
        }

        public void ReadSnapshot(BinaryReader r)
        {
            InStory = r.ReadBoolean(); Background = r.ReadByte(); MiraShown = r.ReadBoolean(); Face = (Expression)r.ReadByte(); Lanterns = r.ReadInt32();
            Runner.Read(r);
            Commands.Clear();
            int n = r.ReadInt32();
            if (n < 0 || n > 64) throw new InvalidDataException("Invalid command queue in snapshot.");
            for (int i = 0; i < n; i++) Commands.Enqueue(new StCommand { Kind = (StCommandKind)r.ReadByte(), Choice = r.ReadInt32() });
            Version++;
        }
    }

    public static class StKeys
    {
        public static readonly ResourceKey<StState> State = new ResourceKey<StState>("St.State");
    }

    /// <summary>Applies the player's step to the runner and turns script events into scene state.</summary>
    sealed class StorySystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var s = context.World.Resource(StKeys.State);
            var run = s.Runner;
            while (s.Commands.Count > 0)
            {
                var c = s.Commands.Dequeue();
                switch (c.Kind)
                {
                    case StCommandKind.Start:
                        System.Array.Clear(run.Vars, 0, run.Vars.Length);
                        s.Background = 0; s.MiraShown = false; s.Face = Expression.Neutral; s.Lanterns = 0;
                        s.InStory = true;
                        run.Start("start");
                        break;
                    case StCommandKind.Advance: run.Advance(); break;
                    case StCommandKind.Choose: run.Choose(c.Choice); break;
                    case StCommandKind.Menu: s.InStory = false; break;
                }
                Apply(s);
                s.Version++;
            }
            return dependency;
        }

        static void Apply(StState s)
        {
            var run = s.Runner;
            for (int i = 0; i < run.EventCount; i++)
            {
                string e = run.EventName(i);
                if (e == "show") s.MiraShown = true;
                else if (e == "hide") s.MiraShown = false;
                else if (e == "bg:square") s.Background = 0;
                else if (e == "bg:bridge") s.Background = 1;
                else if (e.StartsWith("lanterns:")) s.Lanterns = e[e.Length - 1] - '0';
                else if (e == "face:neutral") s.Face = Expression.Neutral;
                else if (e == "face:smile") s.Face = Expression.Smile;
                else if (e == "face:worried") s.Face = Expression.Worried;
                else if (e == "face:sad") s.Face = Expression.Sad;
                else if (e == "face:surprised") s.Face = Expression.Surprised;
            }
            run.ClearEvents();
        }
    }

    public sealed class StModule : GameplayModuleAsset
    {
        static DialogueGraph s_Graph;

        /// <summary>The compiled story (compiled once per process).</summary>
        public static DialogueGraph Graph => s_Graph ??= DialogueCompiler.Compile(StContent.Script);

        public static StModule Create()
        {
            var module = CreateInstance<StModule>();
            module.hideFlags = HideFlags.DontSave;
            return module;
        }

        public override void DeclareData(WorldLayout layout) => layout.Resource(StKeys.State, new StState(Graph));

        public override void RegisterSystems(SystemRegistry registry) => registry.Add(new StorySystem());
    }

    public static class StMode
    {
        public static ModeDefinition Create(out GameplayModuleAsset module)
        {
            module = StModule.Create();
            return ModeDefinition.Create(new[] { module }, SessionSettings.Default);
        }
    }
}
