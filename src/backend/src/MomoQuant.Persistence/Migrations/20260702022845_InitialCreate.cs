using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MomoQuant.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SettingKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SettingValue = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValueType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsSensitive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Exchanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BaseUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WebSocketUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exchanges", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PaperAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InitialBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Currency = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperAccounts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RiskProfiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskProfiles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Strategies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Strategies", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SystemHealthLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ServiceName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LatencyMs = table.Column<int>(type: "int", nullable: true),
                    CheckedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemHealthLogs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Symbols",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                    Symbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BaseAsset = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuoteAsset = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContractType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PricePrecision = table.Column<int>(type: "int", nullable: false),
                    QuantityPrecision = table.Column<int>(type: "int", nullable: false),
                    MinQty = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MinNotional = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TickSize = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    StepSize = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MakerFeeRate = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TakerFeeRate = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Symbols", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Symbols_Exchanges_ExchangeId",
                        column: x => x.ExchangeId,
                        principalTable: "Exchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RiskRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RiskProfileId = table.Column<long>(type: "bigint", nullable: false),
                    RuleKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RuleValue = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValueType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskRules_RiskProfiles_RiskProfileId",
                        column: x => x.RiskProfileId,
                        principalTable: "RiskProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FullName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PasswordHash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Candles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpenTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CloseTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    High = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Low = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    QuoteVolume = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TradeCount = table.Column<int>(type: "int", nullable: false),
                    IsClosed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Candles_Exchanges_ExchangeId",
                        column: x => x.ExchangeId,
                        principalTable: "Exchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Candles_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StrategyParameters",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StrategyId = table.Column<long>(type: "bigint", nullable: false),
                    ParameterKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ParameterValue = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValueType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SymbolId = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyParameters_Strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "Strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StrategyParameters_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TradingSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExchangeId = table.Column<long>(type: "bigint", nullable: false),
                    StartedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StoppedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    InitialBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FinalBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Notes = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradingSessions_Exchanges_ExchangeId",
                        column: x => x.ExchangeId,
                        principalTable: "Exchanges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradingSessions_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "IndicatorSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CandleId = table.Column<long>(type: "bigint", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Ema20 = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Ema50 = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Ema200 = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Vwap = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Rsi14 = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Atr14 = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    VolumeSma20 = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    SwingHigh = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    SwingLow = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    MarketStructure = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicatorSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndicatorSnapshots_Candles_CandleId",
                        column: x => x.CandleId,
                        principalTable: "Candles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IndicatorSnapshots_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<long>(type: "bigint", nullable: true),
                    OldValueJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewValueJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IpAddress = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserAgent = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BacktestRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HigherTimeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    InitialBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FinalBalance = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    StrategySetJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SettingsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BacktestRuns_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BacktestRuns_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PaperAccountSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PaperAccountId = table.Column<long>(type: "bigint", nullable: false),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Equity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    UnrealizedPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MarginUsed = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperAccountSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperAccountSnapshots_PaperAccounts_PaperAccountId",
                        column: x => x.PaperAccountId,
                        principalTable: "PaperAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperAccountSnapshots_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Quantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    AverageEntryPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MarkPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    UnrealizedPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Leverage = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MarginUsed = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Positions_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Positions_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReplaySessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CurrentReplayTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReplaySpeed = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplaySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplaySessions_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplaySessions_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StrategySignals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    StrategyId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CandleId = table.Column<long>(type: "bigint", nullable: true),
                    SignalType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Strength = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    ConfidenceContribution = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    SuggestedStopLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    SuggestedTakeProfit = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Reason = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RawDataJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategySignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategySignals_Candles_CandleId",
                        column: x => x.CandleId,
                        principalTable: "Candles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StrategySignals_Strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "Strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StrategySignals_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StrategySignals_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TradingSessionSymbols",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HigherTimeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingSessionSymbols", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradingSessionSymbols_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradingSessionSymbols_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BacktestResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BacktestRunId = table.Column<long>(type: "bigint", nullable: false),
                    TotalTrades = table.Column<int>(type: "int", nullable: false),
                    WinningTrades = table.Column<int>(type: "int", nullable: false),
                    LosingTrades = table.Column<int>(type: "int", nullable: false),
                    WinRate = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    GrossPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    NetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TotalFees = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    ProfitFactor = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Expectancy = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    AverageWin = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    AverageLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    LargestWin = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    LargestLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    SharpeRatio = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    SortinoRatio = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BacktestResults_BacktestRuns_BacktestRunId",
                        column: x => x.BacktestRunId,
                        principalTable: "BacktestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AiDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Timeframe = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CandleId = table.Column<long>(type: "bigint", nullable: true),
                    SignalId = table.Column<long>(type: "bigint", nullable: true),
                    MarketRegime = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfidenceScore = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    PreferredStrategyCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RiskAdjustment = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    TradeAllowed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Explanation = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RawRequestJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RawResponseJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiDecisions_Candles_CandleId",
                        column: x => x.CandleId,
                        principalTable: "Candles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiDecisions_StrategySignals_SignalId",
                        column: x => x.SignalId,
                        principalTable: "StrategySignals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiDecisions_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiDecisions_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MissedOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SignalId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    BestBid = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    BestAsk = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Reason = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissedOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MissedOrders_StrategySignals_SignalId",
                        column: x => x.SignalId,
                        principalTable: "StrategySignals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MissedOrders_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MissedOrders_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RiskDecisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SignalId = table.Column<long>(type: "bigint", nullable: true),
                    AiDecisionId = table.Column<long>(type: "bigint", nullable: true),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApprovedRiskPercent = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    PositionSize = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    StopLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    TakeProfit = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    RejectedRuleKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskDecisions_AiDecisions_AiDecisionId",
                        column: x => x.AiDecisionId,
                        principalTable: "AiDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskDecisions_StrategySignals_SignalId",
                        column: x => x.SignalId,
                        principalTable: "StrategySignals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskDecisions_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskDecisions_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OrderFills",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalFillId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FillPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FillQuantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FeeAsset = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LiquidityType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FilledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderFills", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    TradeId = table.Column<long>(type: "bigint", nullable: true),
                    ExternalOrderId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Side = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PositionSide = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Price = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsPostOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsReduceOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TimeInForce = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FilledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FailureReason = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TradingSessionId = table.Column<long>(type: "bigint", nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    StrategyId = table.Column<long>(type: "bigint", nullable: true),
                    SignalId = table.Column<long>(type: "bigint", nullable: true),
                    AiDecisionId = table.Column<long>(type: "bigint", nullable: true),
                    RiskDecisionId = table.Column<long>(type: "bigint", nullable: true),
                    Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntryOrderId = table.Column<long>(type: "bigint", nullable: true),
                    ExitOrderId = table.Column<long>(type: "bigint", nullable: true),
                    EntryPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    StopLoss = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    TakeProfit = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GrossPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    Fees = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    FundingFees = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    NetPnl = table.Column<decimal>(type: "decimal(28,12)", nullable: false),
                    RMultiple = table.Column<decimal>(type: "decimal(28,12)", nullable: true),
                    CloseReason = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trades_AiDecisions_AiDecisionId",
                        column: x => x.AiDecisionId,
                        principalTable: "AiDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Orders_EntryOrderId",
                        column: x => x.EntryOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Orders_ExitOrderId",
                        column: x => x.ExitOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_RiskDecisions_RiskDecisionId",
                        column: x => x.RiskDecisionId,
                        principalTable: "RiskDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "Strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_StrategySignals_SignalId",
                        column: x => x.SignalId,
                        principalTable: "StrategySignals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_Symbols_SymbolId",
                        column: x => x.SymbolId,
                        principalTable: "Symbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trades_TradingSessions_TradingSessionId",
                        column: x => x.TradingSessionId,
                        principalTable: "TradingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiDecisions_CandleId",
                table: "AiDecisions",
                column: "CandleId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDecisions_SignalId",
                table: "AiDecisions",
                column: "SignalId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDecisions_SymbolId",
                table: "AiDecisions",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDecisions_TradingSessionId",
                table: "AiDecisions",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_SettingKey",
                table: "AppSettings",
                column: "SettingKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TradingSessionId",
                table: "AuditLogs",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestResults_BacktestRunId",
                table: "BacktestResults",
                column: "BacktestRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BacktestRuns_SymbolId",
                table: "BacktestRuns",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestRuns_TradingSessionId",
                table: "BacktestRuns",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Candles_ExchangeId_SymbolId_Timeframe_OpenTimeUtc",
                table: "Candles",
                columns: new[] { "ExchangeId", "SymbolId", "Timeframe", "OpenTimeUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Candles_OpenTimeUtc",
                table: "Candles",
                column: "OpenTimeUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Candles_SymbolId_Timeframe_OpenTimeUtc",
                table: "Candles",
                columns: new[] { "SymbolId", "Timeframe", "OpenTimeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Exchanges_Code",
                table: "Exchanges",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndicatorSnapshots_CandleId",
                table: "IndicatorSnapshots",
                column: "CandleId");

            migrationBuilder.CreateIndex(
                name: "IX_IndicatorSnapshots_SymbolId_Timeframe_CandleId",
                table: "IndicatorSnapshots",
                columns: new[] { "SymbolId", "Timeframe", "CandleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MissedOrders_SignalId",
                table: "MissedOrders",
                column: "SignalId");

            migrationBuilder.CreateIndex(
                name: "IX_MissedOrders_SymbolId",
                table: "MissedOrders",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_MissedOrders_TradingSessionId",
                table: "MissedOrders",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderFills_OrderId",
                table: "OrderFills",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SymbolId",
                table: "Orders",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TradeId",
                table: "Orders",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TradingSessionId",
                table: "Orders",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperAccounts_Name",
                table: "PaperAccounts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperAccountSnapshots_PaperAccountId",
                table: "PaperAccountSnapshots",
                column: "PaperAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperAccountSnapshots_TradingSessionId",
                table: "PaperAccountSnapshots",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_SymbolId",
                table: "Positions",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_TradingSessionId",
                table: "Positions",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplaySessions_SymbolId",
                table: "ReplaySessions",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplaySessions_TradingSessionId",
                table: "ReplaySessions",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskDecisions_AiDecisionId",
                table: "RiskDecisions",
                column: "AiDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskDecisions_SignalId",
                table: "RiskDecisions",
                column: "SignalId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskDecisions_SymbolId",
                table: "RiskDecisions",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskDecisions_TradingSessionId",
                table: "RiskDecisions",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskProfiles_Name",
                table: "RiskProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskRules_RiskProfileId",
                table: "RiskRules",
                column: "RiskProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Strategies_Code",
                table: "Strategies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyParameters_StrategyId",
                table: "StrategyParameters",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyParameters_SymbolId",
                table: "StrategyParameters",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategySignals_CandleId",
                table: "StrategySignals",
                column: "CandleId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategySignals_StrategyId",
                table: "StrategySignals",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategySignals_SymbolId",
                table: "StrategySignals",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategySignals_TradingSessionId",
                table: "StrategySignals",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Symbols_ExchangeId_Symbol",
                table: "Symbols",
                columns: new[] { "ExchangeId", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemHealthLogs_CheckedAtUtc",
                table: "SystemHealthLogs",
                column: "CheckedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SystemHealthLogs_ServiceName_CheckedAtUtc",
                table: "SystemHealthLogs",
                columns: new[] { "ServiceName", "CheckedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_AiDecisionId",
                table: "Trades",
                column: "AiDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_EntryOrderId",
                table: "Trades",
                column: "EntryOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ExitOrderId",
                table: "Trades",
                column: "ExitOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_RiskDecisionId",
                table: "Trades",
                column: "RiskDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_SignalId",
                table: "Trades",
                column: "SignalId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_StrategyId",
                table: "Trades",
                column: "StrategyId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_SymbolId",
                table: "Trades",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_TradingSessionId",
                table: "Trades",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingSessions_ExchangeId",
                table: "TradingSessions",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingSessions_StartedByUserId",
                table: "TradingSessions",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingSessionSymbols_SymbolId",
                table: "TradingSessionSymbols",
                column: "SymbolId");

            migrationBuilder.CreateIndex(
                name: "IX_TradingSessionSymbols_TradingSessionId",
                table: "TradingSessionSymbols",
                column: "TradingSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderFills_Orders_OrderId",
                table: "OrderFills",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Trades_TradeId",
                table: "Orders",
                column: "TradeId",
                principalTable: "Trades",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiDecisions_Candles_CandleId",
                table: "AiDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_StrategySignals_Candles_CandleId",
                table: "StrategySignals");

            migrationBuilder.DropForeignKey(
                name: "FK_AiDecisions_StrategySignals_SignalId",
                table: "AiDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_RiskDecisions_StrategySignals_SignalId",
                table: "RiskDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Trades_StrategySignals_SignalId",
                table: "Trades");

            migrationBuilder.DropForeignKey(
                name: "FK_AiDecisions_Symbols_SymbolId",
                table: "AiDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Symbols_SymbolId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_RiskDecisions_Symbols_SymbolId",
                table: "RiskDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Trades_Symbols_SymbolId",
                table: "Trades");

            migrationBuilder.DropForeignKey(
                name: "FK_AiDecisions_TradingSessions_TradingSessionId",
                table: "AiDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_TradingSessions_TradingSessionId",
                table: "Orders");

            migrationBuilder.DropForeignKey(
                name: "FK_RiskDecisions_TradingSessions_TradingSessionId",
                table: "RiskDecisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Trades_TradingSessions_TradingSessionId",
                table: "Trades");

            migrationBuilder.DropForeignKey(
                name: "FK_Trades_Orders_EntryOrderId",
                table: "Trades");

            migrationBuilder.DropForeignKey(
                name: "FK_Trades_Orders_ExitOrderId",
                table: "Trades");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BacktestResults");

            migrationBuilder.DropTable(
                name: "IndicatorSnapshots");

            migrationBuilder.DropTable(
                name: "MissedOrders");

            migrationBuilder.DropTable(
                name: "OrderFills");

            migrationBuilder.DropTable(
                name: "PaperAccountSnapshots");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "ReplaySessions");

            migrationBuilder.DropTable(
                name: "RiskRules");

            migrationBuilder.DropTable(
                name: "StrategyParameters");

            migrationBuilder.DropTable(
                name: "SystemHealthLogs");

            migrationBuilder.DropTable(
                name: "TradingSessionSymbols");

            migrationBuilder.DropTable(
                name: "BacktestRuns");

            migrationBuilder.DropTable(
                name: "PaperAccounts");

            migrationBuilder.DropTable(
                name: "RiskProfiles");

            migrationBuilder.DropTable(
                name: "Candles");

            migrationBuilder.DropTable(
                name: "StrategySignals");

            migrationBuilder.DropTable(
                name: "Symbols");

            migrationBuilder.DropTable(
                name: "TradingSessions");

            migrationBuilder.DropTable(
                name: "Exchanges");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "RiskDecisions");

            migrationBuilder.DropTable(
                name: "Strategies");

            migrationBuilder.DropTable(
                name: "AiDecisions");
        }
    }
}
