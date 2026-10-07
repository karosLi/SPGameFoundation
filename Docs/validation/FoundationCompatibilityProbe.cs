// Cold, .NET-harness-only composition inventory. Not included in Unity Assets or production builds.
// Reproduction and the exact baseline are documented in FoundationCompatibilityProbe-20261007.md.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text.Json;
using SPF.Contracts;
using SPF.Runtime.Composition;
using SPF.Runtime.Scheduling;
using SPF.Runtime.Session;
using SPF.Runtime.World;
using SnakeFoundation;
using RpgFoundation;
using SurvivorFoundation;
using PlatformerFoundation;
using DefenseFoundation;
using PuzzleFoundation;
using SlingFoundation;
using BrawlerFoundation;
using StoryFoundation;
using ShooterFoundation;

static class Program
{
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly List<UnityEngine.Object> OwnedConfigs = new List<UnityEngine.Object>();
    static string ExecutionPlanDirectory;

    static T Own<T>(T config) where T : UnityEngine.Object
    {
        OwnedConfigs.Add(config);
        return config;
    }

    static object Field(object owner, string name)
    {
        var field = owner.GetType().GetField(name, InstanceFields)
            ?? throw new InvalidOperationException($"Probe requires {owner.GetType().FullName}.{name}");
        return field.GetValue(owner);
    }

    static string TypeName(Type type) => type.IsGenericType
        ? type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">"
        : type.FullName;

    // Resolve diagnostic key names after creating the session. Never export process-local AccessKey.Id.
    static Dictionary<int, string> KeyNames()
    {
        var names = new Dictionary<int, string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a =>
            a.GetName().Name.EndsWith("Foundation.Runtime") || a.GetName().Name.StartsWith("SPF.")))
        foreach (var type in assembly.GetTypes())
        foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            if (typeof(AccessKey).IsAssignableFrom(field.FieldType) && field.GetValue(null) is AccessKey key)
                names[key.Id] = key.Name;
        return names;
    }

    static object Inspect(string id, ModeDefinition mode, string clock = "fixed")
    {
        try
        {
            using var session = SimSession.Create(mode, 123);
            var world = session.World;
            var keys = KeyNames();
            var levels = ((IEnumerable)Field(world, "m_LevelTables")).Cast<object>().ToHashSet();
            var levelResources = ((IEnumerable)Field(world, "m_LevelResources")).Cast<object>().ToHashSet();
            var resources = ((IEnumerable)Field(world, "m_ResourceList")).Cast<object>().ToArray();
            var resourceKeys = ((IEnumerable)Field(world, "m_ResourceKeys")).Cast<AccessKey>().ToArray();

            var tables = world.Tables.Select(table =>
            {
                var byKey = (IDictionary)Field(table, "m_Columns");
                var reverse = new Dictionary<object, string>();
                foreach (DictionaryEntry entry in byKey)
                    reverse[entry.Value] = keys[(int)entry.Key];
                var columns = ((IEnumerable)Field(table, "m_ColumnList")).Cast<object>().Select(column => new
                {
                    key = reverse.TryGetValue(column, out var name) ? name : "$pooledDeadFlag",
                    type = TypeName(column.GetType().GenericTypeArguments[0]),
                }).ToArray();
                return new
                {
                    key = table.Key.Name,
                    capacity = table.Capacity,
                    pooled = table.IsPooled,
                    levelScoped = levels.Contains(table),
                    trackChangedRows = table.Changes != null,
                    columns,
                };
            }).ToArray();

            var resourceRows = resources.Select((resource, index) =>
            {
                var capacity = resource.GetType().GetProperty("Capacity");
                var saved = resource.GetType().GetProperty("Saved");
                var lengths = new Dictionary<string, object>();
                foreach (var field in resource.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var value = field.GetValue(resource);
                    if (value is Array array) lengths[field.Name + ".Length"] = array.Length;
                    else if (field.FieldType.IsGenericType &&
                        field.FieldType.GetGenericTypeDefinition().FullName == "Unity.Collections.NativeArray`1")
                        lengths[field.Name + ".Length"] = field.FieldType.GetProperty("Length").GetValue(value);
                }
                return new
                {
                    key = resourceKeys[index].Name,
                    type = TypeName(resource.GetType()),
                    levelScoped = levelResources.Contains(resource),
                    snapshotHook = resource is ISnapshotResource,
                    resetHook = resource is IResettableResource,
                    syncHook = resource is ISyncResource,
                    disposable = resource is IDisposable,
                    capacity = capacity?.GetValue(resource),
                    saved = saved?.GetValue(resource),
                    publicArrayLengths = lengths,
                };
            }).ToArray();

            // Use the pipeline's captured declarations and registration provenance. Never re-register
            // modules or call Declare again just to inspect metadata (both may have side effects).
            var plan = session.Pipeline.GetExecutionPlan();
            if (ExecutionPlanDirectory != null)
            {
                string text = plan.ToText(), dot = plan.ToDot();
                if (text != session.Pipeline.GetExecutionPlan().ToText() || dot != session.Pipeline.GetExecutionPlan().ToDot())
                    throw new InvalidOperationException("Execution plan changed on repeated export: " + id);
                if (plan.Systems.Any(s => !s.Source.IsKnown))
                    throw new InvalidOperationException("Composed system has unknown provenance: " + id);
                File.WriteAllText(Path.Combine(ExecutionPlanDirectory, id + ".txt"), text);
                File.WriteAllText(Path.Combine(ExecutionPlanDirectory, id + ".dot"), dot);
            }
            var systems = plan.Systems.Select(entry =>
            {
                var system = session.Pipeline.GetSystem(entry.ScheduleIndex);
                return new
                {
                    type = entry.SystemType,
                    phase = entry.Phase.ToString(),
                    order = entry.Order,
                    registrationIndex = entry.RegistrationIndex,
                    snapshotHook = system is ISnapshotSystem,
                    resetHook = system is IResettableSystem,
                    barrier = entry.IsBarrier,
                };
            }).ToArray();

            var destroyQueue = Field(Field(world, "m_DestroyQueue"), "m_Queue");
            return new
            {
                id,
                modules = mode.Modules.Select(module => module.Id).ToArray(),
                settings = new { mode.Settings.TickRate, mode.Settings.MaxTicksPerFrame, mode.Settings.DestroyQueueCapacity },
                effectiveDestroyQueueCapacity = destroyQueue.GetType().GetProperty("Capacity").GetValue(destroyQueue),
                bootstrapClock = clock, // Annotated from Bootstrap source; no Bootstrap/Unity lifecycle is run.
                tables,
                resources = resourceRows,
                systems,
                snapshotGaps = world.SnapshotGaps(), // Empty is not proof that all mutable state is saved.
            };
        }
        finally
        {
            foreach (var module in mode.Modules) UnityEngine.Object.DestroyImmediate(module);
            UnityEngine.Object.DestroyImmediate(mode);
        }
    }

    static void Main(string[] args)
    {
        if (args.Length != 0)
        {
            if (args.Length != 2 || args[0] != "--execution-plans")
                throw new ArgumentException("Usage: Probe [--execution-plans OUTPUT_DIRECTORY]");
            ExecutionPlanDirectory = args[1];
            Directory.CreateDirectory(ExecutionPlanDirectory);
        }
        try
        {
            var rows = new List<object>();
            GameplayModuleAsset module;
            GameplayModuleAsset[] modules;
            rows.Add(Inspect("snake.classic", ModeDefinition.Create(
                new[] { SnakeGameModule.Create(Own(SnakeConfig.CreateDefault())) }, SessionSettings.Default)));
            rows.Add(Inspect("rpg.classic", RpgMode.Create(Own(RpgConfig.CreateDefault()), out modules)));
            rows.Add(Inspect("survivor.classic", SvMode.Create(Own(SvConfig.CreateDefault()), out module)));
            rows.Add(Inspect("platformer.classic", PlMode.Create(out module)));
            rows.Add(Inspect("defense.classic", TdMode.Create(out module)));
            rows.Add(Inspect("puzzle.classic", M3Mode.Create(out module), "manual in bootstrap"));
            rows.Add(Inspect("sling.classic", SlMode.Create(out module)));
            rows.Add(Inspect("brawler.classic", BwMode.Create(out module)));
            rows.Add(Inspect("story.classic", StMode.Create(out module), "manual in bootstrap"));
            rows.Add(Inspect("shooter.default", ShooterMode.Create(Own(ShooterConfig.CreateDefault()), out module)));
            rows.Add(Inspect("survivor.guard", SvMode.Create(Own(SvConfig.CreateGuardExample()), out module)));
            rows.Add(Inspect("survivor.flying_sword", SvMode.Create(Own(SvConfig.CreateFlyingSwordExample()), out module)));
            rows.Add(Inspect("brawler.belt", BwMode.CreateBeltScroller(BwBeltConfig.Default, out module)));
            rows.Add(Inspect("survivor.crossed_blades", SvMode.Create(Own(SvConfig.CreateCrossedBladeExample()), out module)));
            rows.Add(Inspect("survivor.mobile", SvMode.Create(Own(SvConfig.CreateMobileCombatExample()), out module)));
            rows.Add(Inspect("survivor.weapons", SvMode.Create(Own(SvConfig.CreateWeaponCombatExample()), out module)));
            rows.Add(Inspect("brawler.shared_combat", BwMode.CreateSharedCombat(BwSharedCombatConfig.Default, out module)));
            rows.Add(Inspect("brawler.mobile", BwMode.CreateMobileCombat(out module)));
            rows.Add(Inspect("brawler.weapon_belt", BwMode.CreateWeaponBelt(BwBeltConfig.Default, out module)));
            Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            foreach (var config in OwnedConfigs) UnityEngine.Object.DestroyImmediate(config);
        }
    }
}
