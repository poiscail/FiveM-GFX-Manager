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
      Core.UninstallPack(app,log,pr);
      Check(R(app+"/citizen/common/data/timecycle.xml")=="ORIG", "retrait remet l'original");
      Check(!Directory.Exists(app+"/mods") && !Directory.Exists(app+"/plugins"), "dossiers crees supprimes");
      Check(File.Exists(app+"/citizen/keep.txt"), "fichiers non concernes gardes");
    }
    Core.ExtractArchive(S+"/Rar5.rar", S+"/rarout", log, pr);
    Check(Directory.EnumerateFiles(S+"/rarout","*",SearchOption.AllDirectories).Any(), "decompression RAR5");
    Core.InstallPack(S+"/src",app,log,pr); // dossier deja decompresse
    Check(R(app+"/plugins/dxgi.dll")=="DLL", "install depuis dossier");
    Core.RestoreOriginalBackup(app,log,pr);
    Check(R(app+"/citizen/common/data/timecycle.xml")=="ORIG" && !File.Exists(app+"/mods/vfx.rpf") && Core.InstalledPack()==null, "restauration d'origine");
    Core.InstallPack(S+"/MonPack.zip",app,log,pr);
    Core.InstallReshade(app,null,true,null,log,pr);   // vrai telechargement
    var dll=new FileInfo(app+"/plugins/dxgi.dll"); Check(dll.Length>1000000, "ReShade dxgi.dll ("+dll.Length+" o)");
    Check(Directory.EnumerateFiles(app+"/plugins/reshade-shaders/Shaders","*.fx",SearchOption.AllDirectories).Count()>5, "shaders installes");
    Console.WriteLine(File.ReadAllText(app+"/plugins/ReShade.ini"));
    Core.UninstallReshade(app,log,pr);
    Check(R(app+"/plugins/dxgi.dll")=="DLL" && !File.Exists(app+"/plugins/ReShade.ini") && !Directory.Exists(app+"/plugins/reshade-shaders"), "desinstallation ReShade remet le dll du pack");
    Core.ClearFivemCache(app,log); Check(!Directory.Exists(app+"/data/cache"), "cache vide");
    try { Core.InstallPack(S+"/MonPack.zip",app,log,pr); Check(false,"double install"); } catch(GfxException){ Check(true,"refuse un 2e pack sans retrait"); }
    Console.WriteLine("TOUS LES TESTS OK");
  }
}
