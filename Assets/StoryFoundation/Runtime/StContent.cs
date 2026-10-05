namespace StoryFoundation
{
    /// <summary>
    /// The story: "The Lantern Keeper" — a short night-festival scene with a trust variable, a locked choice,
    /// scene events and two endings. Text lives in the string table (English and Chinese).
    /// Events: "bg:&lt;scene&gt;", "face:&lt;expression&gt;", "show" / "hide" (Mira's portrait), "lanterns:&lt;0..3&gt;".
    /// </summary>
    public static class StContent
    {
        public const string Script = @"
=== start
# bg:square
# lanterns:0
narrator: n.arrive
# show
# face:neutral
mira: m.hello
* c.help -> help
* c.ask -> ask
* c.leave -> leave_early

=== ask
mira: m.story
~ trust += 1
-> help_offer

=== help_offer
mira: m.need
* c.help -> help
* c.leave -> leave_early

=== help
# face:smile
~ trust += 1
~ lit += 1
# lanterns:1
mira: m.thanks
narrator: n.wind
# lanterns:0
# face:worried
mira: m.wind
* c.shield -> shield
* c.relight [if trust >= 2] -> relight
* c.shrug -> shrug

=== shield
~ trust += 1
~ lit += 1
# lanterns:2
# face:smile
mira: m.shield
-> finale

=== relight
~ trust += 2
~ lit += 2
# lanterns:3
# face:surprised
mira: m.relight
-> finale

=== shrug
~ trust -= 1
# face:sad
mira: m.shrug
-> finale

=== finale
? trust >= 3 -> good_end
# bg:bridge
narrator: n.alone
-> END

=== good_end
# bg:bridge
# face:smile
mira: m.together
narrator: n.end_good
-> END

=== leave_early
# hide
narrator: n.left
-> END
";

        public const string Strings =
"key,en,zh\n" +
"narrator,\"\",\"\"\n" +
"mira,Mira,米拉\n" +
"n.arrive,\"The festival square is dark. Only one girl is still awake, untangling paper lanterns.\",节日广场一片漆黑。只有一个女孩还醒着，正在解开纸灯笼。\n" +
"m.hello,\"Oh! A visitor. The lanterns must be lit before the moon rises, and I'm all thumbs tonight.\",哦！有客人。月亮升起前灯笼必须点亮，可我今晚笨手笨脚的。\n" +
"c.help,Help her with the lanterns,帮她挂灯笼\n" +
"c.ask,Ask about the festival,问问这个节日\n" +
"c.leave,Wish her luck and walk on,祝她好运，然后离开\n" +
"m.story,\"Every year we light a lantern for everyone who left the village. Last year I was the only one who came.\",每年我们都会为离开村子的人点一盏灯。去年只有我一个人来。\n" +
"m.need,\"Would you hold the ladder?\",你能帮我扶一下梯子吗？\n" +
"m.thanks,\"There! The first one is burning. You have steady hands.\",好了！第一盏亮了。你的手真稳。\n" +
"n.wind,A cold wind sweeps down from the hills.,一阵冷风从山上吹下来。\n" +
"m.wind,\"No, no, no — it's blowing them out!\",不，不，不——风要把它们吹灭了！\n" +
"c.shield,Shield the flames with your coat,用外套挡住火苗\n" +
"c.relight,Relight them all with your own lamp,用你自己的灯把它们全部重新点亮\n" +
"c.shrug,It's only a few lanterns,不过是几盏灯笼而已\n" +
"m.shield,\"Clever! They're holding. Thank you.\",聪明！火苗稳住了。谢谢你。\n" +
"m.relight,\"You carried a flame all this way? Then you were coming here all along.\",你一路带着火种来的？原来你本来就是要来这里的。\n" +
"m.shrug,\"Only a few... I suppose so.\",不过几盏……大概是吧。\n" +
"n.alone,\"The moon rises over a half-lit square. You cross the bridge alone.\",月亮升起，照着半明半暗的广场。你独自走过了桥。\n" +
"m.together,\"Will you come back next year? I'll save you a lantern.\",明年你还会来吗？我给你留一盏灯。\n" +
"n.end_good,\"Every lantern in the square burns as you cross the bridge together.\",你们一起走过桥时，广场上的每一盏灯都亮着。\n" +
"n.left,\"Behind you, the square stays dark.\",在你身后，广场依旧一片漆黑。\n";
    }
}
