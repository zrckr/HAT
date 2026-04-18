using MonoMod;

namespace FezGame.Components
{
    [MonoModPublic]
    [MonoModPatch("FezGame.Components.MenuBase")]
    class patch_MenuBase { }

    [MonoModPublic]
    [MonoModPatch("FezGame.Components.MainMenu")]
    class patch_MainMenu { }
}

namespace FezGame.Structure
{
    [MonoModPublic]
    [MonoModPatch("FezGame.Structure.MenuLevel")]
    class patch_MenuLevel { }

    [MonoModPublic]
    [MonoModPatch("FezGame.Structure.MenuItem`1")]
    class patch_MenuItem<T> { }

    [MonoModPublic]
    [MonoModPatch("FezGame.Structure.MenuItem")]
    interface patch_MenuItem { }
}
