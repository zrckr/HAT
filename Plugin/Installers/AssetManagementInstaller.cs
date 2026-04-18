using Common;
using FezEngine.Effects.Structures;
using FezEngine.Services;
using FezEngine.Tools;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using System.Reflection;
using HatModLoader.Source.Assets;

namespace HatModLoader.Installers
{
    internal class AssetManagementInstaller : IHatInstaller
    {
        private static Hook CMProviderCtorDetour;
        private static Hook SMInitializeLibraryDetour;

        private static FieldInfo CachedAssetsField;
        private static FieldInfo MusicCacheField;
        private static FieldInfo ReadLockField;
        private static FieldInfo CommonField;
        private static FieldInfo ReferencesField;

        private static readonly Dictionary<string, byte[]> OriginalAssets = new();
        private static readonly Dictionary<string, byte[]> OriginalMusic = new();

        public void Install()
        {
            CachedAssetsField = typeof(MemoryContentManager)
                .GetField("cachedAssets", BindingFlags.NonPublic | BindingFlags.Static);
            MusicCacheField = typeof(SoundManager)
                .GetField("MusicCache", BindingFlags.NonPublic | BindingFlags.Instance);
            ReadLockField = typeof(MemoryContentManager)
                .GetField("ReadLock", BindingFlags.NonPublic | BindingFlags.Static);
            CommonField = typeof(SharedContentManager)
                .GetField("Common", BindingFlags.NonPublic | BindingFlags.Static);
            ReferencesField = CommonField!.FieldType
                .GetField("references", BindingFlags.NonPublic | BindingFlags.Instance);
            
            CMProviderCtorDetour = new Hook(
                typeof(ContentManagerProvider).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null,
                    CallingConventions.HasThis, new Type[] { typeof(Game) }, null),
                new Action<Action<ContentManagerProvider, Game>, ContentManagerProvider, Game>((orig, self, game) =>
                {
                    orig(self, game);
                    InjectAssets(self);
                })
            );

            SMInitializeLibraryDetour = new Hook(
                typeof(SoundManager).GetMethod("InitializeLibrary"),
                new Action<Action<SoundManager>, SoundManager>((orig, self) =>
                {
                    orig(self);
                    InjectMusic(self);
                })
            );
        }

        public void Uninstall()
        {
            CMProviderCtorDetour.Dispose();
            SMInitializeLibraryDetour.Dispose();
        }

        private static void InjectAssets(ContentManagerProvider CMProvider)
        {
            var cachedAssets = (Dictionary<string, byte[]>)CachedAssetsField.GetValue(null);

            foreach (var asset in Hat.Instance.GetFullAssetList())
            {
                if (asset.IsMusicFile) continue;
                cachedAssets[asset.AssetPath] = asset.Data;
            }

            Logger.Log("HAT", "Asset injection completed!");
        }

        private static void InjectMusic(SoundManager soundManager)
        {
            var musicCache = (Dictionary<string, byte[]>)MusicCacheField.GetValue(soundManager);

            foreach (var asset in Hat.Instance.GetFullAssetList())
            {
                if (!asset.IsMusicFile) continue;
                musicCache[asset.AssetPath] = asset.Data;
            }

            Logger.Log("HAT", "Music injection completed!");
        }

        internal static void InjectAsset(Asset asset)
        {
            if (asset.IsMusicFile)
            {
                var soundManager = (SoundManager)ServiceHelper.Get<ISoundManager>();
                var musicCache = (Dictionary<string, byte[]>)MusicCacheField.GetValue(soundManager);
                if (!OriginalMusic.ContainsKey(asset.AssetPath) &&
                    musicCache.TryGetValue(asset.AssetPath, out var original))
                {
                    OriginalMusic[asset.AssetPath] = original;
                }

                musicCache[asset.AssetPath] = asset.Data;
            }
            else
            {
                var cachedAssets = (Dictionary<string, byte[]>)CachedAssetsField.GetValue(null);
                var readLock = ReadLockField.GetValue(null);
                lock (readLock)
                {
                    if (!OriginalAssets.ContainsKey(asset.AssetPath) &&
                        cachedAssets.TryGetValue(asset.AssetPath, out var original))
                    {
                        OriginalAssets[asset.AssetPath] = original;
                    }

                    cachedAssets[asset.AssetPath] = asset.Data;
                }
            }
        }

        internal static void RemoveAsset(Asset asset)
        {
            if (asset.IsMusicFile)
            {
                var soundManager = (SoundManager)ServiceHelper.Get<ISoundManager>();
                var musicCache = (Dictionary<string, byte[]>)MusicCacheField.GetValue(soundManager);
                if (OriginalMusic.TryGetValue(asset.AssetPath, out var original))
                {
                    musicCache[asset.AssetPath] = original;
                }
                else
                {
                    musicCache.Remove(asset.AssetPath);
                }
            }
            else
            {
                var cachedAssets = (Dictionary<string, byte[]>)CachedAssetsField.GetValue(null);
                var readLock = ReadLockField.GetValue(null);
                lock (readLock)
                {
                    if (OriginalAssets.TryGetValue(asset.AssetPath, out var original))
                    {
                        cachedAssets[asset.AssetPath] = original;
                    }
                    else
                    {
                        cachedAssets.Remove(asset.AssetPath);
                    }
                }
            }
        }

        internal static void EvictFromCommon(string assetPath)
        {
            var common = CommonField.GetValue(null);
            var references = (System.Collections.IDictionary)ReferencesField.GetValue(common);
            lock (common)
            {
                if (!references.Contains(assetPath))
                {
                    return;
                }

                var entry = references[assetPath];
                var assetField = entry.GetType().GetField("Asset", BindingFlags.Public | BindingFlags.Instance);

                var asset = assetField?.GetValue(entry);
                if (asset is Texture texture)
                {
                    texture.Unhook();
                }

                if (asset is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                references.Remove(assetPath);
            }
        }
    }
}