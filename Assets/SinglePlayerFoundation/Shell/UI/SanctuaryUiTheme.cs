using UnityEngine;

namespace SPF.Shell.UI
{
    /// <summary>Shared presentation palette. No textures, font copies or per-frame theme work.</summary>
    public static class SanctuaryUiTheme
    {
        public static readonly Color Ink = new Color(.055f, .115f, .125f, .96f);
        public static readonly Color Surface = new Color(.085f, .165f, .175f, .96f);
        public static readonly Color Ivory = new Color(.94f, .91f, .81f, 1f);
        public static readonly Color Muted = new Color(.63f, .72f, .69f, 1f);
        public static readonly Color Bronze = new Color(.67f, .54f, .33f, 1f);
        public static readonly Color Spirit = new Color(.35f, .78f, .72f, 1f);
        public static readonly Color Coral = new Color(.89f, .40f, .32f, 1f);
        public static readonly Color Disabled = new Color(.39f, .47f, .45f, .65f);

        /// <summary>Retain each caller's semantic hue as a restrained enamel tint.</summary>
        public static Color ButtonSurface(Color tint) => new Color(
            .055f + tint.r * .20f, .105f + tint.g * .25f, .11f + tint.b * .20f, tint.a);
    }
}
