using Common;
using FezEngine.Tools;
using FezGame;
using FezGame.Services;
using HatModLoader.Installers;
using HatModLoader.Source.AssemblyResolving;
using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;
using HatModLoader.Source.ModDefinition;

namespace HatModLoader.Source
{
    public class Hat
    {
        public static readonly Version Version = new("1.2.1");

        private static readonly string ModsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");

        private static readonly IList<string> IgnoredModNames = InitializeIgnoredModsList();

        private static readonly IList<string> PriorityModNames = InitializePriorityList();

        public List<ModContainer> Mods { get; } = new();

        public int InvalidModsCount { get; private set; }

        public static string VersionString
        {
            get
            {
#if DEBUG
                return $"{Version}-dev";
#else
                return Version.ToString();
#endif
            }
        }

        public static Hat Instance { get; private set; }

        private readonly Fez _fezGame;

        public Hat(Fez fez)
        {
            Instance = this;
            _fezGame = fez;
            Initialize();
        }

        private void Initialize()
        {
            Logger.Log("HAT", $"HAT Mod Loader {VersionString}");
            
            if (CheckModsFolder())
            {
                if (GetModProxies(out var proxies))
                {
                    if (GetModList(proxies, out var mods))
                    {
                        ResolveDependencies(mods);
                        LoadMods();
                        return;     // HAT initialized
                    }
                }
            }
            
            Logger.Log("HAT", LogSeverity.Warning, "Skip the initialization process...");
        }

        private static bool CheckModsFolder()
        {
            if (!Directory.Exists(ModsDirectory))
            {
                Logger.Log("HAT", LogSeverity.Warning,"'Mods' directory not found. Creating it...");
                Directory.CreateDirectory(ModsDirectory);
                return false;
            }

            return true;
        }

        private static bool GetModProxies(out IEnumerable<IFileProxy> proxies)
        {
            proxies = new IEnumerable<IFileProxy>[]
                {
                    DirectoryFileProxy.EnumerateInDirectory(ModsDirectory),
                    ZipFileProxy.EnumerateInDirectory(ModsDirectory)
                }
                .SelectMany(x => x);

            if (!proxies.Any())
            {
                Logger.Log("HAT", LogSeverity.Warning, "There are no mods inside 'Mods' directory.");
                return false;
            }

            return true;
        }

        private static bool GetModList(in IEnumerable<IFileProxy> proxies, out IList<ModContainer> mods)
        {
            mods = new List<ModContainer>();
            foreach (var proxy in proxies.Where(fp => !IgnoredModNames.Contains(fp.ContainerName)))
            {
                if (Metadata.TryLoad(proxy, out var metadata))
                {
                    mods.Add(new ModContainer(proxy, metadata));
                }
            }

            if (mods.Count < 1)
            {
                Logger.Log("HAT", LogSeverity.Warning, "There are no mods to load. Perhaps they are all in 'ignorelist.txt'.");
                return false;
            }

            return true;
        }

        private void ResolveDependencies(IList<ModContainer> mods)
        {
            var resolverResult = ModDependencyResolver.Resolve(mods, PriorityModNames);
            Mods.AddRange(resolverResult.LoadOrder);
            InvalidModsCount = resolverResult.Invalid.Count;
            
            foreach (var node in resolverResult.Invalid)
            {
                Logger.Log("HAT", $"Mod '{node.Mod.Metadata.Name}' is invalid: {node.Details}"); 
            }
            
            Logger.Log("HAT", "The loading order of mods:");
            foreach (var mod in Mods)
            {
                Logger.Log("HAT", $"  {mod.Metadata.Name} by {mod.Metadata.Author} version {mod.Metadata.Version}");
            }
        }

        private void LoadMods()
        {
            var assetModCount = 0;
            var codeModsCount = 0;
            
            foreach (var mod in Mods)
            {
                if (AssetMod.TryLoad(mod.FileProxy, out var assetMod))
                {
                    mod.AssetMod = assetMod;
                    assetModCount += 1;
                }

                if (CodeMod.TryLoad(mod.FileProxy, mod.Metadata, out var codeMod))
                {
                    mod.CodeMod = codeMod;
                    codeModsCount += 1;
                }
            }
            
            var modsText = $"{Mods.Count} mod{(Mods.Count != 1 ? "s" : "")}";
            var codeModsText = $"{codeModsCount} code mod{(codeModsCount != 1 ? "s" : "")}";
            var assetModsText = $"{assetModCount} asset mod{(assetModCount != 1 ? "s" : "")}";
            Logger.Log("HAT", $"Successfully loaded {modsText} ({codeModsText} and {assetModsText})");
        }
        
        public void InitializeAssemblies()
        {
            foreach (var mod in Mods)
            {
                mod.Initialize(_fezGame);
            }
        }

        public void InitializeComponents()
        {
            foreach (var mod in Mods)
            {
                mod.InjectComponents();
            }
        }
    
        public IEnumerable<Asset> GetFullAssetList()
        {
            var assets = new List<Asset>();
            foreach (var mod in Mods)
            {
                assets.AddRange(mod.GetAssets());
            }

            return assets;
        }

        public void OnGameActivated()
        {
            var changedAssets = new List<Asset>();
            foreach (var mod in Mods)
            {
                foreach (var asset in mod.ReloadAssets())
                {
                    if (!asset.Extension.Equals(".fxc", StringComparison.OrdinalIgnoreCase))
                    {
                        changedAssets.Add(asset);
                    }
                }
            }
            
            if (changedAssets.Count < 1)
            {
                return;
            }

            foreach (var asset in changedAssets)
            {
                if (asset.IsRemoved)
                {
                    AssetManagementInstaller.RemoveAsset(asset);
                    if (!asset.IsMusicFile)
                    {
                        AssetManagementInstaller.EvictFromCommon(asset.AssetPath);
                    }
                }
                else
                {
                    AssetManagementInstaller.InjectAsset(asset);
                    if (!asset.IsMusicFile)
                    {
                        AssetManagementInstaller.PatchInCommon(asset.AssetPath);
                    }
                }
            }

            Logger.Log("HAT", $"Reloaded {changedAssets.Count} asset(s)");

            var levelManager = ServiceHelper.Get<IGameLevelManager>();
            var currentLevel = levelManager.Name;
            if (!string.IsNullOrEmpty(currentLevel))
            {
                levelManager.Name = null;
                levelManager.ChangeLevel(currentLevel);
                Logger.Log("HAT", $"Reloading {currentLevel}...");
            }
        }

        public static void RegisterRequiredDependencyResolvers()
        {
            AssemblyResolverRegistry.Register(new HatSubdirectoryAssemblyResolver("MonoMod"));
            AssemblyResolverRegistry.Register(new HatSubdirectoryAssemblyResolver("FEZRepacker.Core"));
        }

        private static IList<string> InitializeIgnoredModsList()
        {
            var ignoredModsNamesFilePath = Path.Combine(ModsDirectory, "ignorelist.txt");
            const string defaultContent =
                "# List of directories and zip archives to ignore when loading mods, one per line.\n" +
                "# Lines starting with # will be ignored.\n\n" +
                "ExampleDirectoryModName\n" +
                "ExampleZipPackageName.zip\n";
            return ModsTextListLoader.LoadOrCreateDefault(ignoredModsNamesFilePath, defaultContent);
        }

        private static IList<string> InitializePriorityList()
        {
            var priorityListFilePath = Path.Combine(ModsDirectory, "prioritylist.txt");
            const string defaultContent = "# List of directories and zip archives to prioritize during mod loading.\n" +
                                          "# If present on this list, the mod will be loaded before other mods not listed here or listed below it,\n" +
                                          "# including newer versions of the same mod. However, it does not override dependency ordering.\n" +
                                          "# Lines starting with # will be ignored.\n\n" +
                                          "ExampleDirectoryModName\n" +
                                          "ExampleZipPackageName.zip\n";
            return ModsTextListLoader.LoadOrCreateDefault(priorityListFilePath, defaultContent);
        }
    }
}