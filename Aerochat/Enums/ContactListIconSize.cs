using Aerochat.Attributes;

namespace Aerochat.Enums
{
    /// <summary>Contact list row density (mirrors classic Messenger layout options).</summary>
    public enum ContactListIconSize
    {
        [Display("Status only (tiny)")]
        Tiny,

        [Display("Small picture")]
        Small,

        [Display("Medium picture")]
        Medium,

        [Display("Large picture")]
        Large,
    }
}
