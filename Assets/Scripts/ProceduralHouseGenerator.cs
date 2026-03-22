using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ProceduralHouseGenerator : MonoBehaviour
{
    private const string TargetSceneName = "Dungeon Test";
    private const string ResourceFolder = "Dungeon Parts/";
    private const float CellSize = 2.5f;
    private const float HalfCellSize = CellSize * 0.5f;
    private const float FloorHeight = 2.5f;
    private const float InitialDoorDistance = 10f;
    private const float BoundaryDepth = 0.2f;
    private const float VisibilityProbeSize = 0.15f;
    private const float VisibilityVerticalOffset = 1.2f;
    private const float VisibilityLateralOffset = 0.45f;
    private const int RequiredSegmentsAhead = 4;
    private const int MaxGenerationAttemptsPerType = 12;
    private const int MaxLevels = 3;

    private static readonly Direction[] AllDirections =
    {
        Direction.North,
        Direction.East,
        Direction.South,
        Direction.West
    };

    private readonly Dictionary<GridCoord, CellData> cells = new Dictionary<GridCoord, CellData>();
    private readonly Dictionary<BoundaryKey, GameObject> boundaryObjects = new Dictionary<BoundaryKey, GameObject>();
    private readonly HashSet<BoundaryKey> doorBoundaries = new HashSet<BoundaryKey>();
    private readonly HashSet<BoundaryKey> openBoundaries = new HashSet<BoundaryKey>();
    private readonly List<SegmentData> segments = new List<SegmentData>();
    private readonly List<BoundaryKey> boundaryRemovalBuffer = new List<BoundaryKey>();

    private GameObject floorPrefab;
    private GameObject wallPrefab;
    private GameObject ceilingPrefab;
    private GameObject doorWallPrefab;
    private GameObject windowWallPrefab;
    private GameObject stairsPrefab;

    private System.Random random;
    private Transform playerTransform;
    private Camera playerCamera;
    private Vector3 worldGridOrigin;
    private Direction initialDirection;
    private Frontier tailFrontier;
    private int sessionSeed;
    private int currentSegmentIndex;
    private bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !string.Equals(scene.name, TargetSceneName, StringComparison.OrdinalIgnoreCase))
            return;

        if (FindObjectOfType<ProceduralHouseGenerator>() != null)
            return;

        GameObject generatorObject = new GameObject("Procedural House Generator");
        generatorObject.AddComponent<ProceduralHouseGenerator>();
    }

    private void Start()
    {
        if (!TryInitialize())
        {
            Destroy(gameObject);
            return;
        }

        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        ResolvePlayerReferences();

        if (playerTransform == null || playerCamera == null)
            return;

        currentSegmentIndex = FindCurrentSegmentIndex(playerTransform.position);
        EnsureForwardBuffer(false);
    }

    private bool TryInitialize()
    {
        if (!LoadPrefabs())
            return false;

        ResolvePlayerReferences();
        if (playerTransform == null)
            return false;

        sessionSeed = unchecked((int)DateTime.UtcNow.Ticks);
        random = new System.Random(sessionSeed);
        initialDirection = GetClosestDirection(playerTransform.forward);
        worldGridOrigin = ComputeInitialOrigin(playerTransform.position, initialDirection);

        if (!TryBuildInitialRoom(out SegmentBlueprint initialBlueprint))
            return false;

        SegmentData initialSegment = CommitBlueprint(initialBlueprint);
        segments.Add(initialSegment);
        currentSegmentIndex = 0;

        doorBoundaries.Add(GetCanonicalBoundary(initialBlueprint.EntranceDoorCell, initialBlueprint.EntranceDoorSide));
        tailFrontier = initialBlueprint.ExitFrontier;

        RebuildAllBoundaries();
        EnsureForwardBuffer(true);
        return true;
    }

    private bool LoadPrefabs()
    {
        floorPrefab = Resources.Load<GameObject>(ResourceFolder + "Floor");
        wallPrefab = Resources.Load<GameObject>(ResourceFolder + "Wall");
        ceilingPrefab = Resources.Load<GameObject>(ResourceFolder + "Ceiling");
        doorWallPrefab = Resources.Load<GameObject>(ResourceFolder + "DoorWall");
        windowWallPrefab = Resources.Load<GameObject>(ResourceFolder + "WindowWall");
        stairsPrefab = Resources.Load<GameObject>(ResourceFolder + "Stairs");

        return floorPrefab != null &&
               wallPrefab != null &&
               ceilingPrefab != null &&
               doorWallPrefab != null &&
               windowWallPrefab != null &&
               stairsPrefab != null;
    }

    private void ResolvePlayerReferences()
    {
        if (playerTransform == null)
        {
            SimpleFirstPersonController controller = FindObjectOfType<SimpleFirstPersonController>();
            if (controller != null)
                playerTransform = controller.transform;
        }

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null && playerTransform != null)
            playerCamera = playerTransform.GetComponentInChildren<Camera>();
    }

    private void EnsureForwardBuffer(bool ignoreVisibility)
    {
        int attempts = 0;
        while (segments.Count - 1 - currentSegmentIndex < RequiredSegmentsAhead && attempts < RequiredSegmentsAhead + 2)
        {
            if (!TryExtendPath(ignoreVisibility))
                break;

            attempts++;
        }
    }

    private bool TryExtendPath(bool ignoreVisibility)
    {
        BoundaryKey tailBoundary = GetCanonicalBoundary(tailFrontier.SourceCell, tailFrontier.Direction);
        if (!ignoreVisibility && !IsBoundaryHidden(tailBoundary))
            return false;

        if (!TryBuildNextSegment(tailFrontier, out SegmentBlueprint blueprint))
            return false;

        BoundaryKey connectionBoundary = GetCanonicalBoundary(tailFrontier.SourceCell, tailFrontier.Direction);
        if (blueprint.UsesOpenEntrance)
            openBoundaries.Add(connectionBoundary);
        else
            doorBoundaries.Add(connectionBoundary);

        for (int i = 0; i < blueprint.OpenBoundaryCount; i++)
            openBoundaries.Add(blueprint.OpenBoundaries[i]);

        SegmentData segment = CommitBlueprint(blueprint);
        segments.Add(segment);
        tailFrontier = blueprint.ExitFrontier;
        RebuildAllBoundaries();
        return true;
    }

    private SegmentType[] BuildTypeOrder()
    {
        bool canUseStairs = tailFrontier.SourceCell.Level < MaxLevels - 1;
        List<SegmentType> types = new List<SegmentType>(3)
        {
            SegmentType.Room,
            SegmentType.Hallway
        };

        if (canUseStairs)
            types.Add(SegmentType.Staircase);

        Shuffle(types);
        return types.ToArray();
    }

    private bool TryBuildInitialRoom(out SegmentBlueprint blueprint)
    {
        GridCoord entranceCell = GridCoord.Zero;
        for (int attempt = 0; attempt < MaxGenerationAttemptsPerType; attempt++)
        {
            if (TryBuildRoomBlueprint(entranceCell, initialDirection, out blueprint))
            {
                blueprint.EntranceDoorCell = entranceCell;
                blueprint.EntranceDoorSide = Opposite(initialDirection);
                return true;
            }
        }

        blueprint = default;
        return false;
    }

    private bool TryBuildNextSegment(Frontier frontier, out SegmentBlueprint blueprint)
    {
        SegmentType[] types = BuildTypeOrder();
        for (int i = 0; i < types.Length; i++)
        {
            for (int attempt = 0; attempt < MaxGenerationAttemptsPerType; attempt++)
            {
                switch (types[i])
                {
                    case SegmentType.Room:
                        if (TryBuildRoomBlueprint(frontier.SourceCell.Step(frontier.Direction), frontier.Direction, out blueprint))
                            return true;
                        break;
                    case SegmentType.Hallway:
                        if (TryBuildHallwayBlueprint(frontier.SourceCell.Step(frontier.Direction), frontier.Direction, out blueprint))
                            return true;
                        break;
                    case SegmentType.Staircase:
                        if (TryBuildStaircaseBlueprint(frontier.SourceCell.Step(frontier.Direction), frontier.Direction, out blueprint))
                            return true;
                        break;
                }
            }
        }

        blueprint = default;
        return false;
    }

    private bool TryBuildRoomBlueprint(GridCoord entranceCell, Direction entryDirection, out SegmentBlueprint blueprint)
    {
        int width = random.Next(2, 5);
        int depth = random.Next(2, 5);
        int leftCells = random.Next(0, width);
        int rightCells = width - 1 - leftCells;

        SegmentBlueprint candidate = new SegmentBlueprint
        {
            Type = SegmentType.Room,
            EntranceDoorCell = entranceCell,
            EntranceDoorSide = Opposite(entryDirection),
            UsesOpenEntrance = false
        };

        for (int forward = 0; forward < depth; forward++)
        {
            for (int lateral = -leftCells; lateral <= rightCells; lateral++)
            {
                GridCoord coord = OffsetFromEntrance(entranceCell, entryDirection, forward, lateral);
                candidate.AddCell(new CellSpec(coord, CellKind.Standard, entryDirection));
            }
        }

        Direction[] exitChoices = BuildRoomExitChoices(entryDirection);
        Shuffle(exitChoices);

        for (int i = 0; i < exitChoices.Length; i++)
        {
            Frontier frontier = CreateRoomExit(entranceCell, entryDirection, depth, leftCells, rightCells, exitChoices[i]);
            if (!IsBlueprintValid(candidate, frontier))
                continue;

            candidate.ExitFrontier = frontier;
            blueprint = candidate;
            return true;
        }

        blueprint = default;
        return false;
    }

    private bool TryBuildHallwayBlueprint(GridCoord entranceCell, Direction entryDirection, out SegmentBlueprint blueprint)
    {
        SegmentBlueprint candidate = new SegmentBlueprint
        {
            Type = SegmentType.Hallway,
            EntranceDoorCell = entranceCell,
            EntranceDoorSide = Opposite(entryDirection),
            UsesOpenEntrance = false
        };

        int length = random.Next(2, 5);
        for (int i = 0; i < length; i++)
        {
            GridCoord coord = entranceCell;
            for (int step = 0; step < i; step++)
                coord = coord.Step(entryDirection);

            candidate.AddCell(new CellSpec(coord, CellKind.Standard, entryDirection));
        }

        Frontier frontier = new Frontier(candidate.Cells[candidate.CellCount - 1].Coord, entryDirection);
        if (!IsBlueprintValid(candidate, frontier))
        {
            blueprint = default;
            return false;
        }

        candidate.ExitFrontier = frontier;
        blueprint = candidate;
        return true;
    }

    private bool TryBuildStaircaseBlueprint(GridCoord entranceCell, Direction entryDirection, out SegmentBlueprint blueprint)
    {
        if (entranceCell.Level >= MaxLevels - 1)
        {
            blueprint = default;
            return false;
        }

        GridCoord landingCell = entranceCell.Step(entryDirection).WithLevel(entranceCell.Level + 1);
        if (cells.ContainsKey(entranceCell) || cells.ContainsKey(landingCell))
        {
            blueprint = default;
            return false;
        }

        SegmentBlueprint candidate = new SegmentBlueprint
        {
            Type = SegmentType.Staircase,
            EntranceDoorCell = entranceCell,
            EntranceDoorSide = Opposite(entryDirection),
            UsesOpenEntrance = true
        };

        candidate.AddCell(new CellSpec(entranceCell, CellKind.StairBase, entryDirection));
        candidate.AddCell(new CellSpec(landingCell, CellKind.StairLanding, entryDirection));
        candidate.AddOpenBoundary(GetCanonicalBoundary(landingCell, Opposite(entryDirection)));

        Direction[] exitChoices =
        {
            entryDirection,
            TurnLeft(entryDirection),
            TurnRight(entryDirection)
        };

        Shuffle(exitChoices);
        for (int i = 0; i < exitChoices.Length; i++)
        {
            Frontier frontier = new Frontier(landingCell, exitChoices[i]);
            if (!IsBlueprintValid(candidate, frontier))
                continue;

            candidate.ExitFrontier = frontier;
            blueprint = candidate;
            return true;
        }

        blueprint = default;
        return false;
    }

    private SegmentData CommitBlueprint(SegmentBlueprint blueprint)
    {
        GameObject segmentObject = new GameObject($"Generated Segment {segments.Count:D2} {blueprint.Type}");
        segmentObject.transform.SetParent(transform, false);

        SegmentData segment = new SegmentData
        {
            Index = segments.Count,
            Type = blueprint.Type,
            Root = segmentObject.transform
        };

        bool hasBounds = false;
        Bounds segmentBounds = default;

        for (int i = 0; i < blueprint.CellCount; i++)
        {
            CellSpec spec = blueprint.Cells[i];
            CellData cell = CreateCell(segment, spec);
            cells.Add(spec.Coord, cell);
            segment.Cells.Add(spec.Coord);

            Bounds cellBounds = GetCellBounds(spec);
            if (!hasBounds)
            {
                segmentBounds = cellBounds;
                hasBounds = true;
            }
            else
            {
                segmentBounds.Encapsulate(cellBounds);
            }
        }

        segment.WorldBounds = segmentBounds;
        segment.ExitFrontier = blueprint.ExitFrontier;
        return segment;
    }

    private CellData CreateCell(SegmentData segment, CellSpec spec)
    {
        Vector3 worldPosition = GetCellWorldPosition(spec.Coord);
        GameObject cellRoot = new GameObject($"{spec.Kind}_{spec.Coord.X}_{spec.Coord.Level}_{spec.Coord.Z}");
        cellRoot.transform.SetParent(segment.Root, false);
        cellRoot.transform.position = worldPosition;

        if (spec.Kind == CellKind.StairBase)
        {
            Instantiate(stairsPrefab, worldPosition, Quaternion.Euler(0f, GetDirectionYaw(spec.PrimaryDirection), 0f), cellRoot.transform);
        }
        else
        {
            Instantiate(floorPrefab, worldPosition, Quaternion.identity, cellRoot.transform);
            Instantiate(ceilingPrefab, worldPosition, Quaternion.identity, cellRoot.transform);
        }

        return new CellData
        {
            Coord = spec.Coord,
            Kind = spec.Kind,
            PrimaryDirection = spec.PrimaryDirection,
            Root = cellRoot,
            SegmentIndex = segment.Index
        };
    }

    private bool IsBlueprintValid(SegmentBlueprint blueprint, Frontier exitFrontier)
    {
        if (!blueprint.Contains(exitFrontier.SourceCell))
            return false;

        GridCoord nextCell = exitFrontier.SourceCell.Step(exitFrontier.Direction);
        if (cells.ContainsKey(nextCell) || blueprint.Contains(nextCell))
            return false;

        for (int i = 0; i < blueprint.CellCount; i++)
        {
            if (cells.ContainsKey(blueprint.Cells[i].Coord))
                return false;
        }

        return true;
    }

    private int FindCurrentSegmentIndex(Vector3 playerPosition)
    {
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            Bounds expanded = segments[i].WorldBounds;
            expanded.Expand(new Vector3(0.2f, 1f, 0.2f));
            if (expanded.Contains(playerPosition))
                return i;
        }

        int closestIndex = currentSegmentIndex;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < segments.Count; i++)
        {
            Vector3 point = segments[i].WorldBounds.ClosestPoint(playerPosition);
            float sqrDistance = (point - playerPosition).sqrMagnitude;
            if (sqrDistance >= closestDistance)
                continue;

            closestDistance = sqrDistance;
            closestIndex = i;
        }

        return closestIndex;
    }

    private void RebuildAllBoundaries()
    {
        HashSet<BoundaryKey> activeKeys = new HashSet<BoundaryKey>();
        foreach (KeyValuePair<GridCoord, CellData> pair in cells)
        {
            for (int i = 0; i < AllDirections.Length; i++)
                activeKeys.Add(GetCanonicalBoundary(pair.Key, AllDirections[i]));
        }

        foreach (BoundaryKey key in activeKeys)
            UpdateBoundary(key, ResolveBoundaryVisual(key));

        boundaryRemovalBuffer.Clear();
        foreach (KeyValuePair<BoundaryKey, GameObject> pair in boundaryObjects)
        {
            if (activeKeys.Contains(pair.Key))
                continue;

            boundaryRemovalBuffer.Add(pair.Key);
        }

        for (int i = 0; i < boundaryRemovalBuffer.Count; i++)
        {
            BoundaryKey key = boundaryRemovalBuffer[i];
            if (boundaryObjects.TryGetValue(key, out GameObject boundaryObject) && boundaryObject != null)
                Destroy(boundaryObject);

            boundaryObjects.Remove(key);
        }
    }

    private void UpdateBoundary(BoundaryKey key, BoundaryVisualType type)
    {
        if (type == BoundaryVisualType.None)
        {
            if (boundaryObjects.TryGetValue(key, out GameObject existingNone) && existingNone != null)
                Destroy(existingNone);

            boundaryObjects.Remove(key);
            return;
        }

        if (boundaryObjects.TryGetValue(key, out GameObject existing) && existing != null)
            Destroy(existing);

        GameObject prefab = GetBoundaryPrefab(type);
        if (prefab == null)
        {
            boundaryObjects.Remove(key);
            return;
        }

        Vector3 position = GetBoundaryWorldPosition(key.Cell, key.Side);
        Quaternion rotation = Quaternion.Euler(0f, GetDirectionYaw(key.Side), 0f);
        GameObject boundaryObject = Instantiate(prefab, position, rotation, transform);
        boundaryObject.name = $"{type}_{key.Cell.X}_{key.Cell.Level}_{key.Cell.Z}_{key.Side}";
        boundaryObjects[key] = boundaryObject;
    }

    private BoundaryVisualType ResolveBoundaryVisual(BoundaryKey key)
    {
        bool hasA = cells.TryGetValue(key.Cell, out CellData cellA);
        GridCoord neighbor = key.Cell.Step(key.Side);
        bool hasB = cells.TryGetValue(neighbor, out CellData cellB);
        Direction sideA = key.Side;
        Direction sideB = Opposite(key.Side);

        if (!hasA && !hasB)
            return BoundaryVisualType.None;

        if ((hasA && cellA.SuppressesBoundary(sideA)) || (hasB && cellB.SuppressesBoundary(sideB)))
            return BoundaryVisualType.None;

        if (openBoundaries.Contains(key))
            return BoundaryVisualType.None;

        if (doorBoundaries.Contains(key))
            return BoundaryVisualType.DoorWall;

        if (hasA && hasB)
            return BoundaryVisualType.Wall;

        CellData exteriorCell = hasA ? cellA : cellB;
        Direction exteriorSide = hasA ? sideA : sideB;
        if (ShouldUseWindow(exteriorCell, exteriorSide))
            return BoundaryVisualType.WindowWall;

        return BoundaryVisualType.Wall;
    }

    private bool ShouldUseWindow(CellData cell, Direction side)
    {
        if (cell.Kind != CellKind.Standard)
            return false;

        if (cell.SegmentIndex < 0 || cell.SegmentIndex >= segments.Count)
            return false;

        if (segments[cell.SegmentIndex].Type != SegmentType.Room)
            return false;

        int hash = sessionSeed;
        hash = (hash * 397) ^ cell.Coord.X;
        hash = (hash * 397) ^ cell.Coord.Level;
        hash = (hash * 397) ^ cell.Coord.Z;
        hash = (hash * 397) ^ (int)side;
        hash &= int.MaxValue;
        return (hash % 100) < 22;
    }

    private bool IsBoundaryHidden(BoundaryKey boundary)
    {
        if (playerCamera == null)
            return false;

        Bounds bounds = GetBoundaryBounds(boundary);
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(playerCamera);
        if (!GeometryUtility.TestPlanesAABB(planes, bounds))
            return true;

        Vector3[] samplePoints = GetBoundarySamplePoints(boundary);
        GameObject boundaryObject = boundaryObjects.TryGetValue(boundary, out GameObject existing)
            ? existing
            : null;

        for (int i = 0; i < samplePoints.Length; i++)
        {
            Bounds sampleBounds = new Bounds(samplePoints[i], Vector3.one * VisibilityProbeSize);
            if (!GeometryUtility.TestPlanesAABB(planes, sampleBounds))
                continue;

            if (IsSampleVisible(samplePoints[i], boundaryObject))
                return false;
        }

        return true;
    }

    private bool IsSampleVisible(Vector3 samplePoint, GameObject boundaryObject)
    {
        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = samplePoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance + 0.05f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, CompareHitsByDistance);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
                continue;

            if (playerTransform != null && hitCollider.transform.IsChildOf(playerTransform))
                continue;

            if (boundaryObject != null && hitCollider.transform.IsChildOf(boundaryObject.transform))
                return true;

            if (hits[i].distance < distance - 0.02f)
                return false;
        }

        return boundaryObject == null;
    }

    private static int CompareHitsByDistance(RaycastHit left, RaycastHit right)
    {
        return left.distance.CompareTo(right.distance);
    }

    private Bounds GetBoundaryBounds(BoundaryKey boundary)
    {
        if (boundaryObjects.TryGetValue(boundary, out GameObject boundaryObject) && boundaryObject != null)
        {
            Renderer[] renderers = boundaryObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                return bounds;
            }
        }

        Vector3 size = boundary.Side == Direction.North || boundary.Side == Direction.South
            ? new Vector3(CellSize, FloorHeight, BoundaryDepth)
            : new Vector3(BoundaryDepth, FloorHeight, CellSize);

        return new Bounds(GetBoundaryWorldPosition(boundary.Cell, boundary.Side) + Vector3.up * (FloorHeight * 0.5f), size);
    }

    private Vector3[] GetBoundarySamplePoints(BoundaryKey boundary)
    {
        Vector3 center = GetBoundaryWorldPosition(boundary.Cell, boundary.Side) + Vector3.up * VisibilityVerticalOffset;
        Vector3 lateral = GetDirectionVector(TurnRight(boundary.Side)) * VisibilityLateralOffset;
        return new[]
        {
            center,
            center + lateral,
            center - lateral,
            center + Vector3.up * 0.6f,
            center - Vector3.up * 0.45f
        };
    }

    private GameObject GetBoundaryPrefab(BoundaryVisualType type)
    {
        switch (type)
        {
            case BoundaryVisualType.Wall:
                return wallPrefab;
            case BoundaryVisualType.WindowWall:
                return windowWallPrefab;
            case BoundaryVisualType.DoorWall:
                return doorWallPrefab;
            default:
                return null;
        }
    }

    private Vector3 ComputeInitialOrigin(Vector3 playerPosition, Direction direction)
    {
        Vector3 start = playerPosition + GetDirectionVector(direction) * InitialDoorDistance;
        float snappedX = Mathf.Round(start.x / CellSize) * CellSize;
        float snappedY = Mathf.Round(playerPosition.y / FloorHeight) * FloorHeight;
        float snappedZ = Mathf.Round(start.z / CellSize) * CellSize;
        return new Vector3(snappedX, snappedY, snappedZ);
    }

    private Bounds GetCellBounds(CellSpec spec)
    {
        Vector3 center = GetCellWorldPosition(spec.Coord);
        if (spec.Kind == CellKind.StairBase)
            return new Bounds(center + new Vector3(0f, FloorHeight * 0.5f, 0f), new Vector3(CellSize, FloorHeight * 2f, CellSize));

        return new Bounds(center + new Vector3(0f, FloorHeight * 0.5f, 0f), new Vector3(CellSize, FloorHeight, CellSize));
    }

    private GridCoord OffsetFromEntrance(GridCoord entranceCell, Direction forward, int forwardSteps, int lateralSteps)
    {
        GridCoord coord = entranceCell;
        Direction lateralDirection = lateralSteps >= 0 ? TurnRight(forward) : TurnLeft(forward);
        for (int i = 0; i < forwardSteps; i++)
            coord = coord.Step(forward);

        for (int i = 0; i < Mathf.Abs(lateralSteps); i++)
            coord = coord.Step(lateralDirection);

        return coord;
    }

    private Frontier CreateRoomExit(
        GridCoord entranceCell,
        Direction entryDirection,
        int depth,
        int leftCells,
        int rightCells,
        Direction exitDirection)
    {
        if (exitDirection == entryDirection)
        {
            int lateral = random.Next(-leftCells, rightCells + 1);
            GridCoord exitCell = OffsetFromEntrance(entranceCell, entryDirection, depth - 1, lateral);
            return new Frontier(exitCell, exitDirection);
        }

        if (exitDirection == TurnLeft(entryDirection))
        {
            int forward = depth > 1 ? random.Next(1, depth) : 0;
            GridCoord exitCell = OffsetFromEntrance(entranceCell, entryDirection, forward, -leftCells);
            return new Frontier(exitCell, exitDirection);
        }

        int selectedForward = depth > 1 ? random.Next(1, depth) : 0;
        GridCoord selectedExitCell = OffsetFromEntrance(entranceCell, entryDirection, selectedForward, rightCells);
        return new Frontier(selectedExitCell, exitDirection);
    }

    private Direction[] BuildRoomExitChoices(Direction entryDirection)
    {
        return new[]
        {
            entryDirection,
            TurnLeft(entryDirection),
            TurnRight(entryDirection)
        };
    }

    private Vector3 GetCellWorldPosition(GridCoord coord)
    {
        return worldGridOrigin + new Vector3(coord.X * CellSize, coord.Level * FloorHeight, coord.Z * CellSize);
    }

    private Vector3 GetBoundaryWorldPosition(GridCoord cell, Direction side)
    {
        return GetCellWorldPosition(cell) + GetDirectionVector(side) * HalfCellSize;
    }

    private static Direction GetClosestDirection(Vector3 worldForward)
    {
        Vector3 flattened = new Vector3(worldForward.x, 0f, worldForward.z);
        if (flattened.sqrMagnitude <= 0.0001f)
            return Direction.North;

        flattened.Normalize();
        float bestDot = float.NegativeInfinity;
        Direction bestDirection = Direction.North;
        for (int i = 0; i < AllDirections.Length; i++)
        {
            float dot = Vector3.Dot(flattened, GetDirectionVector(AllDirections[i]));
            if (dot <= bestDot)
                continue;

            bestDot = dot;
            bestDirection = AllDirections[i];
        }

        return bestDirection;
    }

    private static BoundaryKey GetCanonicalBoundary(GridCoord cell, Direction side)
    {
        GridCoord neighbor = cell.Step(side);
        if (neighbor.CompareTo(cell) < 0)
            return new BoundaryKey(neighbor, Opposite(side));

        return new BoundaryKey(cell, side);
    }

    private static Vector3 GetDirectionVector(Direction direction)
    {
        switch (direction)
        {
            case Direction.North:
                return Vector3.forward;
            case Direction.East:
                return Vector3.right;
            case Direction.South:
                return Vector3.back;
            default:
                return Vector3.left;
        }
    }

    private static float GetDirectionYaw(Direction direction)
    {
        switch (direction)
        {
            case Direction.North:
                return 0f;
            case Direction.East:
                return 90f;
            case Direction.South:
                return 180f;
            default:
                return 270f;
        }
    }

    private static Direction TurnLeft(Direction direction)
    {
        return (Direction)(((int)direction + 3) % 4);
    }

    private static Direction TurnRight(Direction direction)
    {
        return (Direction)(((int)direction + 1) % 4);
    }

    private static Direction Opposite(Direction direction)
    {
        return (Direction)(((int)direction + 2) % 4);
    }

    private void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private void Shuffle(Direction[] list)
    {
        for (int i = list.Length - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            Direction temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private enum Direction
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    private enum SegmentType
    {
        Room,
        Hallway,
        Staircase
    }

    private enum CellKind
    {
        Standard,
        StairBase,
        StairLanding
    }

    private enum BoundaryVisualType
    {
        None,
        Wall,
        WindowWall,
        DoorWall
    }

    private readonly struct Frontier
    {
        public Frontier(GridCoord sourceCell, Direction direction)
        {
            SourceCell = sourceCell;
            Direction = direction;
        }

        public GridCoord SourceCell { get; }

        public Direction Direction { get; }
    }

    private readonly struct BoundaryKey : IEquatable<BoundaryKey>
    {
        public BoundaryKey(GridCoord cell, Direction side)
        {
            Cell = cell;
            Side = side;
        }

        public GridCoord Cell { get; }

        public Direction Side { get; }

        public bool Equals(BoundaryKey other)
        {
            return Cell.Equals(other.Cell) && Side == other.Side;
        }

        public override bool Equals(object obj)
        {
            return obj is BoundaryKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Cell.GetHashCode() * 397) ^ (int)Side;
        }
    }

    private readonly struct GridCoord : IEquatable<GridCoord>, IComparable<GridCoord>
    {
        public static readonly GridCoord Zero = new GridCoord(0, 0, 0);

        public GridCoord(int x, int level, int z)
        {
            X = x;
            Level = level;
            Z = z;
        }

        public int X { get; }

        public int Level { get; }

        public int Z { get; }

        public GridCoord Step(Direction direction)
        {
            switch (direction)
            {
                case Direction.North:
                    return new GridCoord(X, Level, Z + 1);
                case Direction.East:
                    return new GridCoord(X + 1, Level, Z);
                case Direction.South:
                    return new GridCoord(X, Level, Z - 1);
                default:
                    return new GridCoord(X - 1, Level, Z);
            }
        }

        public GridCoord WithLevel(int newLevel)
        {
            return new GridCoord(X, newLevel, Z);
        }

        public int CompareTo(GridCoord other)
        {
            if (Level != other.Level)
                return Level.CompareTo(other.Level);

            if (Z != other.Z)
                return Z.CompareTo(other.Z);

            return X.CompareTo(other.X);
        }

        public bool Equals(GridCoord other)
        {
            return X == other.X && Level == other.Level && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = X;
            hash = (hash * 397) ^ Level;
            hash = (hash * 397) ^ Z;
            return hash;
        }
    }

    private readonly struct CellSpec
    {
        public CellSpec(GridCoord coord, CellKind kind, Direction primaryDirection)
        {
            Coord = coord;
            Kind = kind;
            PrimaryDirection = primaryDirection;
        }

        public GridCoord Coord { get; }

        public CellKind Kind { get; }

        public Direction PrimaryDirection { get; }
    }

    private sealed class SegmentBlueprint
    {
        private readonly HashSet<GridCoord> cellLookup = new HashSet<GridCoord>();
        private readonly List<CellSpec> cells = new List<CellSpec>();
        private readonly List<BoundaryKey> openBoundaries = new List<BoundaryKey>();

        public SegmentType Type { get; set; }

        public Frontier ExitFrontier { get; set; }

        public GridCoord EntranceDoorCell { get; set; }

        public Direction EntranceDoorSide { get; set; }

        public bool UsesOpenEntrance { get; set; }

        public IReadOnlyList<CellSpec> Cells => cells;

        public int CellCount => cells.Count;

        public IReadOnlyList<BoundaryKey> OpenBoundaries => openBoundaries;

        public int OpenBoundaryCount => openBoundaries.Count;

        public void AddCell(CellSpec spec)
        {
            if (cellLookup.Add(spec.Coord))
                cells.Add(spec);
        }

        public bool Contains(GridCoord coord)
        {
            return cellLookup.Contains(coord);
        }

        public void AddOpenBoundary(BoundaryKey boundary)
        {
            if (!openBoundaries.Contains(boundary))
                openBoundaries.Add(boundary);
        }
    }

    private sealed class SegmentData
    {
        public int Index;
        public SegmentType Type;
        public Frontier ExitFrontier;
        public Bounds WorldBounds;
        public Transform Root;
        public readonly List<GridCoord> Cells = new List<GridCoord>();
    }

    private sealed class CellData
    {
        public GridCoord Coord;
        public CellKind Kind;
        public Direction PrimaryDirection;
        public GameObject Root;
        public int SegmentIndex;

        public bool SuppressesBoundary(Direction side)
        {
            if (Kind == CellKind.StairBase)
                return true;

            return Kind == CellKind.StairLanding && side == ProceduralHouseGenerator.Opposite(PrimaryDirection);
        }
    }
}
