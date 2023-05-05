﻿// <auto-generated />
using System;
using Coflnet.Sky.Items.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace SkyItems.Migrations
{
    [DbContext(typeof(ItemDbContext))]
    partial class ItemDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 64);

            modelBuilder.Entity("Coflnet.Sky.Items.Models.Description", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    b.Property<int?>("ItemId")
                        .HasColumnType("MEDIUMINT(9)");

                    b.Property<int>("Occurences")
                        .HasColumnType("int");

                    b.Property<string>("Text")
                        .HasColumnType("longtext");

                    MySqlPropertyBuilderExtensions.HasCharSet(b.Property<string>("Text"), "utf8");

                    b.HasKey("Id");

                    b.HasIndex("ItemId");

                    b.ToTable("Description");
                });

            modelBuilder.Entity("Coflnet.Sky.Items.Models.Item", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("MEDIUMINT(9)");

                    b.Property<int>("Category")
                        .HasColumnType("int");

                    b.Property<short>("Durability")
                        .HasColumnType("smallint");

                    b.Property<DateTime>("FirstSeen")
                        .HasColumnType("datetime(6)");

                    b.Property<int>("Flags")
                        .HasColumnType("int");

                    b.Property<string>("IconUrl")
                        .HasColumnType("longtext");

                    b.Property<string>("MinecraftType")
                        .HasMaxLength(25)
                        .HasColumnType("varchar(25)");

                    b.Property<string>("Name")
                        .HasMaxLength(80)
                        .HasColumnType("varchar(80)");

                    b.Property<float>("NpcBuyPrice")
                        .HasColumnType("float");

                    b.Property<float>("NpcSellPrice")
                        .HasColumnType("float");

                    b.Property<string>("Tag")
                        .HasMaxLength(44)
                        .HasColumnType("varchar(44)");

                    b.Property<int>("Tier")
                        .HasColumnType("int");

                    b.HasKey("Id");

                    b.HasIndex("Tag")
                        .IsUnique();

                    b.ToTable("Items");
                });

            modelBuilder.Entity("Coflnet.Sky.Items.Models.Modifiers", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    b.Property<int>("FoundCount")
                        .HasColumnType("int");

                    b.Property<int?>("ItemId")
                        .HasColumnType("MEDIUMINT(9)");

                    b.Property<string>("Slug")
                        .HasMaxLength(40)
                        .HasColumnType("varchar(40)");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<string>("Value")
                        .HasMaxLength(150)
                        .HasColumnType("varchar(150)");

                    b.HasKey("Id");

                    b.HasIndex("ItemId");

                    b.HasIndex("Slug", "Value");

                    b.ToTable("Modifiers");
                });

            modelBuilder.Entity("Coflnet.Sky.Items.Models.Description", b =>
                {
                    b.HasOne("Coflnet.Sky.Items.Models.Item", "Item")
                        .WithMany("Descriptions")
                        .HasForeignKey("ItemId");

                    b.Navigation("Item");
                });

            modelBuilder.Entity("Coflnet.Sky.Items.Models.Modifiers", b =>
                {
                    b.HasOne("Coflnet.Sky.Items.Models.Item", "Item")
                        .WithMany("Modifiers")
                        .HasForeignKey("ItemId");

                    b.Navigation("Item");
                });

            modelBuilder.Entity("Coflnet.Sky.Items.Models.Item", b =>
                {
                    b.Navigation("Descriptions");

                    b.Navigation("Modifiers");
                });
#pragma warning restore 612, 618
        }
    }
}
