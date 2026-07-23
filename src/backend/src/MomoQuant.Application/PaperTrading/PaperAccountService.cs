using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.PaperTrading;

public interface IPaperAccountService
{
    Task<ServiceResult<PaperAccountDto>> CreateAsync(CreatePaperAccountRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperAccountDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<PaperAccountDto>>> GetPagedAsync(PagedRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperAccountDto>> UpdateAsync(long id, UpdatePaperAccountRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperAccountDto>> ResetAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperAccountSnapshotDto>>> GetSnapshotsAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class PaperAccountService : IPaperAccountService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IPaperAccountRepository _accountRepository;
    private readonly IPaperAccountSnapshotRepository _snapshotRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public PaperAccountService(
        IPaperAccountRepository accountRepository,
        IPaperAccountSnapshotRepository snapshotRepository,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _accountRepository = accountRepository;
        _snapshotRepository = snapshotRepository;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<PaperAccountDto>> CreateAsync(
        CreatePaperAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<PaperAccountDto>.Fail("Name is required.", "name");
        }

        if (request.InitialBalance <= 0)
        {
            return ServiceResult<PaperAccountDto>.Fail("Initial balance must be greater than zero.", "initialBalance");
        }

        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            return ServiceResult<PaperAccountDto>.Fail("Currency is required.", "currency");
        }

        var now = DateTime.UtcNow;
        var account = new PaperAccount
        {
            Name = request.Name.Trim(),
            InitialBalance = request.InitialBalance,
            CurrentBalance = request.InitialBalance,
            CurrentEquity = request.InitialBalance,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _accountRepository.AddAsync(account, cancellationToken);
        await _accountRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "PAPER_ACCOUNT_CREATED",
            nameof(PaperAccount),
            account.Id,
            _currentUserService.UserId,
            newValueJson: JsonSerializer.Serialize(new { account.Name, account.InitialBalance }, JsonOptions),
            cancellationToken: cancellationToken);

        return ServiceResult<PaperAccountDto>.Ok(PaperMapper.MapAccount(account));
    }

    public async Task<ServiceResult<PaperAccountDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        return account is null
            ? ServiceResult<PaperAccountDto>.Fail("Paper account was not found.")
            : ServiceResult<PaperAccountDto>.Ok(PaperMapper.MapAccount(account));
    }

    public async Task<ServiceResult<PagedResult<PaperAccountDto>>> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _accountRepository.GetPagedAsync(request, cancellationToken);
        return ServiceResult<PagedResult<PaperAccountDto>>.Ok(new PagedResult<PaperAccountDto>
        {
            Items = items.Select(PaperMapper.MapAccount).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<PaperAccountDto>> UpdateAsync(
        long id,
        UpdatePaperAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<PaperAccountDto>.Fail("Name is required.", "name");
        }

        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account is null)
        {
            return ServiceResult<PaperAccountDto>.Fail("Paper account was not found.");
        }

        account.Name = request.Name.Trim();
        account.IsActive = request.IsActive;
        account.UpdatedAtUtc = DateTime.UtcNow;

        await _accountRepository.UpdateAsync(account, cancellationToken);
        await _accountRepository.SaveChangesAsync(cancellationToken);

        return ServiceResult<PaperAccountDto>.Ok(PaperMapper.MapAccount(account));
    }

    public async Task<ServiceResult<PaperAccountDto>> ResetAsync(long id, CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account is null)
        {
            return ServiceResult<PaperAccountDto>.Fail("Paper account was not found.");
        }

        account.CurrentBalance = account.InitialBalance;
        account.CurrentEquity = account.InitialBalance;
        account.TotalRealizedPnl = 0m;
        account.TotalUnrealizedPnl = 0m;
        account.TotalFees = 0m;
        account.MaxDrawdown = 0m;
        account.MaxDrawdownPercent = 0m;
        account.UpdatedAtUtc = DateTime.UtcNow;

        await _accountRepository.UpdateAsync(account, cancellationToken);
        await _accountRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "PAPER_ACCOUNT_RESET",
            nameof(PaperAccount),
            account.Id,
            _currentUserService.UserId,
            cancellationToken: cancellationToken);

        return ServiceResult<PaperAccountDto>.Ok(PaperMapper.MapAccount(account));
    }

    public async Task<ServiceResult<IReadOnlyList<PaperAccountSnapshotDto>>> GetSnapshotsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var account = await _accountRepository.GetByIdAsync(id, cancellationToken);
        if (account is null)
        {
            return ServiceResult<IReadOnlyList<PaperAccountSnapshotDto>>.Fail("Paper account was not found.");
        }

        var snapshots = await _snapshotRepository.GetByAccountIdAsync(id, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperAccountSnapshotDto>>.Ok(
            snapshots.Select(PaperMapper.MapSnapshot).ToList());
    }
}
