using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations;

/// <inheritdoc />
public partial class AddWebPushNotifications : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "push_subscription",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                user_id = table.Column<int>(type: "INTEGER", nullable: false),
                endpoint = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                p256dh = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                auth = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                device_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_push_subscription", x => x.id);
                table.ForeignKey("FK_push_subscription_user_user_id", x => x.user_id, "user", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "push_notification_delivery",
            columns: table => new
            {
                id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                user_id = table.Column<int>(type: "INTEGER", nullable: false),
                push_subscription_id = table.Column<int>(type: "INTEGER", nullable: false),
                event_key = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                body = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                due_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                sent_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                next_attempt_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                last_error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_push_notification_delivery", x => x.id);
                table.ForeignKey("FK_push_notification_delivery_push_subscription_push_subscription_id", x => x.push_subscription_id,
                    "push_subscription", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_push_notification_delivery_user_user_id", x => x.user_id,
                    "user", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_push_subscription_endpoint", "push_subscription", "endpoint", unique: true);
        migrationBuilder.CreateIndex("IX_push_subscription_user_id_is_active", "push_subscription", new[] { "user_id", "is_active" });
        migrationBuilder.CreateIndex("IX_push_notification_delivery_push_subscription_id_event_key", "push_notification_delivery",
            new[] { "push_subscription_id", "event_key" }, unique: true);
        migrationBuilder.CreateIndex("IX_push_notification_delivery_status_due_at_utc_next_attempt_at_utc", "push_notification_delivery",
            new[] { "status", "due_at_utc", "next_attempt_at_utc" });
        migrationBuilder.CreateIndex("IX_push_notification_delivery_user_id", "push_notification_delivery", "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("push_notification_delivery");
        migrationBuilder.DropTable("push_subscription");
    }
}
