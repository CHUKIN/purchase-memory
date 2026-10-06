using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PurchaseMemory.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgresSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "purchase_memory");

            migrationBuilder.CreateTable(
                name: "products",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_drafts",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    MerchantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PurchasedAt = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    ReceiptTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_drafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "receipt_captures",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageStorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ImageContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ImageSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ImageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    MerchantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PurchasedAt = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    ReceiptTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ReceiptTotalBoundingBox_X = table.Column<double>(type: "double precision", nullable: true),
                    ReceiptTotalBoundingBox_Y = table.Column<double>(type: "double precision", nullable: true),
                    ReceiptTotalBoundingBox_Width = table.Column<double>(type: "double precision", nullable: true),
                    ReceiptTotalBoundingBox_Height = table.Column<double>(type: "double precision", nullable: true),
                    ExtractorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExtractorModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SavedPurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UploadedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_captures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_aliases",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RawText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_aliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_aliases_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_items",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Product"),
                    DiscountedItemPosition = table.Column<int>(type: "integer", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseDraftId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_items_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_purchase_items_purchase_drafts_PurchaseDraftId",
                        column: x => x.PurchaseDraftId,
                        principalSchema: "purchase_memory",
                        principalTable: "purchase_drafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_capture_items",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    BoundingBox_X = table.Column<double>(type: "double precision", nullable: true),
                    BoundingBox_Y = table.Column<double>(type: "double precision", nullable: true),
                    BoundingBox_Width = table.Column<double>(type: "double precision", nullable: true),
                    BoundingBox_Height = table.Column<double>(type: "double precision", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Product"),
                    DiscountedItemPosition = table.Column<int>(type: "integer", nullable: true),
                    ReceiptCaptureId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_capture_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_capture_items_receipt_captures_ReceiptCaptureId",
                        column: x => x.ReceiptCaptureId,
                        principalSchema: "purchase_memory",
                        principalTable: "receipt_captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_image_parts",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReceiptCaptureId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_image_parts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_image_parts_receipt_captures_ReceiptCaptureId",
                        column: x => x.ReceiptCaptureId,
                        principalSchema: "purchase_memory",
                        principalTable: "receipt_captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_review_audit_entries",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    FieldName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptCaptureId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_review_audit_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_review_audit_entries_receipt_captures_ReceiptCaptur~",
                        column: x => x.ReceiptCaptureId,
                        principalSchema: "purchase_memory",
                        principalTable: "receipt_captures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_product_matches",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MerchantKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RawItemName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedItemName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RaisedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_product_matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_product_matches_purchase_items_PurchaseItemId",
                        column: x => x.PurchaseItemId,
                        principalSchema: "purchase_memory",
                        principalTable: "purchase_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_product_match_candidates",
                schema: "purchase_memory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    PendingProductMatchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_product_match_candidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_product_match_candidates_pending_product_matches_Pe~",
                        column: x => x.PendingProductMatchId,
                        principalSchema: "purchase_memory",
                        principalTable: "pending_product_matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pending_product_match_candidates_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "purchase_memory",
                        principalTable: "products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_match_candidates_PendingProductMatchId_Posi~",
                schema: "purchase_memory",
                table: "pending_product_match_candidates",
                columns: new[] { "PendingProductMatchId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_match_candidates_ProductId",
                schema: "purchase_memory",
                table: "pending_product_match_candidates",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_pending_product_matches_PurchaseItemId",
                schema: "purchase_memory",
                table: "pending_product_matches",
                column: "PurchaseItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_aliases_MerchantKey_NormalizedText",
                schema: "purchase_memory",
                table: "product_aliases",
                columns: new[] { "MerchantKey", "NormalizedText" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_aliases_ProductId",
                schema: "purchase_memory",
                table: "product_aliases",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_products_NormalizedName",
                schema: "purchase_memory",
                table: "products",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_drafts_CreatedAt",
                schema: "purchase_memory",
                table: "purchase_drafts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_drafts_SourceReceiptId",
                schema: "purchase_memory",
                table: "purchase_drafts",
                column: "SourceReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_items_ProductId",
                schema: "purchase_memory",
                table: "purchase_items",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_items_PurchaseDraftId_Position",
                schema: "purchase_memory",
                table: "purchase_items",
                columns: new[] { "PurchaseDraftId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_capture_items_ReceiptCaptureId_Position",
                schema: "purchase_memory",
                table: "receipt_capture_items",
                columns: new[] { "ReceiptCaptureId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_captures_CreatedAt",
                schema: "purchase_memory",
                table: "receipt_captures",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_captures_ImageSha256",
                schema: "purchase_memory",
                table: "receipt_captures",
                column: "ImageSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_captures_Status",
                schema: "purchase_memory",
                table: "receipt_captures",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_image_parts_ReceiptCaptureId_Position",
                schema: "purchase_memory",
                table: "receipt_image_parts",
                columns: new[] { "ReceiptCaptureId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipt_review_audit_entries_ReceiptCaptureId_OccurredAt",
                schema: "purchase_memory",
                table: "receipt_review_audit_entries",
                columns: new[] { "ReceiptCaptureId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_product_match_candidates",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "product_aliases",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "receipt_capture_items",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "receipt_image_parts",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "receipt_review_audit_entries",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "pending_product_matches",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "receipt_captures",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "purchase_items",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "products",
                schema: "purchase_memory");

            migrationBuilder.DropTable(
                name: "purchase_drafts",
                schema: "purchase_memory");
        }
    }
}
