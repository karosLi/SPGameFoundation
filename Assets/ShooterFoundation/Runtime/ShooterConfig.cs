using UnityEngine;

namespace ShooterFoundation
{
    [CreateAssetMenu(menuName = "SPF/Shooter/Config", fileName = "ShooterConfig")]
    public sealed class ShooterConfig : ScriptableObject
    {
        public ShooterSettings Settings = ShooterSettings.Default;
        public static ShooterConfig CreateDefault() { var c = CreateInstance<ShooterConfig>(); c.hideFlags = HideFlags.DontSave; return c; }
    }
}
