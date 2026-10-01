using Microsoft.EntityFrameworkCore;
using backend.Models;

namespace backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Form> Forms => Set<Form>();
    public DbSet<Field> Fields => Set<Field>();
    public DbSet<FormResponse> FormResponses => Set<FormResponse>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TPH: a single "Fields" table with an explicit discriminator column
        modelBuilder.Entity<Field>()
            .HasDiscriminator<string>("Discriminator")
            .HasValue<TextField>("TextField")
            .HasValue<ChoiceField>("ChoiceField")
            .HasValue<NumberField>("NumberField");

        // User -> Forms (one-to-many)
        modelBuilder.Entity<Form>()
            .HasOne(f => f.User)
            .WithMany(u => u.Forms)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // User -> Subscriptions (one-to-many)
        modelBuilder.Entity<Subscription>()
            .HasOne(s => s.User)
            .WithMany(u => u.Subscriptions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Form -> Fields (composition: cascade)
        modelBuilder.Entity<Field>()
            .HasOne(fi => fi.Form)
            .WithMany(fo => fo.Fields)
            .HasForeignKey(fi => fi.FormId)
            .OnDelete(DeleteBehavior.Cascade);

        // Form -> FormResponses (composition: cascade)
        modelBuilder.Entity<FormResponse>()
            .HasOne(fr => fr.Form)
            .WithMany(fo => fo.Responses)
            .HasForeignKey(fr => fr.FormId)
            .OnDelete(DeleteBehavior.Cascade);

        // FormResponse -> Answers (composition: cascade)
        modelBuilder.Entity<Answer>()
            .HasOne(a => a.FormResponse)
            .WithMany(fr => fr.Answers)
            .HasForeignKey(a => a.FormResponseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Answer -> Field (plain association: no cascade, we do not want to
        // delete a Field just because one of its Answers is deleted)
        modelBuilder.Entity<Answer>()
            .HasOne(a => a.Field)
            .WithMany()
            .HasForeignKey(a => a.FieldId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
