using Aerochat.Controls;
using Aerochat.Enums;

namespace Aerochat.ViewModels
{
    /// <summary>
    /// List row heights for contact rows. Must be at least the <see cref="ProfilePictureFrame"/> tile
    /// size for the mapped <see cref="ProfileFrameSize"/> plus a little vertical slack (margins / rounding).
    /// </summary>
    public static class ContactListRowLayout
    {
        // Tile heights from ProfilePictureFrame.SizeToPixels — keep in sync when frame assets change.
        private const double FrameTiny = 16;
        private const double FrameSmall = 45;
        private const double FrameMedium = 59;
        private const double FrameLarge = 79;

        // Extra vertical space when name + activity are stacked (small/medium/large picture layouts).
        private const double StackedStatusLineSlack = 0;

        public static double RowHeight(ContactListIconSize size) => size switch
        {
            ContactListIconSize.Tiny => FrameTiny + 2,
            ContactListIconSize.Small => FrameSmall + StackedStatusLineSlack,
            ContactListIconSize.Medium => FrameMedium + StackedStatusLineSlack,
            ContactListIconSize.Large => FrameLarge + StackedStatusLineSlack,
            _ => FrameTiny + 2,
        };
    }
}
