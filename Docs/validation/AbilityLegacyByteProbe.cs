using System;
using System.IO;
using System.Security.Cryptography;
using SPF.Contracts;
using SPF.Runtime.Session;
using SurvivorFoundation;
using BrawlerFoundation;
class Probe
{
 static void Main(string[] args)
 {
  string dir=args[0];Directory.CreateDirectory(dir);
  var bwMode=BwMode.CreateWeaponBelt(BwBeltConfig.Default,out var bwModule);
  using(var s=SimSession.Create(bwMode,7)){s.Start();s.World.Resource(BwKeys.Game).Send(BwCommandKind.Start);s.Step();for(int i=0;i<12;i++)s.Step();Save("brawler",BwWeaponSave.Describe(s,"stage-e-byte-control"),BwWeaponSave.Capture(s,"stage-e-byte-control"),dir);}
  var svConfig=SvConfig.CreateWeaponCombatExample();var svMode=SvMode.Create(svConfig,out var svModule);
  using(var s=SimSession.Create(svMode,7)){s.Start();s.World.Resource(SvKeys.Game).Send(SvCommandKind.Start);s.Step();for(int i=0;i<12;i++)s.Step();Save("survivor",SvWeaponSave.Describe(s,"stage-e-byte-control"),SvWeaponSave.Capture(s,"stage-e-byte-control"),dir);}
 }
 static void Save(string name,SaveCompatibilityDescriptor d,byte[] bytes,string dir){File.WriteAllBytes(Path.Combine(dir,name+".bin"),bytes);Console.WriteLine(name+" "+d.ContractFingerprint+" "+d.SchemaFingerprint+" "+d.ContentFingerprint+" "+d.VisualFingerprint+" "+d.RawCompatibilityFingerprint+" "+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());}
}
