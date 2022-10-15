namespace Coflnet.Sky.Items.Models
{
    /// <summary>
    /// Used in places where only limited information about an item is needed
    /// </summary>
    public class ItemPreview
    {
        /// <summary>
        /// Item tag
        /// </summary>
        public string Tag { get; set; }
        /// <summary>
        /// Display item name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Creates a new instance of <see cref="ItemPreview"/>
        /// </summary>
        public ItemPreview(string tag, string name)
        {
            Tag = tag;
            Name = name;
        }

        /// <summary>
        /// Creates a new instance of <see cref="ItemPreview"/>
        /// </summary>
        public ItemPreview()
        {
        }
    }
}