
using System;

namespace Coflnet.Sky.Items.Models
{
    [Flags]
    public enum ItemFlags
    {
        NONE,
        BAZAAR,
        TRADEABLE,
        AUCTION = 4,
        CRAFT = 8,
        /// <summary>
        /// Item has the minecraft effect glowing
        /// </summary>
        GLOWING = 16,
        MUSEUM = 32,
        FIRESALE = 64,
    }
}