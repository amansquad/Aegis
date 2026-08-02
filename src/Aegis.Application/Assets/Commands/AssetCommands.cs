using Aegis.Application.Abstractions.Multitenancy;
using Aegis.Application.Abstractions.Persistence;
using Aegis.Application.Abstractions.Security;
using Aegis.Application.Messaging;
using Aegis.Domain.Assets;
using Aegis.Domain.Assets.ValueObjects;
using Aegis.Domain.Common;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Application.Assets.Commands;

/// <summary>Errors shared by the asset commands.</summary>
internal static class AssetErrors
{
    /// <summary>The asset does not exist, or belongs to another organization.</summary>
    public static readonly Error NotFound = Error.NotFound("Asset.NotFound", "The asset was not found.");

    /// <summary>The asset code is already in use within the organization.</summary>
    public static readonly Error DuplicateCode = Error.Conflict(
        "Asset.DuplicateCode",
        "An asset with this code already exists. Codes must be unique within an organization.");
}

/// <summary>Adds an asset to the registry.</summary>
/// <param name="Code">Operator-facing identifier, unique within the organization.</param>
/// <param name="Name">Descriptive name.</param>
/// <param name="Type">Kind of infrastructure.</param>
/// <param name="Latitude">Position, when known.</param>
/// <param name="Longitude">Position, when known.</param>
/// <param name="Criticality">Consequence of failure.</param>
/// <param name="ParentAssetId">Containing asset, such as the station a pump sits in.</param>
/// <param name="Manufacturer">Manufacturer, where recorded.</param>
/// <param name="ModelNumber">Model designation, where recorded.</param>
/// <param name="SerialNumber">Serial number, where recorded.</param>
/// <param name="InstalledOn">Date the asset entered service.</param>
/// <param name="ExpectedLifespanYears">Design life, used for replacement planning.</param>
/// <param name="Notes">Free-text notes.</param>
public sealed record RegisterAssetCommand(
    string Code,
    string Name,
    AssetType Type,
    double? Latitude,
    double? Longitude,
    AssetCriticality Criticality = AssetCriticality.Medium,
    Guid? ParentAssetId = null,
    string? Manufacturer = null,
    string? ModelNumber = null,
    string? SerialNumber = null,
    DateOnly? InstalledOn = null,
    int? ExpectedLifespanYears = null,
    string? Notes = null) : ICommand<Guid>;

/// <summary>Validates <see cref="RegisterAssetCommand"/>.</summary>
public sealed class RegisterAssetCommandValidator : AbstractValidator<RegisterAssetCommand>
{
    /// <summary>Initialises the validator.</summary>
    public RegisterAssetCommandValidator()
    {
        RuleFor(c => c.Code)
            .NotEmpty().WithMessage("An asset code is required.")
            .MaximumLength(AssetCode.MaxLength)
            .Must(code => AssetCode.Create(code).IsSuccess)
            .WithMessage("An asset code may contain only letters, digits, hyphens, underscores and slashes.");

        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Type).IsInEnum();
        RuleFor(c => c.Criticality).IsInEnum();

        // Latitude and longitude are meaningless apart, so both or neither. Accepting one would
        // store half a position, which reads as a location and points at the Gulf of Guinea.
        RuleFor(c => c.Longitude)
            .NotNull()
            .When(c => c.Latitude is not null)
            .WithMessage("Longitude is required when latitude is supplied.");

        RuleFor(c => c.Latitude)
            .NotNull()
            .When(c => c.Longitude is not null)
            .WithMessage("Latitude is required when longitude is supplied.");

        RuleFor(c => c.ExpectedLifespanYears)
            .InclusiveBetween(1, 200)
            .When(c => c.ExpectedLifespanYears is not null);

        RuleFor(c => c.Notes).MaximumLength(4000);
    }
}

/// <summary>Handles <see cref="RegisterAssetCommand"/>.</summary>
internal sealed class RegisterAssetCommandHandler(
    IAegisDbContext context,
    ITenantContext tenantContext,
    TimeProvider timeProvider) : ICommandHandler<RegisterAssetCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(RegisterAssetCommand request, CancellationToken cancellationToken)
    {
        var code = AssetCode.Create(request.Code);

        if (code.IsFailure)
        {
            return Result.Failure<Guid>(code.Error);
        }

        GeoCoordinate? location = null;

        if (request.Latitude is { } latitude && request.Longitude is { } longitude)
        {
            var coordinate = GeoCoordinate.Create(latitude, longitude);

            if (coordinate.IsFailure)
            {
                return Result.Failure<Guid>(coordinate.Error);
            }

            location = coordinate.Value;
        }

        var assetCode = code.Value;

        // Checked before insert so the caller gets a clear conflict rather than a raw unique index
        // violation. The index remains the actual guarantee — this check races, and losing that
        // race must fail safely rather than admit a duplicate.
        var codeTaken = await context.Assets.AnyAsync(a => a.Code == assetCode, cancellationToken);

        if (codeTaken)
        {
            return Result.Failure<Guid>(AssetErrors.DuplicateCode);
        }

        if (request.ParentAssetId is { } parentId)
        {
            var parentExists = await context.Assets.AnyAsync(a => a.Id == parentId, cancellationToken);

            if (!parentExists)
            {
                return Result.Failure<Guid>(Error.NotFound(
                    "Asset.ParentNotFound",
                    "The parent asset was not found in this organization."));
            }
        }

        var asset = Asset.Register(
            tenantContext.RequireOrganizationId(),
            assetCode,
            request.Name,
            request.Type,
            location);

        if (asset.IsFailure)
        {
            return Result.Failure<Guid>(asset.Error);
        }

        var details = asset.Value.UpdateDetails(
            request.Name,
            request.Criticality,
            request.Manufacturer,
            request.ModelNumber,
            request.SerialNumber,
            request.InstalledOn,
            request.ExpectedLifespanYears,
            request.Notes,
            timeProvider.GetUtcNow());

        if (details.IsFailure)
        {
            return Result.Failure<Guid>(details.Error);
        }

        if (request.ParentAssetId is not null)
        {
            var reparented = asset.Value.Reparent(request.ParentAssetId);

            if (reparented.IsFailure)
            {
                return Result.Failure<Guid>(reparented.Error);
            }
        }

        context.Assets.Add(asset.Value);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(asset.Value.Id);
    }
}

/// <summary>Records an inspection against an asset.</summary>
/// <param name="AssetId">The asset inspected.</param>
/// <param name="Condition">The assessed condition.</param>
/// <param name="InspectedOnUtc">When the inspection took place.</param>
/// <param name="Notes">Observations.</param>
public sealed record RecordInspectionCommand(
    Guid AssetId,
    AssetCondition Condition,
    DateTimeOffset InspectedOnUtc,
    string? Notes) : ICommand<Guid>;

/// <summary>Validates <see cref="RecordInspectionCommand"/>.</summary>
public sealed class RecordInspectionCommandValidator : AbstractValidator<RecordInspectionCommand>
{
    /// <summary>Initialises the validator.</summary>
    public RecordInspectionCommandValidator()
    {
        RuleFor(c => c.AssetId).NotEmpty();
        RuleFor(c => c.Condition).IsInEnum().NotEqual(AssetCondition.Unknown);
        RuleFor(c => c.Notes).MaximumLength(2000);
    }
}

/// <summary>Handles <see cref="RecordInspectionCommand"/>.</summary>
internal sealed class RecordInspectionCommandHandler(
    IAegisDbContext context,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<RecordInspectionCommand, Guid>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> Handle(
        RecordInspectionCommand request,
        CancellationToken cancellationToken)
    {
        var asset = await context.Assets
            .Include(a => a.Inspections)
            .SingleOrDefaultAsync(a => a.Id == request.AssetId, cancellationToken);

        if (asset is null)
        {
            return Result.Failure<Guid>(AssetErrors.NotFound);
        }

        var inspection = asset.RecordInspection(
            request.Condition,
            request.InspectedOnUtc,
            currentUser.Id ?? Guid.Empty,
            request.Notes,
            timeProvider.GetUtcNow());

        if (inspection.IsFailure)
        {
            return Result.Failure<Guid>(inspection.Error);
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(inspection.Value.Id);
    }
}

/// <summary>Permanently retires an asset.</summary>
/// <param name="AssetId">The asset to retire.</param>
/// <param name="Reason">Why, for the record.</param>
public sealed record DecommissionAssetCommand(Guid AssetId, string? Reason) : ICommand;

/// <summary>Handles <see cref="DecommissionAssetCommand"/>.</summary>
internal sealed class DecommissionAssetCommandHandler(
    IAegisDbContext context,
    TimeProvider timeProvider) : ICommandHandler<DecommissionAssetCommand>
{
    /// <inheritdoc />
    public async Task<Result> Handle(
        DecommissionAssetCommand request,
        CancellationToken cancellationToken)
    {
        var asset = await context.Assets.SingleOrDefaultAsync(a => a.Id == request.AssetId, cancellationToken);

        if (asset is null)
        {
            return Result.Failure(AssetErrors.NotFound);
        }

        // Refused while the asset still contains others. Retiring a pumping station whose pumps are
        // still listed as operational leaves the registry describing an impossible state, and the
        // pumps orphaned under a parent nobody maintains.
        var hasActiveChildren = await context.Assets.AnyAsync(
            a => a.ParentAssetId == request.AssetId && a.Status != AssetStatus.Decommissioned,
            cancellationToken);

        if (hasActiveChildren)
        {
            return Result.Failure(Error.Conflict(
                "Asset.HasActiveChildren",
                "This asset still contains assets that are in service. Decommission or reassign " +
                "them first."));
        }

        var decommissioned = asset.Decommission(timeProvider.GetUtcNow(), request.Reason);

        if (decommissioned.IsFailure)
        {
            return decommissioned;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
