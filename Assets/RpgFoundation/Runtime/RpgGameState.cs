using System.Collections.Generic;
using System.IO;
using SPF.Contracts;
using SPF.Runtime.Persistence;
using Unity.Mathematics;

namespace RpgFoundation
{
    /// <summary>
    /// The hero's persistent progress: survives floors (the world is rebuilt per floor) and is what the
    /// save file holds. Main-thread data, read and written by main-thread systems only.
    /// </summary>
    public sealed class HeroProfile : ISaveData
    {
        public const int MaxInventory = 12;

        public int Version => 1;
        public uint RunSeed = 1;
        public int Floor = 1;
        public int Level = 1;
        public int Xp;
        public int Gold;
        public int Potions;
        public int Weapon;
        public int Armour;
        public float HealthFraction = 1f;
        public int Kills;
        public readonly List<int> Inventory = new List<int>();

        public void Reset(uint runSeed, int potions)
        {
            RunSeed = runSeed;
            Floor = 1;
            Level = 1;
            Xp = Gold = Kills = 0;
            Potions = potions;
            Weapon = Armour = 0;
            HealthFraction = 1f;
            Inventory.Clear();
        }

        public void CopyFrom(HeroProfile other)
        {
            RunSeed = other.RunSeed; Floor = other.Floor; Level = other.Level; Xp = other.Xp; Gold = other.Gold;
            Potions = other.Potions; Weapon = other.Weapon; Armour = other.Armour; HealthFraction = other.HealthFraction;
            Kills = other.Kills;
            Inventory.Clear();
            Inventory.AddRange(other.Inventory);
        }

        public void Write(BinaryWriter w)
        {
            w.Write(RunSeed); w.Write(Floor); w.Write(Level); w.Write(Xp); w.Write(Gold); w.Write(Potions);
            w.Write(Weapon); w.Write(Armour); w.Write(HealthFraction); w.Write(Kills);
            w.Write(Inventory.Count);
            foreach (int item in Inventory) w.Write(item);
        }

        public bool Read(BinaryReader r, int version)
        {
            RunSeed = r.ReadUInt32(); Floor = r.ReadInt32(); Level = r.ReadInt32(); Xp = r.ReadInt32(); Gold = r.ReadInt32();
            Potions = r.ReadInt32(); Weapon = r.ReadInt32(); Armour = r.ReadInt32(); HealthFraction = r.ReadSingle(); Kills = r.ReadInt32();
            int count = r.ReadInt32();
            if (count < 0 || count > MaxInventory) return false;
            Inventory.Clear();
            for (int i = 0; i < count; i++) Inventory.Add(r.ReadInt32());
            return Floor >= 1 && Level >= 1;
        }
    }

    /// <summary>Game flow, commands from the UI, input and the hero profile (main-thread resource).</summary>
    public sealed class RpgGameState : ISnapshotResource, IResettableResource
    {
        public RpgFlow Flow = RpgFlow.Menu;
        public HeroProfile Profile = new HeroProfile();
        /// <summary>Profile at the start of the current floor (what a retry restores / what was saved).</summary>
        public HeroProfile FloorStart = new HeroProfile();
        public EntityHandle Hero;
        public InputFrame Input;
        /// <summary>Flow commands (new game, descend, retry, menu); consumed by the floor system.</summary>
        public readonly Queue<RpgCommand> FlowCommands = new Queue<RpgCommand>();
        /// <summary>Inventory commands (equip); consumed by the inventory system.</summary>
        public readonly Queue<RpgCommand> InventoryCommands = new Queue<RpgCommand>();
        public int2 StairsCell;
        public int2 StartCell;
        public int MonstersAlive;
        public int FloorMonsters;
        public bool BossAlive;
        /// <summary>Bumped when anything the HUD shows changes outside per-frame values.</summary>
        public int Version;
        /// <summary>Set when the floor changed (renderers rebuild the map mesh, the game layer saves).</summary>
        public int FloorBuilds;
        public string Message = "";

        public void Send(RpgCommandKind kind, int argument = 0)
        {
            var command = new RpgCommand { Kind = kind, Argument = argument };
            if (kind == RpgCommandKind.Equip || kind == RpgCommandKind.UsePotion) InventoryCommands.Enqueue(command);
            else FlowCommands.Enqueue(command);
        }

        public int FinalFloor;

        /// <summary>Session restart (also after a rejected snapshot): back to the menu, profile kept.</summary>
        public void OnReset()
        {
            Flow = RpgFlow.Menu;
            Hero = EntityHandle.Null;
            Input = default;
            FlowCommands.Clear();
            InventoryCommands.Clear();
            MonstersAlive = FloorMonsters = 0;
            BossAlive = false;
            Message = "";
            Version++;
        }

        public void WriteSnapshot(BinaryWriter w)
        {
            w.Write((int)Flow);
            Profile.Write(w);
            FloorStart.Write(w);
            NativeIO.Write(w, Hero);
            NativeIO.WriteValue(w, Input);
            WriteCommands(w, FlowCommands);
            WriteCommands(w, InventoryCommands);
            w.Write(StairsCell.x); w.Write(StairsCell.y);
            w.Write(StartCell.x); w.Write(StartCell.y);
            w.Write(MonstersAlive); w.Write(FloorMonsters); w.Write(BossAlive);
            w.Write(Message ?? "");
            w.Write(FinalFloor);
        }

        public void ReadSnapshot(BinaryReader r)
        {
            Flow = (RpgFlow)r.ReadInt32();
            if (!Profile.Read(r, Profile.Version) || !FloorStart.Read(r, FloorStart.Version))
                throw new InvalidDataException("Invalid hero profile in snapshot.");
            Hero = NativeIO.ReadHandle(r);
            Input = NativeIO.ReadValue<InputFrame>(r);
            ReadCommands(r, FlowCommands);
            ReadCommands(r, InventoryCommands);
            StairsCell = new int2(r.ReadInt32(), r.ReadInt32());
            StartCell = new int2(r.ReadInt32(), r.ReadInt32());
            MonstersAlive = r.ReadInt32(); FloorMonsters = r.ReadInt32(); BossAlive = r.ReadBoolean();
            Message = r.ReadString();
            FinalFloor = r.ReadInt32();
            // Presentation keys off these counters: a restore is a new floor and new HUD state for it.
            FloorBuilds++;
            Version++;
        }

        static void WriteCommands(BinaryWriter w, Queue<RpgCommand> queue)
        {
            w.Write(queue.Count);
            foreach (var command in queue) { w.Write((int)command.Kind); w.Write(command.Argument); }
        }

        static void ReadCommands(BinaryReader r, Queue<RpgCommand> queue)
        {
            queue.Clear();
            int count = r.ReadInt32();
            if (count < 0 || count > 1024) throw new InvalidDataException("Invalid command queue in snapshot.");
            for (int i = 0; i < count; i++)
                queue.Enqueue(new RpgCommand { Kind = (RpgCommandKind)r.ReadInt32(), Argument = r.ReadInt32() });
        }
    }
}
