using System.Runtime.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Items.Models
{
    public class Description
    {
        public int Id { get; set; }
        public int Occurences { get; set; }
        public Item Item { get; set; }

        [MySqlCharSet("utf8")]
        [DataMember(Name = "text")]
        public string Text { get; set; }
    }
}