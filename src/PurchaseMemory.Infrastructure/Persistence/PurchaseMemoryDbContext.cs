using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PurchaseMemory.Domain.Analytics;
using PurchaseMemory.Domain.Impressions;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Infrastructure.Persistence;

public sealed class PurchaseMemoryDbContext(DbContextOptions<PurchaseMemoryDbContext> options)
    : DbContext(options)
{
    public DbSet<PurchaseDraft> PurchaseDrafts => Set<PurchaseDraft>();

    public DbSet<ReceiptCapture> ReceiptCaptures => Set<ReceiptCapture>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductAlias> ProductAliases => Set<ProductAlias>();

    public DbSet<PendingProductMatch> PendingProductMatches => Set<PendingProductMatch>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<ProductAuditEntry> ProductAuditEntries => Set<ProductAuditEntry>();

    public DbSet<ProductTag> ProductTags => Set<ProductTag>();

    public DbSet<ProductTagLink> ProductTagLinks => Set<ProductTagLink>();

    public DbSet<ProductImpression> ProductImpressions => Set<ProductImpression>();

    public DbSet<AnalyticsQuestionEntry> AnalyticsQuestions => Set<AnalyticsQuestionEntry>();

    /// <summary>
    /// Supabase publishes the <c>public</c> schema through its Data API to
    /// anyone holding the (public) publishable key. The app's tables live in a
    /// schema of their own that the Data API does not expose, so the only way
    /// to the data is this API with its approved-user check.
    /// </summary>
    public const string PostgresSchema = "purchase_memory";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsNpgsql())
        {
            modelBuilder.HasDefaultSchema(PostgresSchema);
        }

        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, long>(
            value => value.ToUnixTimeMilliseconds(),
            value => DateTimeOffset.FromUnixTimeMilliseconds(value));

        var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : null,
            value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);

        var draft = modelBuilder.Entity<PurchaseDraft>();
        draft.ToTable("purchase_drafts");
        draft.HasKey(purchase => purchase.Id);
        draft.Property(purchase => purchase.Id).ValueGeneratedNever();
        draft.Property(purchase => purchase.SourceReceiptId);
        draft.Property(purchase => purchase.MerchantName).HasMaxLength(200);
        draft.Property(purchase => purchase.Currency).HasMaxLength(3).IsFixedLength();
        draft.Property(purchase => purchase.ReceiptTotal).HasPrecision(18, 2);
        draft.Property(purchase => purchase.PurchasedAt).HasConversion(nullableDateTimeOffsetConverter);
        draft.Property(purchase => purchase.CreatedAt).HasConversion(dateTimeOffsetConverter);
        draft.Property(purchase => purchase.UpdatedAt).HasConversion(dateTimeOffsetConverter);
        draft.HasIndex(purchase => purchase.CreatedAt);
        draft.HasIndex(purchase => purchase.SourceReceiptId).IsUnique();

        draft
            .HasMany(purchase => purchase.Items)
            .WithOne()
            .HasForeignKey("PurchaseDraftId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        draft.Navigation(purchase => purchase.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        var item = modelBuilder.Entity<PurchaseItem>();
        item.ToTable("purchase_items");
        item.HasKey(purchaseItem => purchaseItem.Id);
        item.Property(purchaseItem => purchaseItem.Id).ValueGeneratedNever();
        item.Property(purchaseItem => purchaseItem.Name).HasMaxLength(500).IsRequired();
        item.Property(purchaseItem => purchaseItem.Unit).HasMaxLength(30);
        item.Property(purchaseItem => purchaseItem.Quantity).HasPrecision(18, 3);
        item.Property(purchaseItem => purchaseItem.UnitPrice).HasPrecision(18, 2);
        item.Property(purchaseItem => purchaseItem.LineTotal).HasPrecision(18, 2);
        item.Property(purchaseItem => purchaseItem.ProductId);
        ConfigureLineKind(item.Property(purchaseItem => purchaseItem.Kind));
        item.Property(purchaseItem => purchaseItem.DiscountedItemPosition);
        item.Ignore(purchaseItem => purchaseItem.IsDiscount);
        item.HasIndex("PurchaseDraftId", nameof(PurchaseItem.Position)).IsUnique();
        item.HasIndex(purchaseItem => purchaseItem.ProductId);

        // Deleting a product must not silently delete purchase history, so the
        // link is cleared instead. The catalog has no delete path yet either way.
        item
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(purchaseItem => purchaseItem.ProductId)
            .OnDelete(DeleteBehavior.SetNull);

        var product = modelBuilder.Entity<Product>();
        product.ToTable("products");
        product.HasKey(entity => entity.Id);
        product.Property(entity => entity.Id).ValueGeneratedNever();
        product.Property(entity => entity.CanonicalName).HasMaxLength(Product.MaximumNameLength).IsRequired();
        product.Property(entity => entity.NormalizedName).HasMaxLength(Product.MaximumNameLength).IsRequired();
        product.Property(entity => entity.CreatedAt).HasConversion(dateTimeOffsetConverter);
        product.Property(entity => entity.UpdatedAt).HasConversion(dateTimeOffsetConverter);
        product.HasIndex(entity => entity.NormalizedName);
        product.Property(entity => entity.CategorySource).HasConversion<string>().HasMaxLength(20);
        product.Property(entity => entity.Brand).HasMaxLength(Product.MaximumBrandLength);
        product.Property(entity => entity.NormalizedBrand).HasMaxLength(Product.MaximumBrandLength);
        product.Property(entity => entity.BrandSource).HasConversion<string>().HasMaxLength(20);
        product.HasIndex(entity => entity.CategoryId);
        product.HasIndex(entity => entity.NormalizedBrand);

        // The service moves products out of a category before deleting it; the
        // SetNull rule is only the database's last line of defence.
        product
            .HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(entity => entity.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        var category = modelBuilder.Entity<ProductCategory>();
        category.ToTable("product_categories");
        category.HasKey(entity => entity.Id);
        category.Property(entity => entity.Id).ValueGeneratedNever();
        category.Property(entity => entity.Name).HasMaxLength(ProductCategory.MaximumNameLength).IsRequired();
        category.Property(entity => entity.SystemKey).HasMaxLength(ProductCategory.MaximumSystemKeyLength);
        category.Property(entity => entity.CreatedAt).HasConversion(dateTimeOffsetConverter);
        category.Property(entity => entity.UpdatedAt).HasConversion(dateTimeOffsetConverter);
        category.HasIndex(entity => entity.ParentId);
        category
            .HasOne<ProductCategory>()
            .WithMany()
            .HasForeignKey(entity => entity.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
        category.HasData(ProductCategorySeed.Categories);

        var productAudit = modelBuilder.Entity<ProductAuditEntry>();
        productAudit.ToTable("product_audit_entries");
        productAudit.HasKey(entity => entity.Id);
        productAudit.Property(entity => entity.Id).ValueGeneratedNever();
        productAudit.Property(entity => entity.Actor).HasMaxLength(200).IsRequired();
        productAudit.Property(entity => entity.ActorEmail).HasMaxLength(320);
        productAudit.Property(entity => entity.FieldName).HasMaxLength(50).IsRequired();
        productAudit.Property(entity => entity.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        productAudit.Property(entity => entity.OldValue).HasMaxLength(ProductAuditEntry.MaximumValueLength);
        productAudit.Property(entity => entity.NewValue).HasMaxLength(ProductAuditEntry.MaximumValueLength);
        productAudit.Property(entity => entity.OccurredAt).HasConversion(dateTimeOffsetConverter);
        productAudit.HasIndex(entity => new { entity.ProductId, entity.OccurredAt });
        productAudit
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        var tag = modelBuilder.Entity<ProductTag>();
        tag.ToTable("product_tags");
        tag.HasKey(entity => entity.Id);
        tag.Property(entity => entity.Id).ValueGeneratedNever();
        tag.Property(entity => entity.Name).HasMaxLength(ProductTag.MaximumNameLength).IsRequired();
        tag.Property(entity => entity.NormalizedName).HasMaxLength(ProductTag.MaximumNormalizedNameLength).IsRequired();
        tag.Property(entity => entity.CreatedAt).HasConversion(dateTimeOffsetConverter);
        tag.Property(entity => entity.UpdatedAt).HasConversion(dateTimeOffsetConverter);
        // The service refuses duplicates first; the index settles two
        // simultaneous requests for the same name.
        tag.HasIndex(entity => entity.NormalizedName).IsUnique();

        var tagLink = modelBuilder.Entity<ProductTagLink>();
        tagLink.ToTable("product_tag_links");
        tagLink.HasKey(entity => new { entity.ProductId, entity.TagId });
        tagLink.Property(entity => entity.CreatedAt).HasConversion(dateTimeOffsetConverter);
        tagLink.HasIndex(entity => entity.TagId);
        tagLink
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
        tagLink
            .HasOne<ProductTag>()
            .WithMany()
            .HasForeignKey(entity => entity.TagId)
            .OnDelete(DeleteBehavior.Cascade);

        var impression = modelBuilder.Entity<ProductImpression>();
        impression.ToTable("product_impressions");
        impression.HasKey(entity => entity.Id);
        impression.Property(entity => entity.Id).ValueGeneratedNever();
        impression.Property(entity => entity.ItemName).HasMaxLength(ProductImpression.MaximumItemNameLength).IsRequired();
        impression.Property(entity => entity.AuthorUserId).HasMaxLength(200).IsRequired();
        impression.Property(entity => entity.AuthorEmail).HasMaxLength(320);
        impression.Property(entity => entity.Rating).HasConversion<string>().HasMaxLength(20).IsRequired();
        impression.Property(entity => entity.Note).HasMaxLength(ProductImpression.MaximumNoteLength);
        impression.Property(entity => entity.PhotoStorageKey).HasMaxLength(500);
        impression.Property(entity => entity.PhotoContentType).HasMaxLength(100);
        impression.Property(entity => entity.CreatedAt).HasConversion(dateTimeOffsetConverter);
        impression.Property(entity => entity.DeletedAt).HasConversion(nullableDateTimeOffsetConverter);
        impression.Ignore(entity => entity.IsDeleted);
        impression.HasIndex(entity => entity.PurchaseItemId);
        impression.HasIndex(entity => entity.ProductId);
        impression.HasIndex(entity => new { entity.PurchaseId, entity.CreatedAt });
        impression.HasIndex(entity => new { entity.AuthorUserId, entity.CreatedAt });

        // Purchases have no delete path; if one ever appears, its impressions go with it.
        impression
            .HasOne<PurchaseDraft>()
            .WithMany()
            .HasForeignKey(entity => entity.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);

        // A corrected purchase replaces its line rows. The repository moves
        // impressions to the replacement line first; SetNull only keeps a
        // missed one alive (still attached to the purchase) instead of deleting it.
        impression
            .HasOne<PurchaseItem>()
            .WithMany()
            .HasForeignKey(entity => entity.PurchaseItemId)
            .OnDelete(DeleteBehavior.SetNull);

        impression
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.SetNull);

        // Plan, period and answer are JSON text rather than jsonb: the same
        // column type on SQLite and Postgres, and nothing queries inside them.
        var question = modelBuilder.Entity<AnalyticsQuestionEntry>();
        question.ToTable("analytics_questions");
        question.HasKey(entity => entity.Id);
        question.Property(entity => entity.Id).ValueGeneratedNever();
        question.Property(entity => entity.Question).HasMaxLength(AnalyticsQuestionEntry.MaximumQuestionLength).IsRequired();
        question.Property(entity => entity.NormalizedQuestion).HasMaxLength(AnalyticsQuestionEntry.MaximumQuestionLength).IsRequired();
        question.Property(entity => entity.PlanJson).IsRequired();
        question.Property(entity => entity.AnswerJson).IsRequired();
        question.Property(entity => entity.Conclusion).HasMaxLength(AnalyticsQuestionEntry.MaximumConclusionLength).IsRequired();
        question.Property(entity => entity.AskedByUserId).HasMaxLength(AnalyticsQuestionEntry.MaximumUserIdLength).IsRequired();
        question.Property(entity => entity.AskedByEmail).HasMaxLength(AnalyticsQuestionEntry.MaximumEmailLength);
        question.Property(entity => entity.CreatedAt).HasConversion(dateTimeOffsetConverter);
        question.Property(entity => entity.AnsweredAt).HasConversion(dateTimeOffsetConverter);
        // Not unique: follow-ups with the same words but different plans are separate entries.
        question.HasIndex(entity => new { entity.NormalizedQuestion, entity.IsFollowUp });
        question.HasIndex(entity => entity.AnsweredAt);

        var alias = modelBuilder.Entity<ProductAlias>();
        alias.ToTable("product_aliases");
        alias.HasKey(entity => entity.Id);
        alias.Property(entity => entity.Id).ValueGeneratedNever();
        alias.Property(entity => entity.MerchantKey).HasMaxLength(200).IsRequired();
        alias.Property(entity => entity.NormalizedText).HasMaxLength(ProductAlias.MaximumTextLength).IsRequired();
        alias.Property(entity => entity.RawText).HasMaxLength(ProductAlias.MaximumTextLength).IsRequired();
        alias.Property(entity => entity.Source).HasConversion<string>().HasMaxLength(20).IsRequired();
        alias.Property(entity => entity.CreatedAt).HasConversion(dateTimeOffsetConverter);
        alias.HasIndex(entity => new { entity.MerchantKey, entity.NormalizedText }).IsUnique();
        alias
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        var pendingMatch = modelBuilder.Entity<PendingProductMatch>();
        pendingMatch.ToTable("pending_product_matches");
        pendingMatch.HasKey(entity => entity.Id);
        pendingMatch.Property(entity => entity.Id).ValueGeneratedNever();
        pendingMatch.Property(entity => entity.MerchantKey).HasMaxLength(200).IsRequired();
        pendingMatch.Property(entity => entity.RawItemName).HasMaxLength(ProductAlias.MaximumTextLength).IsRequired();
        pendingMatch
            .Property(entity => entity.NormalizedItemName)
            .HasMaxLength(ProductAlias.MaximumTextLength)
            .IsRequired();
        pendingMatch.Property(entity => entity.RaisedAt).HasConversion(dateTimeOffsetConverter);
        pendingMatch.HasIndex(entity => entity.PurchaseItemId).IsUnique();
        pendingMatch
            .HasOne<PurchaseItem>()
            .WithMany()
            .HasForeignKey(entity => entity.PurchaseItemId)
            .OnDelete(DeleteBehavior.Cascade);

        pendingMatch
            .HasMany(entity => entity.Candidates)
            .WithOne()
            .HasForeignKey("PendingProductMatchId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        pendingMatch.Navigation(entity => entity.Candidates).UsePropertyAccessMode(PropertyAccessMode.Field);

        var pendingCandidate = modelBuilder.Entity<PendingProductMatchCandidate>();
        pendingCandidate.ToTable("pending_product_match_candidates");
        pendingCandidate.HasKey(entity => entity.Id);
        pendingCandidate.Property(entity => entity.Id).ValueGeneratedNever();
        pendingCandidate.HasIndex("PendingProductMatchId", nameof(PendingProductMatchCandidate.Position)).IsUnique();
        pendingCandidate
            .HasOne<Product>()
            .WithMany()
            .HasForeignKey(entity => entity.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        var receipt = modelBuilder.Entity<ReceiptCapture>();
        receipt.ToTable("receipt_captures");
        receipt.HasKey(capture => capture.Id);
        receipt.Property(capture => capture.Id).ValueGeneratedNever();
        receipt.Property(capture => capture.ImageStorageKey).HasMaxLength(500).IsRequired();
        receipt.Property(capture => capture.ImageContentType).HasMaxLength(100).IsRequired();
        receipt.Property(capture => capture.ImageSha256).HasMaxLength(64).IsRequired();
        receipt.Property(capture => capture.MerchantName).HasMaxLength(200);
        receipt.Property(capture => capture.PurchasedAt).HasConversion(nullableDateTimeOffsetConverter);
        receipt.Property(capture => capture.Currency).HasMaxLength(3).IsFixedLength();
        receipt.Property(capture => capture.ReceiptTotal).HasPrecision(18, 2);
        receipt.OwnsOne(capture => capture.ReceiptTotalBoundingBox);
        receipt.Property(capture => capture.ExtractorName).HasMaxLength(100);
        receipt.Property(capture => capture.ExtractorModel).HasMaxLength(200);
        receipt.Property(capture => capture.ErrorMessage).HasMaxLength(1000);
        receipt.Property(capture => capture.CreatedAt).HasConversion(dateTimeOffsetConverter);
        receipt.Property(capture => capture.UpdatedAt).HasConversion(dateTimeOffsetConverter);
        receipt.Property(capture => capture.UploadedByUserId).HasMaxLength(200);
        receipt.Property(capture => capture.UploadedByEmail).HasMaxLength(320);
        receipt.HasIndex(capture => capture.ImageSha256).IsUnique();
        receipt.HasIndex(capture => capture.Status);
        receipt.HasIndex(capture => capture.CreatedAt);

        receipt
            .HasMany(capture => capture.Items)
            .WithOne()
            .HasForeignKey("ReceiptCaptureId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        receipt.Navigation(capture => capture.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        receipt
            .HasMany(capture => capture.ImageParts)
            .WithOne()
            .HasForeignKey("ReceiptCaptureId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        receipt.Navigation(capture => capture.ImageParts).UsePropertyAccessMode(PropertyAccessMode.Field);

        receipt
            .HasMany(capture => capture.ReviewAuditEntries)
            .WithOne()
            .HasForeignKey("ReceiptCaptureId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        receipt.Navigation(capture => capture.ReviewAuditEntries).UsePropertyAccessMode(PropertyAccessMode.Field);

        var receiptItem = modelBuilder.Entity<ReceiptCaptureItem>();
        receiptItem.ToTable("receipt_capture_items");
        receiptItem.HasKey(captureItem => captureItem.Id);
        receiptItem.Property(captureItem => captureItem.Id).ValueGeneratedNever();
        receiptItem.Property(captureItem => captureItem.Name).HasMaxLength(500).IsRequired();
        receiptItem.Property(captureItem => captureItem.Unit).HasMaxLength(30);
        receiptItem.Property(captureItem => captureItem.Quantity).HasPrecision(18, 3);
        receiptItem.Property(captureItem => captureItem.UnitPrice).HasPrecision(18, 2);
        receiptItem.Property(captureItem => captureItem.LineTotal).HasPrecision(18, 2);
        receiptItem.OwnsOne(captureItem => captureItem.BoundingBox);
        ConfigureLineKind(receiptItem.Property(captureItem => captureItem.Kind));
        receiptItem.Property(captureItem => captureItem.DiscountedItemPosition);
        receiptItem.HasIndex("ReceiptCaptureId", nameof(ReceiptCaptureItem.Position)).IsUnique();

        var imagePart = modelBuilder.Entity<ReceiptImagePart>();
        imagePart.ToTable("receipt_image_parts");
        imagePart.HasKey(part => part.Id);
        imagePart.Property(part => part.Id).ValueGeneratedNever();
        imagePart.Property(part => part.StorageKey).HasMaxLength(500).IsRequired();
        imagePart.Property(part => part.ContentType).HasMaxLength(100).IsRequired();
        imagePart.Property(part => part.Sha256).HasMaxLength(64).IsRequired();
        imagePart.HasIndex("ReceiptCaptureId", nameof(ReceiptImagePart.Position)).IsUnique();

        var auditEntry = modelBuilder.Entity<ReceiptReviewAuditEntry>();
        auditEntry.ToTable("receipt_review_audit_entries");
        auditEntry.HasKey(entry => entry.Id);
        auditEntry.Property(entry => entry.Id).ValueGeneratedNever();
        auditEntry.Property(entry => entry.Actor).HasMaxLength(200).IsRequired();
        auditEntry.Property(entry => entry.ActorEmail).HasMaxLength(320);
        auditEntry.Property(entry => entry.FieldName).HasMaxLength(200).IsRequired();
        auditEntry.Property(entry => entry.ChangeType).HasConversion<string>().HasMaxLength(20).IsRequired();
        auditEntry.Property(entry => entry.OldValue).HasMaxLength(2000);
        auditEntry.Property(entry => entry.NewValue).HasMaxLength(2000);
        auditEntry.Property(entry => entry.OccurredAt).HasConversion(dateTimeOffsetConverter);
        auditEntry.HasIndex("ReceiptCaptureId", nameof(ReceiptReviewAuditEntry.OccurredAt));

        RoundDecimalsToDeclaredScale(modelBuilder);
    }

    /// <summary>
    /// Postgres numeric(18,2) rounds on its own (half away from zero) while
    /// SQLite keeps decimal text exactly. Rounding here, to each column's
    /// declared scale, makes both providers store and return the same value.
    /// The converter keeps the CLR/provider type, so no migration changes.
    /// </summary>
    private static void RoundDecimalsToDeclaredScale(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            if (property.GetScale() is not { } scale)
            {
                continue;
            }

            if (property.ClrType == typeof(decimal))
            {
                property.SetValueConverter(new ValueConverter<decimal, decimal>(
                    value => Math.Round(value, scale, MidpointRounding.AwayFromZero),
                    value => value));
            }
            else if (property.ClrType == typeof(decimal?))
            {
                property.SetValueConverter(new ValueConverter<decimal?, decimal?>(
                    value => value.HasValue ? Math.Round(value.Value, scale, MidpointRounding.AwayFromZero) : null,
                    value => value));
            }
        }
    }

    /// <summary>
    /// Stored as text like the other enums here; existing rows default to
    /// Product because discounts were never saved before this column existed.
    /// </summary>
    private static void ConfigureLineKind(
        PropertyBuilder<ReceiptLineKind> property)
    {
        property
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ReceiptLineKind.Product)
            .HasSentinel((ReceiptLineKind)(-1))
            .IsRequired();
    }
}
