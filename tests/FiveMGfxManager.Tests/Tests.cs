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
    W(S+"/brutson/explosion.awc","AWC");
    try { Core.ImportPack(S+"/brutson","Son",log,pr); Check(false,"brut"); } catch(GfxException e){ Check(e.Message.Contains("OpenIV") && Core.ListLibrary().Count==3, "fichiers .awc seuls refuses proprement"); }
    // skin d'arme en .ydr / .ytd seuls : mis dans un paquet FiveM (rpf + assembly.xml)
    byte[] Rsc(string body){ var b=new byte[16].Concat(System.Text.Encoding.ASCII.GetBytes(body)).ToArray(); BitConverter.GetBytes(0x37435352u).CopyTo(b,0); b[4]=13; BitConverter.GetBytes(0x11u).CopyTo(b,8); BitConverter.GetBytes(0x22u).CopyTo(b,12); return b; }
    Directory.CreateDirectory(S+"/Glock Or/stream"); File.WriteAllBytes(S+"/Glock Or/stream/w_pi_pistol.ydr",Rsc("MODELE")); File.WriteAllBytes(S+"/Glock Or/stream/w_pi_pistol.ytd",Rsc("TEXTURE"));
    W(S+"/Glock Or/lisezmoi.txt","x");
    var gl=Core.ImportPack(S+"/Glock Or","Armes",log,pr);
    string pk=S+"/appdata/bibliotheque/"+gl.Id+"/contenu/mods/gfx_glock_or.rpf";
    var ents=Core.ReadRpfEntries(pk);
    Check(ents!=null && ents.Any(x=>x.Path=="assembly.xml") && ents.Any(x=>x.Path=="content/w_pi_pistol.ydr" && x.Ressource), "ydr/ytd seuls mis dans un paquet FiveM");
    string asm=System.Text.Encoding.UTF8.GetString(Core.ReadRpfFile(pk,"assembly.xml"));
    Check(asm.Contains("target=\"Five\"") && asm.Contains("<add source=\"w_pi_pistol.ytd\">w_pi_pistol.ytd</add>") && asm.Contains("<name>Glock Or</name>") && System.Text.RegularExpressions.Regex.IsMatch(asm,"id=\"\\{[0-9A-F-]{36}\\}\""), "assembly.xml lisible par FiveM");
    Check(Core.ReadRpfFile(pk,"content/w_pi_pistol.ytd").SequenceEqual(Rsc("TEXTURE")), "ressource intacte dans le paquet");
    // deux packs d'armes qui remplacent le meme pistolet : choix arme par arme
    { Directory.CreateDirectory(S+"/Glock Noir/stream"); File.WriteAllBytes(S+"/Glock Noir/stream/w_pi_pistol.ydr",Rsc("NOIR")); File.WriteAllBytes(S+"/Glock Noir/stream/w_sb_smg.ydr",Rsc("SMG"));
      var gn=Core.ImportPack(S+"/Glock Noir","Armes",log,pr,"Armes RP");
      Check(Core.WeaponNames(gl.Armes).SequenceEqual(new[]{"w_pi_pistol"}) && Core.WeaponNames(gn.Armes).SequenceEqual(new[]{"w_pi_pistol","w_sb_smg"}), "armes de chaque pack retenues a l'import");
      Check(gn.Dossier=="Armes RP" && Core.LibraryFolders().SequenceEqual(new[]{"Armes RP"}), "pack importe dans un dossier");
      Core.InstallLibraryPack(gl.Id,app,log,pr);
      var wc=Core.WeaponConflicts(gn.Id,app);
      Check(wc.Count==1 && wc[0].Arme=="w_pi_pistol" && wc[0].Fichiers.SequenceEqual(new[]{"w_pi_pistol.ydr"}) && wc[0].Pack.LibId==gl.Id, "arme deja remplacee par un pack installe reperee");
      System.Collections.Generic.List<string> In(string f)=>Core.ReadRpfEntries(f)?.Select(x=>x.Path).Where(x=>x.StartsWith("content/")).OrderBy(x=>x).ToList();
      // garder l'ancienne : le nouveau pack s'installe sans son pistolet
      Core.InstallLibraryPack(gn.Id,app,log,pr,null,new[]{"w_pi_pistol.ydr"});
      Check(In(app+"/mods/gfx_glock_noir.rpf").SequenceEqual(new[]{"content/w_sb_smg.ydr"}) && In(app+"/mods/gfx_glock_or.rpf").Count==2, "garder l'ancienne : le nouveau pack n'a pas le pistolet");
      Core.UninstallPack(Core.InstalledPacks().Single(p=>p.LibId==gn.Id).Id,app,log,pr);
      Check(!File.Exists(app+"/mods/gfx_glock_noir.rpf") && In(app+"/mods/gfx_glock_or.rpf").Count==2, "retrait du nouveau pack : l'ancien intact");
      // prendre la nouvelle : l'ancien pack perd son pistolet, et le recupere au retrait du nouveau
      Core.InstallLibraryPack(gn.Id,app,log,pr,null,new string[0]);
      Check(In(app+"/mods/gfx_glock_or.rpf").SequenceEqual(new[]{"content/w_pi_pistol.ytd"}) && In(app+"/mods/gfx_glock_noir.rpf").Count==2, "prendre la nouvelle : l'ancien pack perd le pistolet");
      Check(Core.ReadRpfFile(app+"/mods/gfx_glock_or.rpf","content/w_pi_pistol.ytd").SequenceEqual(Rsc("TEXTURE")) && Core.WeaponConflicts(gl.Id,app).Single().Arme=="w_pi_pistol", "paquet refait intact, arme toujours suivie");
      Core.UninstallPack(Core.InstalledPacks().Single(p=>p.LibId==gn.Id).Id,app,log,pr);
      Check(In(app+"/mods/gfx_glock_or.rpf").SequenceEqual(new[]{"content/w_pi_pistol.ydr","content/w_pi_pistol.ytd"}) && Core.ReadRpfFile(app+"/mods/gfx_glock_or.rpf","content/w_pi_pistol.ydr").SequenceEqual(Rsc("MODELE")), "retrait du nouveau : l'ancien recupere son pistolet");
      Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
      Check(!File.Exists(app+"/mods/gfx_glock_or.rpf"), "tout retire proprement");
      // renommer, ranger, dossiers
      Core.RenamePack(gn.Id,"Glock Noir RP"); Core.MoveToFolder(new[]{gl.Id},"armes rp"); Core.CreateFolder("Vide");
      Check(Core.GetLibraryPack(gn.Id).Nom=="Glock Noir RP" && Core.GetLibraryPack(gl.Id).Dossier=="Armes RP" && Core.LibraryFolders().SequenceEqual(new[]{"Armes RP","Vide"}), "pack renomme et range, dossier vide garde");
      Core.RenameFolder("Armes RP","Armes"); Check(Core.GetLibraryPack(gl.Id).Dossier=="Armes" && Core.GetLibraryPack(gn.Id).Dossier=="Armes", "dossier renomme");
      Core.DeleteFolder("Armes"); Core.DeleteFolder("Vide");
      Check(Core.GetLibraryPack(gn.Id).Dossier==null && Core.LibraryFolders().Count==0 && Core.ListLibrary().Count==5, "dossier supprime, packs gardes");
      try { Core.RenamePack(gn.Id," "); Check(false,"nom vide"); } catch(GfxException){ Check(true,"nom vide refuse"); }
      Core.DeleteFromLibrary(gn.Id,log); }
    Core.DeleteFromLibrary(gl.Id,log);
    // .rpf d'armes sans assembly.xml (FiveM l'ignorerait) : mis dans un paquet
    { var r=new byte[1024]; var w=new BinaryWriter(new MemoryStream(r));
      w.Write(0x52504637u); w.Write(2u); w.Write(16u); w.Write(0x4E45504Fu);
      w.Write(0u); w.Write(0x7FFFFF00u); w.Write(1u); w.Write(1u);
      w.Write(1UL|(1UL<<40)); w.Write(3u); w.Write(0u);
      w.Write(System.Text.Encoding.ASCII.GetBytes("\0a.ydr\0")); w.Flush();
      r[512]=(byte)'Y'; r[513]=(byte)'D'; r[514]=(byte)'R';
      Directory.CreateDirectory(S+"/rpfarme"); File.WriteAllBytes(S+"/rpfarme/mesarmes.rpf",r);
      var lu=Core.ReadRpfEntries(S+"/rpfarme/mesarmes.rpf");
      Check(lu!=null && lu.Single().Path=="a.ydr" && System.Text.Encoding.ASCII.GetString(Core.ReadRpfFile(S+"/rpfarme/mesarmes.rpf","a.ydr"))=="YDR", "lecture d'un rpf OPEN");
      var ma=Core.ImportPack(S+"/rpfarme/mesarmes.rpf","Armes",log,pr);
      string mp=S+"/appdata/bibliotheque/"+ma.Id+"/contenu/mods/mesarmes.rpf";
      Check(Core.ReadRpfFile(mp,"content/mesarmes.rpf")?.SequenceEqual(r)==true && System.Text.Encoding.UTF8.GetString(Core.ReadRpfFile(mp,"assembly.xml")).Contains("models\\cdimages\\mesarmes.rpf</add>"), "rpf sans assembly.xml mis dans un paquet");
      Core.DeleteFromLibrary(ma.Id,log); }
    // pack de plusieurs armes : une archive par arme dans l'archive, fichier commun en double
    { string mk=S+"/multi"; foreach (var (arme,f) in new[]{("Pistolet","w_pi_pistol.ydr"),("Carabine","w_ar_carbinerifle.ytd")}) {
        Directory.CreateDirectory(mk+"/"+arme+"/stream"); File.WriteAllBytes(mk+"/"+arme+"/stream/"+f,Rsc(arme));
        File.WriteAllBytes(mk+"/"+arme+"/stream/w_commun.ytd",Rsc("COMMUN")); W(mk+"/"+arme+"/fxmanifest.lua","fx_version 'cerulean'");
        System.IO.Compression.ZipFile.CreateFromDirectory(mk+"/"+arme, S+"/multizip_"+arme+".zip"); }
      Directory.CreateDirectory(S+"/multipack"); File.Move(S+"/multizip_Pistolet.zip",S+"/multipack/Pistolet.zip"); File.Move(S+"/multizip_Carabine.zip",S+"/multipack/Carabine.rar.zip");
      System.IO.Compression.ZipFile.CreateFromDirectory(S+"/multipack", S+"/Pack Armes.zip");
      var mp=Core.ImportPack(S+"/Pack Armes.zip","Armes",log,pr);
      string pkm=S+"/appdata/bibliotheque/"+mp.Id+"/contenu/mods/gfx_pack_armes.rpf";
      var em=Core.ReadRpfEntries(pkm)?.Select(x=>x.Path).ToList();
      Check(em!=null && em.Contains("content/w_pi_pistol.ydr") && em.Contains("content/w_ar_carbinerifle.ytd") && em.Count(x=>x=="content/w_commun.ytd")==1, "pack de plusieurs armes (archives dans l'archive) dans un seul paquet");
      Core.DeleteFromLibrary(mp.Id,log);
      // deux versions differentes du meme fichier : refus qui nomme les dossiers
      Directory.CreateDirectory(S+"/variantes/Or"); Directory.CreateDirectory(S+"/variantes/Noir");
      File.WriteAllBytes(S+"/variantes/Or/w_pi_pistol.ytd",Rsc("OR")); File.WriteAllBytes(S+"/variantes/Noir/w_pi_pistol.ytd",Rsc("NOIR"));
      try { Core.ImportPack(S+"/variantes","Armes",log,pr); Check(false,"variantes"); } catch(GfxException e){ Check(e.Message.Contains("Or") && e.Message.Contains("Noir"), "deux versions d'une arme refusees en nommant les dossiers"); }
      // chemin OpenIV en dossiers : mods\update\...\dlc.rpf\x64\models\cdimages\weapons.rpf\*.ydr
      string ov=S+"/openiv/mods/update/x64/dlcpacks/patchday8ng/dlc.rpf/x64/models/cdimages/weapons.rpf";
      Directory.CreateDirectory(ov); File.WriteAllBytes(ov+"/w_sb_smg.ydr",Rsc("SMG"));
      var op=Core.ImportPack(S+"/openiv","Armes",log,pr);
      string oc=S+"/appdata/bibliotheque/"+op.Id+"/contenu";
      Check(Core.ReadRpfEntries(oc+"/mods/gfx_openiv.rpf")?.Any(x=>x.Path=="content/w_sb_smg.ydr")==true && !Directory.Exists(oc+"/mods/update"), "chemin OpenIV en dossiers mis dans un paquet");
      Core.DeleteFromLibrary(op.Id,log); }
    var wp=Core.ImportPack(S+"/weapons.rpf","Armes",log,pr);
    Check(File.Exists(S+"/appdata/bibliotheque/"+wp.Id+"/contenu/mods/weapons.rpf"), ".rpf au nom absent de GTA V pose a la racine de mods (pack FiveM)");
    Core.DeleteFromLibrary(wp.Id,log);
    // fichiers seuls a mettre dans citizen\common\data (BloodFX, kill effects, roue des armes, map, no water)
    W(S+"/cit/BloodFX/core.ypt","SANG"); W(S+"/cit/Kill Effect/timecycle_mods_4.xml","KILL"); W(S+"/cit/Roue/hud.gfx","ROUE");
    W(S+"/cit/Map/ui/minimap.ytd","MAP"); W(S+"/cit/lisezmoi.txt","x");
    var cit=Core.ImportPack(S+"/cit","Graphique",log,pr); string cc=S+"/appdata/bibliotheque/"+cit.Id+"/contenu/citizen/common/data/";
    Check(R(cc+"effects/core.ypt")=="SANG" && R(cc+"timecycle/timecycle_mods_4.xml")=="KILL" && R(cc+"ui/hud.gfx")=="ROUE" && R(cc+"ui/minimap.ytd")=="MAP"
          && Directory.GetFiles(S+"/appdata/bibliotheque/"+cit.Id+"/contenu","*",SearchOption.AllDirectories).Length==4, "BloodFX, kill effect, roue des armes, map ranges dans citizen/common/data");
    Core.DeleteFromLibrary(cit.Id,log);
    W(S+"/eau/No Water/water.xml","SEC"); W(S+"/eau/No Water/LISEZMOI.txt","x");
    Core.InstallPack(S+"/eau",app,log,pr); Check(R(app+"/citizen/common/data/levels/gta5/water.xml")=="SEC", "no water : water.xml dans citizen/common/data/levels/gta5");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Check(!Directory.Exists(app+"/citizen/common/data/levels") && R(app+"/citizen/common/data/timecycle.xml")=="ORIG", "retrait du no water propre");
    // onglet Mods : seuls les .rpf sont gardes, et ils vont dans FiveM.app\mods
    W(S+"/routes/Custom Roads/roads.rpf","ROUTE"); W(S+"/routes/Custom Roads/hud.gfx","x"); W(S+"/routes/lisezmoi.txt","x");
    var mod=Core.ImportPack(S+"/routes",Core.ModType,log,pr); string mc=S+"/appdata/bibliotheque/"+mod.Id+"/contenu";
    Check(mod.Type=="Mod" && R(mc+"/mods/roads.rpf")=="ROUTE" && Directory.GetFiles(mc,"*",SearchOption.AllDirectories).Length==1, "mod : seul le .rpf est garde, a la racine de mods");
    Core.InstallLibraryPack(mod.Id,app,log,pr); Check(R(app+"/mods/roads.rpf")=="ROUTE" && !File.Exists(app+"/citizen/common/data/ui/hud.gfx"), "mod installe dans FiveM.app/mods");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr); Core.DeleteFromLibrary(mod.Id,log);
    Check(!File.Exists(app+"/mods/roads.rpf") && Core.ListLibrary().Count==3, "mod retire et supprime proprement");
    try { Core.ImportPack(S+"/eau",Core.ModType,log,pr); Check(false,"mod sans rpf"); } catch(GfxException e){ Check(e.Message.Contains(".rpf") && Core.ListLibrary().Count==3, "mod sans .rpf refuse"); }
    // onglet QuantV : la nouvelle version remplace celle du pack (et de FiveM.app si le pack est installe)
    string lut="/plugins/reshade-shaders/Textures/QuantV/LUT/";
    W(S+"/qv/QuantV/mods/aQuantV.rpf","QV1"); W(S+"/qv/QuantV/plugins/QuantV.addon","A1"); W(S+"/qv/QuantV/plugins/QuantV.preset.ini","REGLAGES");
    W(S+"/qv/QuantV"+lut+"global.png","G1"); W(S+"/qv/QuantV"+lut+"old.png","OLD");
    var qp=Core.ImportPack(S+"/qv","Graphique",log,pr); Core.InstallLibraryPack(qp.Id,app,log,pr);
    var qf=Core.FindQuantV(); Check(qf.Count==1 && qf[0].Installe && qf[0].Rpf=="mods/aQuantV.rpf", "QuantV trouve dans le pack installe");
    W(S+"/qv2/QuantV 4/mods/aQuantV.rpf","QV2"); W(S+"/qv2/QuantV 4/plugins/QuantV.addon","A2"); W(S+"/qv2/QuantV 4/plugins/QuantV.preset.ini","NEUF");
    W(S+"/qv2/QuantV 4"+lut+"global.png","G2");
    Core.UpdateQuantV(S+"/qv2",app,log,pr); string qc=S+"/appdata/bibliotheque/"+qp.Id+"/contenu";
    Check(R(app+"/mods/aQuantV.rpf")=="QV2" && R(app+"/plugins/QuantV.addon")=="A2" && R(app+lut+"global.png")=="G2" && R(qc+"/mods/aQuantV.rpf")=="QV2"
          && Core.InstalledPacks().Single().LibId==qp.Id, "QuantV mis a jour depuis un dossier (pack et FiveM.app)");
    Check(!File.Exists(app+lut+"old.png") && !File.Exists(qc+lut+"old.png") && R(app+"/plugins/QuantV.preset.ini")=="REGLAGES", "anciens fichiers QuantV retires, reglages .ini gardes");
    W(S+"/QuantV_v5.rpf","QV3"); Core.UpdateQuantV(S+"/QuantV_v5.rpf",app,log,pr);
    Check(R(app+"/mods/QuantV_v5.rpf")=="QV3" && !File.Exists(app+"/mods/aQuantV.rpf") && R(app+"/plugins/QuantV.addon")=="A2", "QuantV mis a jour avec un .rpf seul (renomme)");
    Check(Directory.GetDirectories(S+"/appdata/quantv").Length>=1, "ancienne version de QuantV sauvegardee");
    try { Core.UpdateQuantV(S+"/explosions.rpf",app,log,pr); Check(false,"pas quantv"); } catch(GfxException e){ Check(e.Message.Contains("QuantV") && R(app+"/mods/QuantV_v5.rpf")=="QV3", "mise a jour sans QuantV refusee"); }
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr); Core.DeleteFromLibrary(qp.Id,log);
    Check(!File.Exists(app+"/mods/QuantV_v5.rpf") && !File.Exists(app+"/plugins/QuantV.addon") && Core.ListLibrary().Count==3, "pack QuantV retire proprement");
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
    // --- profils graphiques ---
    string gs=S+"/citizenfx/gta5_settings.xml"; Environment.SetEnvironmentVariable("FIVEM_GFX_SETTINGS", gs);
    var keys=new[]{"Tessellation","LodScale","PedLodBias","VehicleLodBias","ShadowQuality","ReflectionQuality","ReflectionMSAA","SSAO",
      "AnisotropicFiltering","MSAA","TextureQuality","ParticleQuality","WaterQuality","GrassQuality","ShaderQuality","Shadow_SoftShadows",
      "Shadow_Distance","Shadow_LongShadows","Shader_SSA","CityDensity","PedVarietyMultiplier","VehicleVarietyMultiplier","PostFX","DoF",
      "HdStreamingInFlight","MaxLodScale","MotionBlurStrength","FXAA_Enabled","TXAA_Enabled","VSync"};
    W(gs,"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Settings>\n  <configSource>SMC_AUTO</configSource>\n  <graphics>\n"+
         string.Concat(keys.Select(k=>"    <"+k+" value=\"7\" />\n"))+"  </graphics>\n</Settings>\n");
    string orig=File.ReadAllText(gs);
    Check(Core.CurrentGraphicsProfile()==null && !Core.HasGraphicsBackup(), "reglages perso : aucun profil reconnu");
    Core.ApplyGraphicsProfile("Performance",log);
    Check(R(gs).Contains("<GrassQuality value=\"0\"") && R(gs).Contains("<MotionBlurStrength value=\"0.000000\"") && R(gs).Contains("SMC_USER"), "profil Performance ecrit");
    Check(Core.CurrentGraphicsProfile()=="Performance" && Core.HasGraphicsBackup(), "profil Performance reconnu, origine sauvegardee");
    Core.ApplyGraphicsProfile("Qualité",log);
    Check(Core.CurrentGraphicsProfile()=="Qualité" && R(gs).Contains("<Shadow_LongShadows value=\"true\""), "profil Qualite ecrit");
    Core.RestoreGraphics(log);
    Check(File.ReadAllText(gs)==orig && Core.HasGraphicsBackup(), "reglages d'origine remis");

    // --- presets ReShade ---
    string pl=app+"/plugins", sh=pl+"/reshade-shaders/Shaders";
    W(pl+"/ReShade.ini","[GENERAL]\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\nPresetPath=.\\A.ini\n");
    W(pl+"/A.ini","Techniques=Bloom@Bloom.fx,Sharp@qUINT_sharp.fx\n\n[Bloom.fx]\nX=1\n");
    W(pl+"/B.ini","Techniques = Vignette@Vignette.fx\n"); // espaces comme certains presets
    W(pl+"/notes.ini","[x]\ny=1\n");
    W(sh+"/Bloom.fx","#include \"ReShade.fxh\"\n"); W(sh+"/ReShade.fxh","//"); W(sh+"/Vignette.fx","//");
    W(sh+"/qUINT/qUINT_sharp.fx","#include \"qUINT_common.fxh\""); W(sh+"/Commun/qUINT_common.fxh","//");
    W(sh+"/Inutile/Truc.fx","//"); W(sh+"/Inutile2/sous/Machin.fx","//");
    var ps=Core.ListReshadePresets(app);
    Check(ps.Select(p=>p.Nom).SequenceEqual(new[]{"A.ini","B.ini"}) && ps[0].Actif && !ps[1].Actif && ps[0].Effets==2, "presets listes (ReShade.ini et autres .ini exclus)");
    Core.SetActivePreset(app,"B.ini",log);
    Check(Core.ActivePresetName(app)=="B.ini" && R(pl+"/ReShade.ini").Contains("EffectSearchPaths"), "preset active, reste du ReShade.ini garde");
    Check(Core.DuplicatePreset(app,"A.ini","A perso",log)=="A perso.ini" && R(pl+"/A perso.ini")==R(pl+"/A.ini"), "preset duplique");
    try { Core.DuplicatePreset(app,"A.ini","A perso",log); Check(false,"dup"); } catch(GfxException){ Check(true, "copie au meme nom refusee"); }
    Core.SetReshadePerformanceMode(app,true,log);
    Check(Core.ReshadePerformanceMode(app) && R(pl+"/ReShade.ini").Contains("PerformanceMode=1"), "mode performance active");
    Check(Core.UnusedShaderFolders(app).SequenceEqual(new[]{"Inutile","Inutile2"}), "dossiers de shaders inutilises trouves (#include suivis)");
    Check(Core.RetireUnusedShaders(app,log)==2 && !Directory.Exists(sh+"/Inutile") && Core.RetiredShaderFolders().Count==2, "shaders inutilises ranges");
    Core.RestoreRetiredShaders(app,log);
    Check(File.Exists(sh+"/Inutile2/sous/Machin.fx") && Core.RetiredShaderFolders().Count==0, "shaders ranges remis");

    // --- profils complets ---
    Core.ApplyGraphicsProfile("Équilibré",log);
    var lp=Core.ListLibrary()[0];
    Core.InstallLibraryPack(lp.Id,app,log,pr);
    Core.SaveCurrentAsProfile("RP",app,log);
    Check(Core.ListProfiles().Single().Packs.Single()==lp.Id && Core.ListProfiles()[0].Preset=="B.ini", "profil enregistre (pack + preset)");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Core.SetActivePreset(app,"A.ini",log); Core.SetReshadePerformanceMode(app,false,log); Core.ApplyGraphicsProfile("Performance",log);
    Core.ApplyProfile("RP",app,log,pr);
    Check(Core.IsInstalled(lp.Id) && Core.ActivePresetName(app)=="B.ini" && Core.ReshadePerformanceMode(app) && Core.CurrentGraphicsProfile()=="Équilibré",
          "profil applique : pack, preset, mode perf et graphismes remis");
    Environment.SetEnvironmentVariable("FIVEM_GFX_VRAM","0.000000001");
    Check(Core.InstalledSize(false)>0 && Core.HeavyModsWarning()!=null, "alerte VRAM quand les packs pesent trop");
    Environment.SetEnvironmentVariable("FIVEM_GFX_VRAM","64");
    Check(Core.HeavyModsWarning()==null, "pas d'alerte avec assez de VRAM");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    Core.DeleteProfile("RP");
    Check(Core.ListProfiles().Count==0, "profil supprime");
    Core.RestoreGraphics(log);
    Directory.Delete(pl,true);

    // --- sous-dossiers, notes, fichiers suspects, apercu, verification, annulation ---
    Core.CreateFolder("Jour"); Core.CreateFolder("Jour/Soleil");
    Core.MoveToFolder(new[]{lp.Id},"Jour/Soleil");
    Core.RenameFolder("Jour","Matin");
    Check(Core.LibraryFolders().Contains("Matin/Soleil") && Core.ListLibrary().First(p=>p.Id==lp.Id).Dossier=="Matin/Soleil", "sous-dossier renomme avec son parent");
    Core.DeleteFolder("Matin/Soleil");
    Check(Core.ListLibrary().First(p=>p.Id==lp.Id).Dossier=="Matin" && !Core.LibraryFolders().Contains("Matin/Soleil"), "sous-dossier supprime : packs dans le parent");
    Core.DeleteFolder("Matin");
    Check(Core.ListLibrary().First(p=>p.Id==lp.Id).Dossier==null && !Core.LibraryFolders().Contains("Matin"), "dossier supprime");
    Core.SetFavorite(new[]{lp.Id},true); Core.SetRating(lp.Id,4); Core.SetComment(lp.Id,"top");
    var lq=Core.ListLibrary().First(p=>p.Id==lp.Id);
    Check(lq.Favori && lq.Note==4 && lq.Commentaire=="top", "favori, note et commentaire");
    Directory.CreateDirectory(S+"/sus/plugins");
    foreach(var f in new[]{"dxgi.dll","hack.exe","x.dll","lisez.txt"}) File.WriteAllText(S+"/sus/plugins/"+f,"x");
    var sus=Core.SuspiciousFiles(S+"/sus");
    Check(sus.Count==2 && !sus.Any(f=>f.EndsWith("dxgi.dll")), "fichiers suspects (.exe, .dll inconnue ; dxgi.dll ignore)");
    var pv=Core.PreviewInstall(lp.Id,app);
    Check(pv.Nouveaux.Count+pv.Remplaces.Count>0 && pv.Suspects.Count==0, "apercu de l'installation ("+pv.Remplaces.Count+" remplaces, "+pv.Nouveaux.Count+" nouveaux)");
    var rel=pv.Remplaces.Concat(pv.Nouveaux).First(f=>!new[]{".ini",".txt",".cfg",".json",".log"}.Contains(Path.GetExtension(f).ToLowerInvariant()));
    Core.InstallLibraryPack(lp.Id,app,log,pr);
    string good=R(app+"/"+rel);
    Check(Core.CheckInstalledPacks(app).Count==0, "packs installes intacts");
    File.WriteAllText(app+"/"+rel,"remis par une mise a jour du jeu");
    var dmg=Core.CheckInstalledPacks(app);
    Check(dmg.Count==1 && dmg[0].Modifies.Single()==rel, "fichier remis d'origine detecte");
    Core.RepairPack(dmg[0].Pack.Id,app,log);
    Check(R(app+"/"+rel)==good && Core.CheckInstalledPacks(app).Count==0, "pack repare");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);
    var last=Core.LastUndoable();
    Check(last!=null && last.Type=="retrait", "derniere action annulable : le retrait");
    Core.UndoHistory(last.Id,app,log,pr);
    Check(Core.IsInstalled(lp.Id) && Core.LastUndoable()?.Id!=last.Id, "retrait annule (pack reinstalle, l'annulation remonte l'historique)");
    Core.UninstallPack(Core.InstalledPacks().Single().Id,app,log,pr);

    // ancienne config (un seul pack) convertie
    File.WriteAllText(S+"/appdata/config.json","{\"PackInstalle\":{\"Date\":\"d\",\"Id\":\"old\",\"Nom\":\"Vieux\"}}");
    Check(Core.InstalledPacks().Single().Nom=="Vieux", "ancienne config convertie");
    Console.WriteLine("TOUS LES TESTS OK");
  }
}
