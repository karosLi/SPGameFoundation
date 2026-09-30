using System;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using SPF.L1.Body;
using Unity.Mathematics;

namespace SnakeFoundation.Tests
{
    /// <summary>
    /// Headless preview: simulates a match and exports what the camera would see (body nodes sampled with
    /// the renderer's algorithm, food, props, portals) as JSON; Tools/DotnetHarness/preview.py draws it.
    /// Explicit: runs only when requested (SPF_PREVIEW_OUT = output directory).
    /// </summary>
    public class SnakePreviewExport
    {
        [Test, Explicit("Writes preview files; run on demand")]
        public void ExportFrames()
        {
            string dir = Environment.GetEnvironmentVariable("SPF_PREVIEW_OUT");
            if (string.IsNullOrEmpty(dir)) Assert.Ignore("SPF_PREVIEW_OUT not set");
            Directory.CreateDirectory(dir);

            using var t = new SnakeTestWorld(aiPerRegion: 150, foodPerChunk: 150, propsPerChunk: 1, seed: 2024);
            t.Step(60);
            t.StartPlayer();
            var skins = t.Runtime.Skins;
            int[] exportTicks = { 150, 450, 900 };
            int exported = 0;
            for (int tick = 1; tick <= 900; tick++)
            {
                // The player circles and boosts now and then.
                t.Game.Command = new PlayerCommand { Direction = new float2(math.cos(tick * 0.02f), math.sin(tick * 0.02f)), Boost = tick % 150 < 20 };
                t.Step();
                if (t.Game.Flow == GameFlow.GameOver) { t.StartPlayer(); }
                if (exported < exportTicks.Length && tick == exportTicks[exported])
                    File.WriteAllText(Path.Combine(dir, $"frame{exported++}.json"), Export(t, skins));
            }
        }

        static string Export(SnakeTestWorld t, Skin[] skins)
        {
            var inv = CultureInfo.InvariantCulture;
            var world = t.World;
            var s = t.Runtime.Settings;
            float2 focus = t.Game.Focus;
            float half = 60f;
            var sb = new StringBuilder();
            sb.Append("{\"focus\":[").Append(focus.x.ToString(inv)).Append(',').Append(focus.y.ToString(inv)).Append("],\"half\":").Append(half.ToString(inv));
            var region = t.Runtime.Regions[t.Game.ActiveRegion];
            sb.Append(",\"region\":[").Append(region.Min.x.ToString(inv)).Append(',').Append(region.Min.y.ToString(inv)).Append(',').Append(region.Max.x.ToString(inv)).Append(',').Append(region.Max.y.ToString(inv)).Append(']');
            sb.Append(",\"tick\":").Append(world.Resource(SnakeKeys.Game).NextSnakeId);

            sb.Append(",\"food\":[");
            var fp = world.Column(SnakeKeys.FoodPosition);
            var fi = world.Column(SnakeKeys.FoodInfo);
            bool first = true;
            for (int i = 0; i < world.Table(SnakeKeys.Food).Count; i++)
            {
                if (math.any(math.abs(fp[i] - focus) > half + 2)) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append('[').Append(fp[i].x.ToString("F2", inv)).Append(',').Append(fp[i].y.ToString("F2", inv)).Append(',').Append(fi[i].Radius.ToString("F2", inv)).Append(',').Append(fi[i].Color).Append(']');
            }
            sb.Append("],\"snakes\":[");
            var table = world.Table(SnakeKeys.Snake);
            var heads = table.Column(SnakeKeys.Head);
            var headings = table.Column(SnakeKeys.Heading);
            var trails = table.Column(SnakeKeys.Trail);
            var radii = table.Column(SnakeKeys.Radius);
            var infos = table.Column(SnakeKeys.Info);
            var masses = table.Column(SnakeKeys.Mass);
            var points = world.Resource(SnakeKeys.Bodies).Points;
            first = true;
            for (int row = 0; row < table.Count; row++)
            {
                var info = infos[row];
                if (info.Region != t.Game.ActiveRegion) continue;
                var b = table.Column(SnakeKeys.Bounds)[row];
                if (b.x > focus.x + half || b.z < focus.x - half || b.y > focus.y + half || b.w < focus.y - half) continue;
                var skin = skins[info.Skin % skins.Length];
                float radius = radii[row];
                float spacing = s.NodeSpacing(radius);
                int nodes = TrailMath.NodeCount(trails[row], heads[row], s.TrailSpacing, spacing);
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"player\":").Append(info.Has(SnakeFlags.Player) ? "true" : "false")
                  .Append(",\"mass\":").Append(masses[row].ToString("F1", inv))
                  .Append(",\"r\":").Append(radius.ToString("F3", inv))
                  .Append(",\"a\":[").Append(skin.Primary.r).Append(',').Append(skin.Primary.g).Append(',').Append(skin.Primary.b).Append(']')
                  .Append(",\"b\":[").Append(skin.Secondary.r).Append(',').Append(skin.Secondary.g).Append(',').Append(skin.Secondary.b).Append(']')
                  .Append(",\"alpha\":").Append(skin.Alpha.ToString("F2", inv))
                  .Append(",\"stripe\":").Append(skin.Stripe)
                  .Append(",\"heading\":[").Append(headings[row].x.ToString("F3", inv)).Append(',').Append(headings[row].y.ToString("F3", inv)).Append(']')
                  .Append(",\"nodes\":[");
                for (int n = 0; n < nodes; n++)
                {
                    float2 p = TrailMath.SampleBehind(trails[row], points, heads[row], n * spacing, s.TrailSpacing);
                    if (n > 0) sb.Append(',');
                    sb.Append('[').Append(p.x.ToString("F2", inv)).Append(',').Append(p.y.ToString("F2", inv)).Append(']');
                }
                sb.Append("]}");
            }
            sb.Append("],\"portals\":[");
            first = true;
            for (int i = 0; i < t.Runtime.PortalCount; i++)
            {
                var portal = t.Runtime.Portals[i];
                if (portal.FromRegion != t.Game.ActiveRegion) continue;
                if (!first) sb.Append(','); first = false;
                sb.Append('[').Append(portal.Position.x.ToString(inv)).Append(',').Append(portal.Position.y.ToString(inv)).Append(',').Append(portal.Radius.ToString(inv)).Append(']');
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
