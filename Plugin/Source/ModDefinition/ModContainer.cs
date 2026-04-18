using Common;
using FezEngine.Tools;
using HatModLoader.Source.AssemblyResolving;
using HatModLoader.Source.Assets;
using HatModLoader.Source.FileProxies;
using Microsoft.Xna.Framework;

namespace HatModLoader.Source.ModDefinition;

public class ModContainer : IDisposable
{
    public IFileProxy FileProxy { get; }

    public Metadata Metadata { get; }

    public AssetMod AssetMod { get; internal set; }

    public CodeMod CodeMod { get; internal set; }

    private IAssemblyResolver _assemblyResolver;

    private readonly List<IntPtr> _nativeLibraryHandles = new();

    public ModContainer(IFileProxy fileProxy, Metadata metadata)
    {
        FileProxy = fileProxy;
        Metadata = metadata;
    }

    public void Initialize(Game game)
    {
        if (CodeMod != null)
        {
            _assemblyResolver = new ModInternalAssemblyResolver(this);
            AssemblyResolverRegistry.Register(_assemblyResolver);
            CodeMod?.Initialize(game, Metadata.Entrypoint);
            AppDomain.CurrentDomain.ProcessExit += UnloadNativeLibraries;
            LoadNativeLibraries();
        }
    }

    public void InjectComponents()
    {
        foreach (var component in CodeMod?.Components ?? new List<GameComponent>())
        {
            ServiceHelper.AddComponent(component);
        }
    }

    public IEnumerable<Asset> GetAssets()
    {
        return AssetMod?.Assets ?? new List<Asset>();
    }

    public IEnumerable<Asset> ReloadAssets()
    {
        FileProxy.Refresh();
        if (AssetMod == null)
        {
            if (!AssetMod.TryLoad(FileProxy, out var assetMod))
            {
                return new List<Asset>();
            }

            AssetMod = assetMod;
            return AssetMod.Assets;
        }

        return AssetMod.Reload(FileProxy);
    }

    public void Dispose()
    {
        foreach (var component in CodeMod?.Components ?? new List<GameComponent>())
        {
            ServiceHelper.RemoveComponent(component);
        }

        if (_assemblyResolver != null)
        {
            AssemblyResolverRegistry.Unregister(_assemblyResolver);
        }
    }

    private void UnloadNativeLibraries(object sender, EventArgs e)
    {
        lock (_nativeLibraryHandles)
        {
            foreach (var library in _nativeLibraryHandles)
            {
                FileProxy.UnloadLibrary(library);
            }
        }
    }

    private void LoadNativeLibraries()
    {
        if (Metadata.NativeDependencies == null || Metadata.NativeDependencies.Length == 0)
        {
            return;
        }

        var os = GetOsPlatform();
        var cpu = GetProcessArchitecture();
        var platformSpecific = Metadata.NativeDependencies
            .Where(library => os == library.Platform)
            .Where(library => cpu == library.Architecture)
            .ToArray();

        if (platformSpecific.Length == 0)
        {
            throw new PlatformNotSupportedException($"There're no native libraries found for " +
                                                    $"Platform=\"{os}\" Architecture=\"{cpu}\"");
        }

        foreach (var library in platformSpecific)
        {
            if (!FileProxy.FileExists(library.Path))
            {
                throw new DllNotFoundException($"There's no native library found at: {library.Path}");
            }

            var libraryHandle = FileProxy.LoadLibrary(library.Path);
            if (libraryHandle == IntPtr.Zero)
            {
                Logger.Log(Metadata.Name, $"Unable to load native library: {library.Path}");
                continue;
            }

            Logger.Log(Metadata.Name, $"Native library successfully loaded: {library.Path}");
            _nativeLibraryHandles.Add(libraryHandle);
        }
    }

    private static Metadata.OSPlatform GetOsPlatform()
    {
        return Environment.OSVersion.Platform switch
        {
            PlatformID.Win32NT => Metadata.OSPlatform.Windows,
            PlatformID.Unix => Metadata.OSPlatform.Linux,
            _ => Metadata.OSPlatform.OSX // Assuming that there's only three modding environments for the game
        };
    }

    private static Metadata.Architecture? GetProcessArchitecture()
    {
        // By default, FEZ is 32-bit application, so we gonna check its bitness against .NET runtime, not CPU
        var is64BitProcess = IntPtr.Size == 8;
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            if (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")?
                    .StartsWith("arm", StringComparison.OrdinalIgnoreCase) ?? false)
            {
                return is64BitProcess ? Metadata.Architecture.Arm64 : Metadata.Architecture.Arm;
            }

            return is64BitProcess ? Metadata.Architecture.X64 : Metadata.Architecture.X86;
        }

        try
        {
            var cpuInfo = File.ReadAllText("/proc/cpuinfo").ToLowerInvariant();
            if (cpuInfo.Contains("arm") || cpuInfo.Contains("aarch64"))
            {
                return is64BitProcess ? Metadata.Architecture.Arm64 : Metadata.Architecture.Arm;
            }

            return is64BitProcess ? Metadata.Architecture.X64 : Metadata.Architecture.X86;
        }
        catch
        {
            // ignored
        }

        return null;
    }
}