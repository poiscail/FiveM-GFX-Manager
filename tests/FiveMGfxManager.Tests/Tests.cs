using System; using System.IO; using System.Linq; using FiveMGfxManager;
class T {
  static void Check(bool c, string m){ if(!c) throw new Exception("ECHEC: "+m); Console.WriteLine("  ok - "+m); }
  static string R(string p)=>File.ReadAllText(p).Trim();
  static void Main(string[] a){
    string S=a[0]; Environment.SetEnvironmentVariable("FIVEM_GFX_DATA", S+"/appdata");
    Action<string> log=Console.WriteLine; Action<double> pr=_=>{};
    Environment.SetEnvironmentVariable("FIVEM_GFX_GTA", S+"/gta"); // faux dossier GTA V
    foreach (var f in new[]{"x64a.rpf","x64/audio/sfx/RESIDENT.rpf","x64/audio/sfx/WEAPONS_PLAYER.rpf","x64/audio/sfx/POLICE_SCANNER.rpf",
                            "update/x64/dlcpacks/mpheist/dlc.rpf","update/x64/dlcpacks/mpbeach/dlc.rpf","mods/x64/audio/sfx/EXPLOSIONS.rpf"})
      { Directory.CreateDirectory(Path.GetDirectoryName(S+"/gta/"+f)); File.WriteAllText(S+"/gta/"+f,"GTA"); }
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
    var wp=Core.ImportPack(S+"/weapons.rpf","Armes",log,pr);
    Check(File.Exists(S+"/appdata/bibliotheque/"+wp.Id+"/contenu/mods/weapons.rpf"), ".rpf au nom absent de GTA V pose a la racine de mods (pack FiveM)");
    Core.DeleteFromLibrary(wp.Id,log);
    Core.InstallLibraryPack(g.Id,app,log,pr); Core.InstallLibraryPack(son.Id,app,log,pr); Core.InstallLibraryPack(arm.Id,app,log,pr);
    Check(Core.InstalledPacks().Count==3 && R(app+"/citizen/common/data/timecycle.xml")=="MOD", "3 packs installes ensemble");
    Check(R(S+"/gta/x64/audio/sfx/RESIDENT.rpf")=="SON" && !File.Exists(app+"/mods/x64/audio/sfx/RESIDENT.rpf"), "pack son pose dans GTA V/x64/audio (FiveM ne lit pas ces sons dans mods)");
    Check(R(S+"/gta/x64/audio/sfx/WEAPONS_PLAYER.rpf")=="ARME" && !File.Exists(app+"/mods/lisezmoi.txt"), "pack armes : .rpf seul range a son chemin GTA");
    W(S+"/son2/RESIDENT.rpf","SON2");
    var son2=Core.ImportPack(S+"/son2","Son",log,pr);
    var cf=Core.ConflictingPacks(son2.Id);
    Check(cf.Count==1 && cf[0].LibId==son.Id, "nouveau pack son : l'ancien pack son est detecte (a retirer)");
    try { Core.InstallLibraryPack(son2.Id,app,log,pr); Check(false,"son2"); } catch(GfxException e){ Check(e.Message.Contains("Retire d'abord") && R(S+"/gta/x64/audio/sfx/RESIDENT.rpf")=="SON", "2e pack son refuse tant que l'ancien est installe"); }
    Core.DeleteFromLibrary(son2.Id,log);
    Check(Core.ConflictingPacks(arm.Id).Count==0, "pas de conflit entre pack son et pack armes");
    try { Core.InstallLibraryPack(g.Id,app,log,pr); Check(false,"2x"); } catch(GfxException){ Check(true,"meme pack pas installe 2 fois"); }
    try { Core.AddModsToPack(arm.Id,S+"/couteau",log,pr); Check(false,"mods"); } catch(GfxException){ Check(true,"ajout de mods refuse si pack installe"); }
    try { Core.DeleteFromLibrary(arm.Id,log); Check(false,"del"); } catch(GfxException){ Check(true,"suppression refusee si pack installe"); }
    Core.UninstallPack(Core.InstalledPacks().Single(p=>p.LibId==arm.Id).Id,app,log,pr);
    Check(R(S+"/gta/x64/audio/sfx/WEAPONS_PLAYER.rpf")=="GTA" && R(S+"/gta/x64/audio/sfx/RESIDENT.rpf")=="SON", "retrait d'un pack : son d'origine de GTA V remis, sans toucher aux autres");
    Core.AddModsToPack(arm.Id,S+"/couteau",log,pr); Core.AddModsToPack(arm.Id,S+"/couteau",log,pr);
    Check(Core.GetLibraryPack(arm.Id).Mods.Count==1, "mod ajoute au pack (sans doublon)");
    Core.InstallLibraryPack(arm.Id,app,log,pr);
    Check(R(app+"/mods/update/update.rpf")=="COUTEAU" && R(S+"/gta/x64/audio/sfx/WEAPONS_PLAYER.rpf")=="ARME", "pack avec mod ajoute installe");
    Core.SetPackType(son.Id,"Armes"); Check(Core.GetLibraryPack(son.Id).Type=="Armes" && Core.InstalledPacks().Single(p=>p.LibId==son.Id).Type=="Armes", "changement de type");
    foreach (var p in Core.InstalledPacks().ToList()) Core.UninstallPack(p.Id,app,log,pr);
    Check(R(app+"/citizen/common/data/timecycle.xml")=="ORIG" && !Directory.Exists(app+"/mods") && R(S+"/gta/x64/audio/sfx/RESIDENT.rpf")=="GTA" && R(S+"/gta/x64/audio/sfx/WEAPONS_PLAYER.rpf")=="GTA", "tout retire : FiveM et GTA V propres");
    Core.DeleteFromLibrary(arm.Id,log); Check(Core.ListLibrary().Count==2, "suppression de la bibliotheque");
    // sons d'armes deja presents ailleurs avant d'installer un pack son
    W(app+"/mods/autre/x64/audio/sfx/RESIDENT.rpf","VIEUX");
    W(app+"/mods/x64/audio/sfx/RESIDENT.rpf","ANCIEN"); // copie laissee par une ancienne version (FiveM ne la lit pas)
    W(app+"/mods/casse.rpf","pas un rpf");
    W(S+"/gta/mods/x64/audio/sfx/RESIDENT.rpf","OPENIV");
    using (var bw=new BinaryWriter(File.Create(app+"/mods/addon.rpf"))) { // RPF7 non chiffre : content/x64/audio/sfx/WEAPONS_PLAYER.rpf
      var nm=new[]{"","content","x64","audio","sfx","WEAPONS_PLAYER.rpf"}; var off=new int[nm.Length]; var tbl=new System.Collections.Generic.List<byte>();
      for(int i=0;i<nm.Length;i++){ off[i]=tbl.Count; tbl.AddRange(System.Text.Encoding.ASCII.GetBytes(nm[i])); tbl.Add(0); }
      while(tbl.Count%16!=0) tbl.Add(0);
      bw.Write(0x52504637u); bw.Write(nm.Length); bw.Write(tbl.Count); bw.Write(0x4E45504Fu);
      for(int i=0;i<5;i++){ bw.Write((uint)off[i]); bw.Write(0x7FFFFF00u); bw.Write(i+1); bw.Write(1); }
      bw.Write((ulong)off[5] | (5UL<<16) | (1UL<<40)); bw.Write(0UL);
      bw.Write(tbl.ToArray()); while(bw.BaseStream.Length<1024) bw.Write((byte)0);
    }
    Check(Core.FindWeaponSounds(g.Id,app).Count==0, "pas de recherche de sons pour un pack graphique");
    var hits=Core.FindWeaponSounds(son.Id,app); foreach(var h in hits) log("  trouve : "+h);
    Check(hits.Where(h=>h.Deplacable).Select(h=>h.Rel).OrderBy(r=>r).SequenceEqual(new[]{"mods/autre/x64/audio/sfx/RESIDENT.rpf","mods/x64/audio/sfx/RESIDENT.rpf"}), "son d'arme seul dans mods (et vieille copie dans mods/x64) trouve");
    Check(hits.Any(h=>h.Rel=="mods/addon.rpf" && h.Dedans=="content/x64/audio/sfx/WEAPONS_PLAYER.rpf"), "son d'arme trouve dans un .rpf de mods");
    Check(hits.Any(h=>h.Rel==null && h.Chemin.Replace('\\','/').EndsWith("gta/mods/x64/audio/sfx/RESIDENT.rpf")), "son d'arme trouve dans GTA V/mods");
    Core.InstallLibraryPack(son.Id,app,log,pr,hits);
    Check(!File.Exists(app+"/mods/autre/x64/audio/sfx/RESIDENT.rpf") && !File.Exists(app+"/mods/x64/audio/sfx/RESIDENT.rpf") && R(S+"/gta/x64/audio/sfx/RESIDENT.rpf")=="SON" && File.Exists(app+"/mods/addon.rpf"), "son d'arme mis de cote a l'installation (pas les autres mods)");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Check(R(app+"/mods/autre/x64/audio/sfx/RESIDENT.rpf")=="VIEUX" && R(app+"/mods/x64/audio/sfx/RESIDENT.rpf")=="ANCIEN" && R(S+"/gta/x64/audio/sfx/RESIDENT.rpf")=="GTA", "son mis de cote remis au retrait du pack");
    Directory.Delete(app+"/mods",true); File.Delete(S+"/gta/mods/x64/audio/sfx/RESIDENT.rpf");
    // installation directe de .rpf seuls sans bibliotheque
    Core.InstallPack(S+"/explosions.rpf",app,log,pr,"Armes"); Check(R(S+"/gta/x64/audio/sfx/explosions.rpf")=="BOUM", "install directe d'un .rpf de sons");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Check(!File.Exists(S+"/gta/x64/audio/sfx/explosions.rpf"), "son ajoute a GTA V supprime au retrait");
    Core.InstallPack(S+"/x64e.rpf",app,log,pr); Check(R(app+"/mods/x64e.rpf")=="X64E", "x64e.rpf seul range a la racine de mods");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    // chemins cherches dans le dossier de GTA V
    W(S+"/scanner/Sons Police/POLICE_SCANNER.rpf","RADIO");
    Core.InstallPack(S+"/scanner",app,log,pr,"Son"); Check(R(S+"/gta/x64/audio/sfx/POLICE_SCANNER.rpf")=="RADIO", ".rpf seul range au chemin trouve dans GTA V");
    Core.RestoreOriginalBackup(app,log,pr);
    Check(R(S+"/gta/x64/audio/sfx/POLICE_SCANNER.rpf")=="GTA" && Core.InstalledPacks().Count==0, "restauration d'origine : sons de GTA V remis aussi");
    W(S+"/heist/mpheist/dlc.rpf","HEIST");
    Core.InstallPack(S+"/heist",app,log,pr); Check(R(app+"/mods/update/x64/dlcpacks/mpheist/dlc.rpf")=="HEIST", "dlc.rpf range grace a son dossier mpheist");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    W(S+"/dlc/dlc.rpf","?");
    try { Core.InstallPack(S+"/dlc/dlc.rpf",app,log,pr); Check(false,"dlc"); } catch(GfxException e){ Check(e.Message.Contains("plusieurs endroits") && Core.InstalledPacks().Count==0, "dlc.rpf sans dossier refuse (ambigu)"); }
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
    Core.InstallPack(S+"/src",app,log,pr);
    Check(R(app+"/plugins/dxgi.dll")=="DLL", "pack installe par-dessus ReShade");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Check(R(app+"/plugins/dxgi.dll")=="RESHADE" && R(app+"/citizen/common/data/timecycle.xml")=="ORIG", "retrait du pack remet le dll de ReShade");
    Core.InstallPack(S+"/src",app,log,pr);
    Core.UninstallReshade(app,log,pr);
    Check(R(app+"/plugins/dxgi.dll")=="DLL" && !File.Exists(app+"/plugins/ReShade.ini"), "desinstallation ReShade garde le dll du pack installe par-dessus");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Check(!File.Exists(app+"/plugins/dxgi.dll") && R(app+"/citizen/common/data/timecycle.xml")=="ORIG", "puis retrait du pack : plus de dll");
    Check(!Directory.Exists(app+"/plugins") || !Directory.EnumerateFiles(app+"/plugins","*",SearchOption.AllDirectories).Any(), "plugins vide a la fin");
    // ancienne config (un seul pack) convertie
    File.WriteAllText(S+"/appdata/config.json","{\"PackInstalle\":{\"Date\":\"d\",\"Id\":\"old\",\"Nom\":\"Vieux\"}}");
    Check(Core.InstalledPacks().Single().Nom=="Vieux", "ancienne config convertie");
    Console.WriteLine("TOUS LES TESTS OK");
  }
}
