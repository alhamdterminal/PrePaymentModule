using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace PrePaymentModule.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : base(options) // Pass the options to the base constructor
        {
        }

        public DbSet<Installment> Installments { get; set; }
        public DbSet<Prepayment> PrePayments { get; set; }
        public DbSet<History> History { get; set; }
        public DbSet<Voucher> Vouchers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Prepayment>()
            .HasKey(p => p.Id);
            modelBuilder.Entity<History>().ToTable("History");
        }
        public void SaveInstallments(List<Installment> installments)
        {
            Installments.AddRange(installments);
            SaveChanges();
        }
    }
}
