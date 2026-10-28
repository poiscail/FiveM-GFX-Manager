using System; using System.IO; using System.Linq; using FiveMGfxManager;
class T {
  static void Check(bool c, string m){ if(!c) throw new Exception("ECHEC: "+m); Console.WriteLine("  ok - "+m); }
  static string R(string p)=>File.ReadAllText(p).Trim();
  static void Main(string[] a){
    string S=a[0]; Environment.SetEnvironmentVariable("FIVEM_GFX_DATA", S+"/appdata");
    Action<string> log=Console.WriteLine; Action<double> pr=_=>{};
    string app=S+"/FiveM.app"; Directory.CreateDirectory(app+"/citizen/common/data"); Directory.CreateDirectory(app+"/data/cache/x");
    File.WriteAllText(app+"/citizen/common/data/timecycle.xml","ORIG"); File.WriteAllText(app+"/citizen/keep.txt","keep");
    File.SetAttributes(app+"/citizen/common/data/timecycle.xml", FileAttributes.ReadOnly);
    Core.CreateOriginalBackup(app,log,pr);
    foreach (var pack in new[]{"MonPack.zip","MonPack.7z"}) {
      Core.InstallPack(S+"/"+pack,app,log,pr);
      Check(R(app+"/citizen/common/data/timecycle.xml")=="MOD", pack+" remplace");
      Check(File.Exists(app+"/mods/vfx.rpf"), pack+" ajoute");
      Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
      Check(R(app+"/citizen/common/data/timecycle.xml")=="ORIG", "retrait remet l'original");
      Check(!Directory.Exists(app+"/mods") && !Directory.Exists(app+"/plugins"), "dossiers crees supprimes");
      Check(File.Exists(app+"/citizen/keep.txt"), "fichiers non concernes gardes");
    }
    Core.ExtractArchive(S+"/Rar5.rar", S+"/rarout", log, pr);
    Check(Directory.EnumerateFiles(S+"/rarout","*",SearchOption.AllDirectories).Any(), "decompression RAR5");
    Core.InstallPack(S+"/src",app,log,pr); // dossier deja decompresse
    Check(R(app+"/plugins/dxgi.dll")=="DLL", "install depuis dossier");
    Core.RestoreOriginalBackup(app,log,pr);
    Check(R(app+"/citizen/common/data/timecycle.xml")=="ORIG" && !File.Exists(app+"/mods/vfx.rpf") && Core.InstalledPacks().Count==0, "restauration d'origine");
    Core.InstallPack(S+"/MonPack.zip",app,log,pr);
    Core.InstallReshade(app,null,true,null,log,pr);   // vrai telechargement
    var dll=new FileInfo(app+"/plugins/dxgi.dll"); Check(dll.Length>1000000, "ReShade dxgi.dll ("+dll.Length+" o)");
    Check(Directory.EnumerateFiles(app+"/plugins/reshade-shaders/Shaders","*.fx",SearchOption.AllDirectories).Count()>=5 && File.Exists(app+"/plugins/reshade-shaders/Shaders/ReShade.fxh"), "shaders installes"); // la branche slim n'a que 5 .fx
    Console.WriteLine(File.ReadAllText(app+"/plugins/ReShade.ini"));
    Core.UninstallReshade(app,log,pr);
    Check(R(app+"/plugins/dxgi.dll")=="DLL" && !File.Exists(app+"/plugins/ReShade.ini") && !Directory.Exists(app+"/plugins/reshade-shaders"), "desinstallation ReShade remet le dll du pack");
    Core.ClearFivemCache(app,log); Check(!Directory.Exists(app+"/data/cache"), "cache vide");
    try { Core.InstallPack(S+"/MonPack.zip",app,log,pr); Check(false,"conflit"); } catch(GfxException e){ Check(e.Message.Contains("MonPack"),"refuse un 2e pack qui touche les memes fichiers"); }
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);

    // --- bibliotheque : packs graphique, son (format x64 de GTA), armes (.rpf seuls), ajout de mods ---
    void W(string p,string t){ Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p,t); }
    W(S+"/son/Mon Son/x64/audio/sfx/RESIDENT.rpf","SON");
    W(S+"/armes/Pack Armes v2/weapons_player.rpf","ARME"); W(S+"/armes/Pack Armes v2/lisezmoi.txt","x");
    W(S+"/couteau/update/update.rpf","COUTEAU");
    W(S+"/explosions.rpf","BOUM");
    W(S+"/weapons.rpf","INCONNU");
    W(S+"/x64e.rpf","X64E");
    W(S+"/brut/w_pi_pistol.ytd","YTD");
    var g=Core.ImportPack(S+"/MonPack.7z","Graphique",log,pr);
    var son=Core.ImportPack(S+"/son","Son",log,pr);
    var arm=Core.ImportPack(S+"/armes","Armes",log,pr);
    Check(Core.ListLibrary().Count==3 && Core.ListLibrary().Any(p=>p.Type=="Son"), "3 packs dans la bibliotheque");
    Check(Core.ListLibrary().Select(p=>p.Id).SequenceEqual(new[]{g.Id,son.Id,arm.Id}), "bibliotheque triee par date d'ajout");
    try { Core.ImportPack(S+"/MonPack.7z","Graphique",log,pr); Check(false,"doublon"); } catch(GfxException e){ Check(e.Message.Contains("déjà") && Core.ListLibrary().Count==3, "meme pack importe 2 fois refuse"); }
    try { Core.ImportPack(S+"/brut","Armes",log,pr); Check(false,"brut"); } catch(GfxException e){ Check(e.Message.Contains("OpenIV") && Core.ListLibrary().Count==3, "fichiers .ytd seuls refuses proprement"); }
    try { Core.ImportPack(S+"/weapons.rpf","Armes",log,pr); Check(false,"inconnu"); } catch(GfxException e){ Check(e.Message.Contains("weapons.rpf") && Core.ListLibrary().Count==3, ".rpf seul a chemin inconnu refuse"); }
    Core.InstallLibraryPack(g.Id,app,log,pr); Core.InstallLibraryPack(son.Id,app,log,pr); Core.InstallLibraryPack(arm.Id,app,log,pr);
    Check(Core.InstalledPacks().Count==3 && R(app+"/citizen/common/data/timecycle.xml")=="MOD", "3 packs installes ensemble");
    Check(R(app+"/mods/x64/audio/sfx/RESIDENT.rpf")=="SON", "pack son range dans mods/x64");
    Check(R(app+"/mods/x64/audio/sfx/weapons_player.rpf")=="ARME" && !File.Exists(app+"/mods/lisezmoi.txt"), "pack armes : .rpf seul range a son chemin GTA");
    try { Core.InstallLibraryPack(g.Id,app,log,pr); Check(false,"2x"); } catch(GfxException){ Check(true,"meme pack pas installe 2 fois"); }
    try { Core.AddModsToPack(arm.Id,S+"/couteau",log,pr); Check(false,"mods"); } catch(GfxException){ Check(true,"ajout de mods refuse si pack installe"); }
    try { Core.DeleteFromLibrary(arm.Id,log); Check(false,"del"); } catch(GfxException){ Check(true,"suppression refusee si pack installe"); }
    Core.UninstallPack(Core.InstalledPacks().Single(p=>p.LibId==arm.Id).Id,app,log,pr);
    Check(!File.Exists(app+"/mods/x64/audio/sfx/weapons_player.rpf") && R(app+"/mods/x64/audio/sfx/RESIDENT.rpf")=="SON", "retrait d'un pack sans toucher aux autres");
    Core.AddModsToPack(arm.Id,S+"/couteau",log,pr); Core.AddModsToPack(arm.Id,S+"/couteau",log,pr);
    Check(Core.GetLibraryPack(arm.Id).Mods.Count==1, "mod ajoute au pack (sans doublon)");
    Core.InstallLibraryPack(arm.Id,app,log,pr);
    Check(R(app+"/mods/update/update.rpf")=="COUTEAU" && R(app+"/mods/x64/audio/sfx/weapons_player.rpf")=="ARME", "pack avec mod ajoute installe");
    Core.SetPackType(son.Id,"Armes"); Check(Core.GetLibraryPack(son.Id).Type=="Armes" && Core.InstalledPacks().Single(p=>p.LibId==son.Id).Type=="Armes", "changement de type");
    foreach (var p in Core.InstalledPacks().ToList()) Core.UninstallPack(p.Id,app,log,pr);
    Check(R(app+"/citizen/common/data/timecycle.xml")=="ORIG" && !Directory.Exists(app+"/mods"), "tout retire : FiveM propre");
    Core.DeleteFromLibrary(arm.Id,log); Check(Core.ListLibrary().Count==2, "suppression de la bibliotheque");
    // installation directe de .rpf seuls sans bibliotheque
    Core.InstallPack(S+"/explosions.rpf",app,log,pr,"Armes"); Check(R(app+"/mods/x64/audio/sfx/explosions.rpf")=="BOUM", "install directe d'un .rpf de sons");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Core.InstallPack(S+"/x64e.rpf",app,log,pr); Check(R(app+"/mods/x64e.rpf")=="X64E", "x64e.rpf seul range a la racine de mods");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Check(!Directory.Exists(app+"/mods"), "rien ne reste apres les installs directes");

    // --- ReShade et pack qui touchent le meme plugins/dxgi.dll ---
    W(S+"/fake/ReShade64.dll","RESHADE");
    Core.InstallPack(S+"/src",app,log,pr);                       // pack avec plugins/dxgi.dll = DLL
    Core.InstallReshade(app,S+"/fake/ReShade64.dll",false,null,log,pr);
    Check(R(app+"/plugins/dxgi.dll")=="RESHADE", "ReShade installe par-dessus le dll du pack");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Check(R(app+"/plugins/dxgi.dll")=="RESHADE" && R(app+"/citizen/common/data/timecycle.xml")=="ORIG", "retrait du pack garde le dll de ReShade");
    Core.UninstallReshade(app,log,pr);
    Check(!File.Exists(app+"/plugins/dxgi.dll"), "desinstallation ReShade ensuite : plus de dll");
    Core.InstallReshade(app,S+"/fake/ReShade64.dll",false,null,log,pr);
    try { Core.InstallPack(S+"/src",app,log,pr); Check(false,"rs"); } catch(GfxException e){ Check(e.Message.Contains("ReShade") && Core.InstalledPacks().Count==0 && R(app+"/plugins/dxgi.dll")=="RESHADE", "pack refuse s'il ecrase ReShade"); }
    Core.UninstallReshade(app,log,pr);
    Check(!Directory.Exists(app+"/plugins") || !Directory.EnumerateFiles(app+"/plugins","*",SearchOption.AllDirectories).Any(), "plugins vide a la fin");
    // ancienne config (un seul pack) convertie
    File.WriteAllText(S+"/appdata/config.json","{\"PackInstalle\":{\"Date\":\"d\",\"Id\":\"old\",\"Nom\":\"Vieux\"}}");
    Check(Core.InstalledPacks().Single().Nom=="Vieux", "ancienne config convertie");
    Console.WriteLine("TOUS LES TESTS OK");
  }
}
