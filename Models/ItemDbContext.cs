using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Items.Models
{
    /// <summary>
    /// <see cref="DbContext"/> For flip tracking
    /// </summary>
    public class ItemDbContext : DbContext
    {
        public DbSet<Item> Items { get; set; }
        public DbSet<Modifiers> Modifiers { get; set; }

        /// <summary>
        /// Creates a new instance of <see cref="ItemDbContext"/>
        /// </summary>
        /// <param name="options"></param>
        public ItemDbContext(DbContextOptions<ItemDbContext> options)
        : base(options)
        {
        }

        /// <summary>
        /// Configures additional relations and indexes
        /// </summary>
        /// <param name="modelBuilder"></param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Item>(entity =>
            {
                entity.HasIndex(e => e.Tag).IsUnique();
            });
            modelBuilder.Entity<Modifiers>(entity =>
            {
                // occurance lookup
                entity.HasIndex(e => new { e.Slug, e.Value, e.FoundCount });
            });
        }
    }
}