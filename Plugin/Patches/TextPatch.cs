using System.Runtime.CompilerServices;

namespace FezGame.Tools
{
    internal static class TextPatch
    {
        public static string GetRawOrDefault(string tag, string defaultText)
        {
            // returns original text if it's prefixed with @
            // allows easier injection of custom text into in-game UI structures like main menu

            if (tag.StartsWith("@")) return tag.Substring(1);
            return defaultText;
        }
    }

    public static class patch_StaticText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag) => TextPatch.GetRawOrDefault(tag, orig_GetString(tag));
    }

    public static class patch_GameText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag) => TextPatch.GetRawOrDefault(tag, orig_GetString(tag));

        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetStringRaw(string tag);
        public static string GetStringRaw(string tag) => TextPatch.GetRawOrDefault(tag, orig_GetStringRaw(tag));
    }

    public static class patch_CreditsText
    {
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string orig_GetString(string tag);
        public static string GetString(string tag) => TextPatch.GetRawOrDefault(tag, orig_GetString(tag));
    }
}
