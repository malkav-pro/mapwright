using System.Collections.Immutable;

namespace Mapwright.Domain;

public sealed record TileInvalidation(
    ImmutableHashSet<LayerId> LayerIds,
    MapBounds Bounds,
    bool RebuildCoverage,
    bool RebuildDistance,
    bool RebuildComposite);

public sealed record DocumentChange(
    MapProject Project,
    TileInvalidation Invalidation,
    bool IsNoOp = false);

public interface IEditCommand
{
    CommandId Id { get; }
    long BaseRevision { get; }
    DocumentChange Apply(MapProject project);
}

public sealed record AddPaintStroke(
    CommandId Id,
    long BaseRevision,
    LayerId LayerId,
    PaintStroke Stroke) : IEditCommand
{
    public DocumentChange Apply(MapProject project)
    {
        CommandGuard.RequireRevision(project, BaseRevision);
        project.ValidateConnectedTerrain();
        var layerIndex = project.RequireLayerIndex(LayerId);
        var layer = project.TerrainLayers[layerIndex];
        if (Stroke.Kind != TerrainStrokeKind.Coverage || !Stroke.AddsCoverage)
            throw new InvalidDataException("A coverage command requires a coverage stroke.");
        if (layer.Role != TerrainRole.Foreground)
            throw new InvalidDataException("Only Foreground may own terrain coverage.");
        CommandGuard.RequirePaintable(project, layer);
        var updated = layer.Add(Stroke);
        var layers = project.TerrainLayers.SetItem(layerIndex, updated);
        var next = project with { TerrainLayers = layers, Revision = project.Revision + 1 };
        var bounds = Stroke.Bounds.Expand(layer.Coastline.EffectReach);
        return new DocumentChange(next, new TileInvalidation(
            ImmutableHashSet.Create(LayerId), bounds, true, true, true));
    }
}

public sealed record AddTextureStroke(
    CommandId Id,
    long BaseRevision,
    LayerId LayerId,
    PaintStroke Stroke) : IEditCommand
{
    public DocumentChange Apply(MapProject project)
    {
        CommandGuard.RequireRevision(project, BaseRevision);
        project.ValidateConnectedTerrain();
        if (Stroke.Kind != TerrainStrokeKind.Texture || Stroke.AddsCoverage)
            throw new InvalidDataException("A texture command cannot change Foreground coverage.");
        var layerIndex = project.RequireLayerIndex(LayerId);
        var layer = project.TerrainLayers[layerIndex];
        CommandGuard.RequirePaintable(project, layer);
        var updated = layer.Add(Stroke);
        var next = project with
        {
            TerrainLayers = project.TerrainLayers.SetItem(layerIndex, updated),
            Revision = project.Revision + 1
        };
        return new DocumentChange(next, new TileInvalidation(
            ImmutableHashSet.Create(LayerId), Stroke.Bounds, false, false, true));
    }
}

public sealed record AddResolvedTextureStroke(
    CommandId Id,
    long BaseRevision,
    TerrainRole TargetRole,
    TexturePaintStroke Stroke) : IEditCommand
{
    public DocumentChange Apply(MapProject project)
    {
        CommandGuard.RequireRevision(project, BaseRevision);
        project.ValidateConnectedTerrain();
        var layer = project.RequireRole(TargetRole);
        CommandGuard.RequirePaintable(project, layer);
        Stroke.Validate();
        var layerIndex = project.RequireLayerIndex(layer.Id);
        var updated = layer.AddTexture(Stroke);
        var next = project with
        {
            TerrainLayers = project.TerrainLayers.SetItem(layerIndex, updated),
            Revision = project.Revision + 1
        };
        return new DocumentChange(next, new TileInvalidation(
            ImmutableHashSet.Create(layer.Id), Stroke.Bounds, false, false, true));
    }
}

public sealed record AddLandStroke(
    CommandId Id,
    long BaseRevision,
    TerrainRole TargetRole,
    LandStroke Stroke) : IEditCommand
{
    public DocumentChange Apply(MapProject project)
    {
        CommandGuard.RequireRevision(project, BaseRevision);
        project.ValidateConnectedTerrain();
        if (TargetRole != TerrainRole.Foreground)
            throw new InvalidDataException("Land commands target only Foreground coverage.");
        var layer = project.RequireRole(TargetRole);
        CommandGuard.RequirePaintable(project, layer);
        Stroke.Validate();
        var layerIndex = project.RequireLayerIndex(layer.Id);
        var updated = layer.AddLand(Stroke);
        var next = project with
        {
            TerrainLayers = project.TerrainLayers.SetItem(layerIndex, updated),
            Revision = project.Revision + 1
        };
        var bounds = Stroke.Bounds.Expand(layer.Coastline.EffectReach);
        return new DocumentChange(next, new TileInvalidation(
            ImmutableHashSet.Create(layer.Id), bounds, true, true, true));
    }
}

public sealed record RenameTerrainLayer(
    CommandId Id,
    long BaseRevision,
    TerrainRole Role,
    string Name) : IEditCommand
{
    public DocumentChange Apply(MapProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        return TerrainStateCommands.Apply(project, BaseRevision, Role,
            layer => layer.Name == Name ? null : layer with { Name = Name }, rebuildComposite: false);
    }
}

public sealed record SetTerrainVisibility(
    CommandId Id,
    long BaseRevision,
    TerrainRole Role,
    bool Visible) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => TerrainStateCommands.Apply(
        project, BaseRevision, Role,
        layer => layer.Visible == Visible ? null : layer with { Visible = Visible }, rebuildComposite: true);
}

public sealed record SetTerrainLock(
    CommandId Id,
    long BaseRevision,
    TerrainRole Role,
    bool Locked) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => TerrainStateCommands.Apply(
        project, BaseRevision, Role,
        layer => layer.Locked == Locked ? null : layer with { Locked = Locked }, rebuildComposite: false);
}

public sealed record SetTerrainSolo(
    CommandId Id,
    long BaseRevision,
    TerrainRole Role,
    bool Solo) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => TerrainStateCommands.Apply(
        project, BaseRevision, Role,
        layer => layer.Solo == Solo ? null : layer with { Solo = Solo }, rebuildComposite: true,
        invalidateBothRoles: true);
}

public sealed record SetTerrainOpacity(
    CommandId Id,
    long BaseRevision,
    TerrainRole Role,
    double Opacity) : IEditCommand
{
    public DocumentChange Apply(MapProject project)
    {
        if (!double.IsFinite(Opacity) || Opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Opacity));
        return TerrainStateCommands.Apply(project, BaseRevision, Role,
            layer => layer.Opacity.Equals(Opacity) ? null : layer with { Opacity = Opacity },
            rebuildComposite: true);
    }
}

public sealed record MoveRiverPoint(
    CommandId Id,
    long BaseRevision,
    int PointIndex,
    MapPoint Position) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => RiverCommandChanges.Edit(
        project, BaseRevision, river =>
        {
            Position.ValidateFinite();
            if (PointIndex < 0 || PointIndex >= river.Points.Length)
                throw new ArgumentOutOfRangeException(nameof(PointIndex));
            return river with { Points = river.Points.SetItem(PointIndex, Position) };
        });
}

public sealed record SetRiverWidth(
    CommandId Id,
    long BaseRevision,
    double Width) : IEditCommand
{
    public DocumentChange Apply(MapProject project) =>
        new SetAllRiverWidths(Id, BaseRevision, Width).Apply(project);
}

public sealed record CreateRiver(
    CommandId Id,
    long BaseRevision,
    River River) : IEditCommand
{
    public DocumentChange Apply(MapProject project)
    {
        CommandGuard.RequireRevision(project, BaseRevision);
        project.ValidateConnectedTerrain();
        if (project.River is not null) throw new InvalidOperationException("The project already has a river.");
        River.Validate();
        var foreground = project.RequireRole(TerrainRole.Foreground);
        if (River.TargetLayerId != foreground.Id)
            throw new InvalidDataException("The river modifier must target Foreground coverage.");
        var next = project with { River = River, Revision = project.Revision + 1 };
        return RiverCommandChanges.Change(next, foreground.Id,
            River.Bounds(foreground.Coastline.EffectReach));
    }
}

public sealed record InsertRiverPoint(
    CommandId Id,
    long BaseRevision,
    int PointIndex,
    MapPoint Position,
    double Width) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => RiverCommandChanges.Edit(
        project, BaseRevision, river =>
        {
            Position.ValidateFinite();
            River.ValidateWidth(Width, nameof(Width));
            if (PointIndex < 0 || PointIndex > river.Points.Length)
                throw new ArgumentOutOfRangeException(nameof(PointIndex));
            return river with
            {
                Points = river.Points.Insert(PointIndex, Position),
                WidthProfile = new RiverWidthProfile(
                    river.ResolvedWidthProfile.Widths.Insert(PointIndex, Width))
            };
        });
}

public sealed record DeleteRiverPoint(
    CommandId Id,
    long BaseRevision,
    int PointIndex) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => RiverCommandChanges.Edit(
        project, BaseRevision, river =>
        {
            if (river.Points.Length <= 2)
                throw new InvalidOperationException("A river must retain at least two centreline points.");
            if (PointIndex < 0 || PointIndex >= river.Points.Length)
                throw new ArgumentOutOfRangeException(nameof(PointIndex));
            return river with
            {
                Points = river.Points.RemoveAt(PointIndex),
                WidthProfile = new RiverWidthProfile(
                    river.ResolvedWidthProfile.Widths.RemoveAt(PointIndex))
            };
        });
}

public sealed record SetRiverPointWidth(
    CommandId Id,
    long BaseRevision,
    int PointIndex,
    double Width) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => RiverCommandChanges.Edit(
        project, BaseRevision, river =>
        {
            River.ValidateWidth(Width, nameof(Width));
            if (PointIndex < 0 || PointIndex >= river.Points.Length)
                throw new ArgumentOutOfRangeException(nameof(PointIndex));
            return river with
            {
                WidthProfile = new RiverWidthProfile(
                    river.ResolvedWidthProfile.Widths.SetItem(PointIndex, Width))
            };
        });
}

public sealed record SetAllRiverWidths(
    CommandId Id,
    long BaseRevision,
    double Width) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => RiverCommandChanges.Edit(
        project, BaseRevision, river =>
        {
            River.ValidateWidth(Width, nameof(Width));
            return river with
            {
                Width = Width,
                WidthProfile = new RiverWidthProfile(
                    Enumerable.Repeat(Width, river.Points.Length).ToImmutableArray())
            };
        });
}

public sealed record SetRiverBankSoftness(
    CommandId Id,
    long BaseRevision,
    double BankSoftness) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => RiverCommandChanges.Edit(
        project, BaseRevision, river => river with { BankSoftness = BankSoftness });
}

public sealed record SetRiverEnabled(
    CommandId Id,
    long BaseRevision,
    bool Enabled) : IEditCommand
{
    public DocumentChange Apply(MapProject project) => RiverCommandChanges.Edit(
        project, BaseRevision, river => river with { Enabled = Enabled });
}

public sealed record DeleteRiver(
    CommandId Id,
    long BaseRevision) : IEditCommand
{
    public DocumentChange Apply(MapProject project) =>
        RiverCommandChanges.Edit(project, BaseRevision, _ => null);
}

internal static class RiverCommandChanges
{
    public static DocumentChange Edit(
        MapProject project,
        long baseRevision,
        Func<River, River?> update)
    {
        CommandGuard.RequireRevision(project, baseRevision);
        project.ValidateConnectedTerrain();
        var river = project.River ?? throw new InvalidOperationException("The project has no river to edit.");
        var foreground = project.RequireRole(TerrainRole.Foreground);
        var oldBounds = river.Bounds(foreground.Coastline.EffectReach);
        var updated = update(river);
        var bounds = oldBounds;
        if (updated is not null)
        {
            updated.Validate();
            if (updated.Id != river.Id)
                throw new InvalidDataException("River edits must preserve stable identity.");
            if (updated.TargetLayerId != foreground.Id)
                throw new InvalidDataException("The river modifier must target Foreground coverage.");
            bounds = bounds.Union(updated.Bounds(foreground.Coastline.EffectReach));
        }
        var next = project with { River = updated, Revision = project.Revision + 1 };
        return Change(next, foreground.Id, bounds);
    }

    public static DocumentChange Change(MapProject next, LayerId layerId, MapBounds bounds) => new(
        next,
        new TileInvalidation(ImmutableHashSet.Create(layerId), bounds, true, true, true));
}

internal static class CommandGuard
{
    public static void RequireRevision(MapProject project, long baseRevision)
    {
        if (project.Revision != baseRevision)
            throw new RevisionConflictException(baseRevision, project.Revision);
    }

    public static void RequirePaintable(MapProject project, TerrainLayer layer)
    {
        if (layer.Locked)
            throw new PaintTargetBlockedException(layer.Role, PaintBlockReason.Locked, "Unlock");
        if (!project.IsTerrainEffectivelyVisible(layer.Role))
            throw new PaintTargetBlockedException(layer.Role, PaintBlockReason.Hidden, "Show layer");
    }
}

internal static class TerrainStateCommands
{
    public static DocumentChange Apply(
        MapProject project,
        long baseRevision,
        TerrainRole role,
        Func<TerrainLayer, TerrainLayer?> update,
        bool rebuildComposite,
        bool invalidateBothRoles = false)
    {
        CommandGuard.RequireRevision(project, baseRevision);
        project.ValidateConnectedTerrain();
        var index = project.RequireLayerIndex(project.RequireRole(role).Id);
        var current = project.TerrainLayers[index];
        var updated = update(current);
        if (updated is null)
            return new DocumentChange(project, EmptyInvalidation(), IsNoOp: true);

        updated.Validate();
        var next = project with
        {
            TerrainLayers = project.TerrainLayers.SetItem(index, updated),
            Revision = project.Revision + 1
        };
        var ids = invalidateBothRoles
            ? project.TerrainLayers.Select(layer => layer.Id).ToImmutableHashSet()
            : ImmutableHashSet.Create(current.Id);
        return new DocumentChange(next, new TileInvalidation(
            ids,
            new MapBounds(0, 0, project.Width, project.Height),
            false,
            false,
            rebuildComposite));
    }

    private static TileInvalidation EmptyInvalidation() => new(
        ImmutableHashSet<LayerId>.Empty,
        default,
        false,
        false,
        false);
}

public enum PaintBlockReason
{
    Locked = 0,
    Hidden = 1
}

public sealed class PaintTargetBlockedException(
    TerrainRole role,
    PaintBlockReason reason,
    string suggestedAction)
    : InvalidOperationException($"{role} cannot be painted because it is {reason.ToString().ToLowerInvariant()}. {suggestedAction}.")
{
    public TerrainRole Role { get; } = role;
    public PaintBlockReason Reason { get; } = reason;
    public string SuggestedAction { get; } = suggestedAction;
}

public sealed class RevisionConflictException(long expected, long actual)
    : InvalidOperationException($"Command expected revision {expected}, but the project is at revision {actual}.")
{
    public long Expected { get; } = expected;
    public long Actual { get; } = actual;
}
