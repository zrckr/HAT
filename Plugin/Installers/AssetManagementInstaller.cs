using Common;
using FezEngine.Effects.Structures;
using FezEngine.Services;
using FezEngine.Structure;
using FezEngine.Tools;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;
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
        private static FieldInfo AssetField;

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
            var referencedAssetType = CommonField.FieldType
                .GetNestedType("ReferencedAsset", BindingFlags.NonPublic);
            AssetField = referencedAssetType!
                .GetField("Asset", BindingFlags.Public | BindingFlags.Instance);

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
                var key = FindReferencesKey(references, assetPath);
                if (key == null)
                {
                    return;
                }

                var asset = AssetField.GetValue(references[key]);
                if (asset is Texture texture)
                {
                    texture.Unhook();
                }

                if (asset is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                references.Remove(key);
            }
        }

        internal static void PatchInCommon(string assetPath)
        {
            var common = CommonField.GetValue(null);
            var references = (System.Collections.IDictionary)ReferencesField.GetValue(common);
            lock (common)
            {
                var key = FindReferencesKey(references, assetPath);
                if (key == null)
                {
                    return;
                }

                var entry = references[key];
                var existing = AssetField.GetValue(entry);

                var mcm = new MemoryContentManager(ServiceHelper.Game.Services,
                    ServiceHelper.Game.Content.RootDirectory);

                switch (existing)
                {
                    case AnimatedTexture existingAt:
                    {
                        var tempAt = mcm.Load<AnimatedTexture>(assetPath);
                        var inPlace = PatchTexture2D(existingAt.Texture, tempAt.Texture, out var replacement);
                        if (!inPlace)
                        {
                            existingAt.Texture.Unhook();
                            existingAt.Texture.Dispose();
                            existingAt.Texture = replacement;
                        }
                        else
                        {
                            tempAt.Texture.Dispose();
                        }

                        existingAt.Offsets = tempAt.Offsets;
                        existingAt.FrameWidth = tempAt.FrameWidth;
                        existingAt.FrameHeight = tempAt.FrameHeight;
                        existingAt.Timing = tempAt.Timing;
                        existingAt.PotOffset = tempAt.PotOffset;
                        return;
                    }

                    case Texture2D existingTex:
                    {
                        var tempTex = mcm.Load<Texture2D>(assetPath);
                        var inPlace = PatchTexture2D(existingTex, tempTex, out var replacement);
                        if (!inPlace)
                        {
                            existingTex.Unhook();
                            existingTex.Dispose();
                            AssetField.SetValue(entry, replacement);
                        }
                        else
                        {
                            tempTex.Dispose();
                        }

                        return;
                    }

                    case SoundEffect existingSfx:
                    {
                        var tempSfx = mcm.Load<SoundEffect>(assetPath);
                        existingSfx.Dispose();
                        AssetField.SetValue(entry, tempSfx);
                        return;
                    }

                    default:
                    {
                        // Fall back to evict (already holding lock(common))
                        if (existing is Texture texture)
                        {
                            texture.Unhook();
                        }

                        if (existing is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }

                        references.Remove(key);
                        return;
                    }
                }
            }
        }

        private static string FindReferencesKey(System.Collections.IDictionary references, string assetPath)
        {
            foreach (System.Collections.DictionaryEntry kv in references)
            {
                if (string.Equals((string)kv.Key, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return (string)kv.Key;
                }
            }

            return null;
        }

        private static bool PatchTexture2D(Texture2D existing, Texture2D temp, out Texture2D replacement)
        {
            replacement = temp;
            if (existing.Width != temp.Width || existing.Height != temp.Height ||
                existing.Format != temp.Format || existing.LevelCount != temp.LevelCount)
            {
                return false;
            }

            const int bytesPerPixel = 4; // All FEZ textures are SurfaceFormat.Color
            for (var level = 0; level < existing.LevelCount; level++)
            {
                var w = Math.Max(existing.Width >> level, 1);
                var h = Math.Max(existing.Height >> level, 1);
                var size = w * h * bytesPerPixel;
                var buf = new byte[size];
                temp.GetData(level, null, buf, 0, size);
                existing.SetData(level, null, buf, 0, size);
            }

            return true;
        }
    }
}