using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ProceduralHouseGenerator : MonoBehaviour
{
    private const string TargetSceneName = "Dungeon Test";
    private const string ResourceFolder = "Dungeon Parts/";
    private const float CellSize = 2.5f;
    private const float HalfCellSize = CellSize * 0.5f;
    private const float FloorHeight = 2.5f;
    private const float InitialDoorDistance = CellSize * 4.5f;
    private const float BoundaryDepth = 0.2f;
    private const float VisibilityVerticalOffset = 1.2f;
    private const float VisibilityLateralOffset = 0.45f;
    private const float VisibilityRayTolerance = 0.05f;
    private const float MinimumHiddenGenerationDistance = CellSize * 2.25f;
    private const float EntryRoomActivationPadding = 0.2f;
    private const int RequiredMainSegmentsAhead = 3;
    private const int MaxGenerationAttemptsPerType = 10;
    private const int BranchSegmentsPerMainSegment = 1;
    private const int MaxBranchDepth = 2;
    private const int MaxPendingFrontiers = 10;
    private const int RetainedMainSegmentsBehind = 0;
    private const int MaxSegmentsRecycledPerUpdate = 4;
    private const float BranchRecycleDistance = CellSize * 4.5f;
    private const int MaxLiveCells = 100;
    private const int WindowViewDepth = 2;
    private const int WindowViewHalfWidth = 1;
    private const bool UseExteriorWindows = true;
    private const float RoomExtraExitChance = 0.22f;
    private const float HallwayTurnChance = 0.62f;
    private const float HallwaySecondTurnChance = 0.18f;
    private const float HallwayBranchChance = 0.18f;
    private const float LoopDoorChance = 0f;
    private const float RoomWindowChance = 0.3f;
    private const float HallWindowChance = 0.22f;

    private static readonly Direction[] AllDirections =
    {
        Direction.North,
        Direction.East,
        Direction.South,
        Direction.West
    };

    private readonly Dictionary<GridCoord, CellData> cells = new Dictionary<GridCoord, CellData>();
    private readonly Dictionary<BoundaryKey, GameObject> boundaryObjects = new Dictionary<BoundaryKey, GameObject>();
    private readonly Dictionary<BoundaryKey, BoundaryVisualType> boundaryVisuals = new Dictionary<BoundaryKey, BoundaryVisualType>();
    private readonly HashSet<BoundaryKey> doorBoundaries = new HashSet<BoundaryKey>();
    private readonly HashSet<BoundaryKey> openBoundaries = new HashSet<BoundaryKey>();
    private readonly HashSet<BoundaryKey> boundaryUpdateSet = new HashSet<BoundaryKey>();
    private readonly List<SegmentData> segments = new List<SegmentData>();
    private readonly List<int> mainPathSegmentIndices = new List<int>();
    private readonly List<PendingFrontier> pendingFrontiers = new List<PendingFrontier>();

    private GameObject floorPrefab;
    private GameObject wallPrefab;
    private GameObject ceilingPrefab;
    private GameObject doorWallPrefab;
    private GameObject windowWallPrefab;

    private System.Random random;
    private Transform playerTransform;
    private Camera playerCamera;
    private Vector3 worldGridOrigin;
    private Direction initialDirection;
    private Frontier mainTailFrontier;
    private Bounds entryRoomBounds;
    private BoundaryKey entryExitBoundary;
    private BoundaryKey entryFrontBoundary;
    private int sessionSeed;
    private int currentMainPathIndex;
    private int branchSegmentCount;
    private bool initialized;
    private bool generationStarted;
    private bool entryFrontConvertedToWindow;

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

        ConfigureTestingLighting();
        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        ResolvePlayerReferences();
        if (playerTransform == null || playerCamera == null)
            return;

        if (!generationStarted)
        {
            if (CanBeginGeneration())
                BeginGeneration();

            return;
        }

        TryConvertEntryFrontDoorToWindow();
        currentMainPathIndex = FindCurrentMainPathIndex(playerTransform.position);
        RecycleStaleSegments();
        PrunePendingFrontiers();
        currentMainPathIndex = FindCurrentMainPathIndex(playerTransform.position);
        EnsureGenerationBuffers(false);
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
        BuildEntryAntechamber();
        return true;
    }

    private bool LoadPrefabs()
    {
        floorPrefab = Resources.Load<GameObject>(ResourceFolder + "Floor");
        wallPrefab = Resources.Load<GameObject>(ResourceFolder + "Wall");
        ceilingPrefab = Resources.Load<GameObject>(ResourceFolder + "Ceiling");
        doorWallPrefab = Resources.Load<GameObject>(ResourceFolder + "DoorWall");
        windowWallPrefab = Resources.Load<GameObject>(ResourceFolder + "WindowWall");

        return floorPrefab != null &&
               wallPrefab != null &&
               ceilingPrefab != null &&
               doorWallPrefab != null &&
               windowWallPrefab != null;
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

    private void ConfigureTestingLighting()
    {
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.sun = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.shadowDistance = 0f;
        QualitySettings.pixelLightCount = 0;

        Light[] sceneLights = FindObjectsOfType<Light>(true);
        for (int i = 0; i < sceneLights.Length; i++)
        {
            sceneLights[i].shadows = LightShadows.None;
            sceneLights[i].enabled = false;
        }

        DynamicGI.UpdateEnvironment();
    }

    private void BuildEntryAntechamber()
    {
        cells.Clear();
        boundaryObjects.Clear();
        boundaryVisuals.Clear();
        doorBoundaries.Clear();
        openBoundaries.Clear();
        boundaryUpdateSet.Clear();
        segments.Clear();
        mainPathSegmentIndices.Clear();
        pendingFrontiers.Clear();
        generationStarted = false;
        branchSegmentCount = 0;
        currentMainPathIndex = 0;
        entryFrontConvertedToWindow = false;

        SegmentBlueprint blueprint = new SegmentBlueprint(SegmentType.Entry);
        GridCoord firstCell = GridCoord.Zero;
        GridCoord secondCell = firstCell.Step(initialDirection);
        blueprint.AddCell(new CellSpec(firstCell, initialDirection));
        blueprint.AddCell(new CellSpec(secondCell, initialDirection));
        blueprint.ExitFrontier = new Frontier(secondCell, initialDirection);

        SegmentData entrySegment = CommitBlueprint(blueprint);
        entrySegment.IsActive = true;
        entrySegment.IsMainPath = true;
        segments.Add(entrySegment);
        mainPathSegmentIndices.Add(entrySegment.Index);
        mainTailFrontier = blueprint.ExitFrontier;
        entryRoomBounds = entrySegment.WorldBounds;
        entryFrontBoundary = GetCanonicalBoundary(firstCell, Opposite(initialDirection));
        entryExitBoundary = GetCanonicalBoundary(secondCell, initialDirection);

        doorBoundaries.Add(entryFrontBoundary);
        doorBoundaries.Add(entryExitBoundary);
        RefreshBoundariesForSegment(entrySegment.Cells, null, blueprint);
    }

    private bool CanBeginGeneration()
    {
        if (playerTransform == null)
            return false;

        Bounds expanded = entryRoomBounds;
        expanded.Expand(new Vector3(EntryRoomActivationPadding * 2f, 1.5f, EntryRoomActivationPadding * 2f));
        return expanded.Contains(playerTransform.position);
    }

    private void BeginGeneration()
    {
        if (generationStarted)
            return;

        generationStarted = true;
        EnsureGenerationBuffers(false);
    }

    private void TryConvertEntryFrontDoorToWindow()
    {
        if (entryFrontConvertedToWindow || !generationStarted)
            return;

        if (!IsBoundaryHidden(entryFrontBoundary))
            return;

        doorBoundaries.Remove(entryFrontBoundary);
        entryFrontConvertedToWindow = true;
        UpdateBoundary(entryFrontBoundary, ResolveBoundaryVisual(entryFrontBoundary));
    }

    private void EnsureGenerationBuffers(bool ignoreVisibility)
    {
        int mainAttempts = 0;
        while (mainPathSegmentIndices.Count - 1 - currentMainPathIndex < RequiredMainSegmentsAhead &&
               mainAttempts < RequiredMainSegmentsAhead + 3)
        {
            if (!TryExtendMainPath(ignoreVisibility))
                break;

            mainAttempts++;
        }

        int targetBranchSegments = Mathf.Max(1, (mainPathSegmentIndices.Count - 1) * BranchSegmentsPerMainSegment);
        int branchAttempts = 0;
        while ((branchSegmentCount < targetBranchSegments || HasPendingExteriorDoorFrontier()) &&
               pendingFrontiers.Count > 0 &&
               branchAttempts < targetBranchSegments + 4)
        {
            if (!TryExtendBranch(ignoreVisibility))
                break;

            branchAttempts++;
        }
    }

    private bool HasPendingExteriorDoorFrontier()
    {
        for (int i = 0; i < pendingFrontiers.Count; i++)
        {
            BoundaryKey boundary = GetCanonicalBoundary(pendingFrontiers[i].Frontier.SourceCell, pendingFrontiers[i].Frontier.Direction);
            if (doorBoundaries.Contains(boundary) && IsExteriorBoundary(boundary))
                return true;
        }

        return false;
    }

    private bool ShouldPreserveDoorFrontier(Frontier frontier)
    {
        return GetCanonicalBoundary(frontier.SourceCell, frontier.Direction).Equals(entryExitBoundary);
    }

    private bool TryExtendMainPath(bool ignoreVisibility)
    {
        BoundaryKey boundary = GetCanonicalBoundary(mainTailFrontier.SourceCell, mainTailFrontier.Direction);
        if (!CanGenerateAtBoundary(boundary, ignoreVisibility))
            return false;

        HiddenBuildResult buildResult = TryBuildHiddenNextSegment(mainTailFrontier, ignoreVisibility, out SegmentBlueprint blueprint);
        if (buildResult != HiddenBuildResult.Success)
        {
            if (buildResult == HiddenBuildResult.NoBlueprint && !ShouldPreserveDoorFrontier(mainTailFrontier))
            {
                SealFrontier(mainTailFrontier);
                return TryPromoteBranchToMainTail(ignoreVisibility);
            }

            return false;
        }

        SegmentData segment = CommitSegment(mainTailFrontier, blueprint, countAsBranch: false);
        mainPathSegmentIndices.Add(segment.Index);
        mainTailFrontier = blueprint.ExitFrontier;
        EnqueueBranchFrontiers(blueprint, MaxBranchDepth);
        return true;
    }

    private bool TryExtendBranch(bool ignoreVisibility)
    {
        if (pendingFrontiers.Count == 0)
            return false;

        for (int index = 0; index < pendingFrontiers.Count; index++)
        {
            PendingFrontier pending = pendingFrontiers[index];
            BoundaryKey boundary = GetCanonicalBoundary(pending.Frontier.SourceCell, pending.Frontier.Direction);
            if (!CanGenerateAtBoundary(boundary, ignoreVisibility))
                continue;

            HiddenBuildResult buildResult = TryBuildHiddenNextSegment(pending.Frontier, ignoreVisibility, out SegmentBlueprint blueprint);
            if (buildResult != HiddenBuildResult.Success)
            {
                if (buildResult == HiddenBuildResult.NoBlueprint)
                {
                    pendingFrontiers.RemoveAt(index);
                    SealFrontier(pending.Frontier);
                    return true;
                }

                continue;
            }

            pendingFrontiers.RemoveAt(index);
            CommitSegment(pending.Frontier, blueprint, countAsBranch: true);

            int nextDepth = pending.RemainingDepth - 1;
            if (nextDepth > 0)
            {
                EnqueueFrontier(blueprint.ExitFrontier, nextDepth);
                EnqueueBranchFrontiers(blueprint, nextDepth);
            }

            return true;
        }

        return false;
    }

    private bool TryPromoteBranchToMainTail(bool ignoreVisibility)
    {
        if (pendingFrontiers.Count == 0)
            return false;

        int selectedIndex = -1;
        for (int i = 0; i < pendingFrontiers.Count; i++)
        {
            BoundaryKey boundary = GetCanonicalBoundary(pendingFrontiers[i].Frontier.SourceCell, pendingFrontiers[i].Frontier.Direction);
            if (ignoreVisibility || CanGenerateAtBoundary(boundary, false))
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex < 0)
            selectedIndex = 0;

        mainTailFrontier = pendingFrontiers[selectedIndex].Frontier;
        pendingFrontiers.RemoveAt(selectedIndex);
        return true;
    }

    private SegmentData CommitSegment(Frontier entranceFrontier, SegmentBlueprint blueprint, bool countAsBranch)
    {
        doorBoundaries.Add(GetCanonicalBoundary(entranceFrontier.SourceCell, entranceFrontier.Direction));

        SegmentData segment = CommitBlueprint(blueprint);
        segments.Add(segment);
        segment.IsActive = true;
        segment.IsMainPath = !countAsBranch;

        if (countAsBranch)
            branchSegmentCount++;

        RefreshBoundariesForSegment(segment.Cells, entranceFrontier, blueprint);
        return segment;
    }

    private HiddenBuildResult TryBuildHiddenNextSegment(Frontier frontier, bool ignoreVisibility, out SegmentBlueprint blueprint)
    {
        const int hiddenAttempts = 4;
        bool foundVisibleCandidate = false;
        bool foundDeferredCandidate = false;
        for (int attempt = 0; attempt < hiddenAttempts; attempt++)
        {
            if (!TryBuildNextSegment(frontier, out blueprint))
                return foundVisibleCandidate || foundDeferredCandidate
                    ? HiddenBuildResult.Deferred
                    : HiddenBuildResult.NoBlueprint;

            if (WouldExceedLiveCellCap(blueprint))
            {
                foundDeferredCandidate = true;
                continue;
            }

            if (ignoreVisibility || IsBlueprintHidden(frontier, blueprint))
                return HiddenBuildResult.Success;

            foundVisibleCandidate = true;
        }

        blueprint = default;
        return foundVisibleCandidate || foundDeferredCandidate
            ? HiddenBuildResult.Deferred
            : HiddenBuildResult.NoBlueprint;
    }

    private bool TryBuildNextSegment(Frontier frontier, out SegmentBlueprint blueprint)
    {
        GridCoord entranceCell = frontier.SourceCell.Step(frontier.Direction);
        SegmentType sourceType = GetSegmentTypeAt(frontier.SourceCell);

        if (sourceType == SegmentType.Entry || sourceType == SegmentType.Room)
        {
            for (int attempt = 0; attempt < MaxGenerationAttemptsPerType; attempt++)
            {
                if (TryBuildHallwayBlueprint(entranceCell, frontier.Direction, out blueprint))
                    return true;
            }

            blueprint = default;
            return false;
        }

        SegmentType firstType = random.NextDouble() < 0.62d ? SegmentType.Room : SegmentType.Hallway;
        SegmentType secondType = firstType == SegmentType.Room ? SegmentType.Hallway : SegmentType.Room;

        for (int attempt = 0; attempt < MaxGenerationAttemptsPerType; attempt++)
        {
            if (TryBuildSegment(frontier.Direction, entranceCell, firstType, out blueprint))
                return true;
        }

        for (int attempt = 0; attempt < MaxGenerationAttemptsPerType; attempt++)
        {
            if (TryBuildSegment(frontier.Direction, entranceCell, secondType, out blueprint))
                return true;
        }

        blueprint = default;
        return false;
    }

    private bool TryBuildSegment(Direction entryDirection, GridCoord entranceCell, SegmentType type, out SegmentBlueprint blueprint)
    {
        switch (type)
        {
            case SegmentType.Room:
                return TryBuildRoomBlueprint(entranceCell, entryDirection, out blueprint);
            default:
                return TryBuildHallwayBlueprint(entranceCell, entryDirection, out blueprint);
        }
    }

    private bool TryBuildRoomBlueprint(GridCoord entranceCell, Direction entryDirection, out SegmentBlueprint blueprint)
    {
        int width = RollRoomDimension();
        int depth = RollRoomDimension();
        int leftCells = random.Next(0, width);
        int rightCells = width - 1 - leftCells;

        SegmentBlueprint candidate = new SegmentBlueprint(SegmentType.Room);
        for (int forward = 0; forward < depth; forward++)
        {
            for (int lateral = -leftCells; lateral <= rightCells; lateral++)
            {
                GridCoord coord = OffsetFromEntrance(entranceCell, entryDirection, forward, lateral);
                candidate.AddCell(new CellSpec(coord, entryDirection));
            }
        }

        Direction[] exitDirections =
        {
            entryDirection,
            TurnLeft(entryDirection),
            TurnRight(entryDirection)
        };

        Shuffle(exitDirections);
        for (int i = 0; i < exitDirections.Length; i++)
        {
            Frontier exitFrontier = CreateRoomExit(entranceCell, entryDirection, depth, leftCells, rightCells, exitDirections[i]);
            candidate.ExitFrontier = exitFrontier;
            candidate.ClearBranchFrontiers();
            TryAddRoomBranches(candidate, entranceCell, entryDirection, depth, leftCells, rightCells, exitFrontier.Direction);

            if (IsBlueprintValid(candidate))
            {
                blueprint = candidate;
                return true;
            }
        }

        blueprint = default;
        return false;
    }

    private void TryAddRoomBranches(
        SegmentBlueprint blueprint,
        GridCoord entranceCell,
        Direction entryDirection,
        int depth,
        int leftCells,
        int rightCells,
        Direction mainExitDirection)
    {
        List<Direction> candidates = new List<Direction>
        {
            entryDirection,
            TurnLeft(entryDirection),
            TurnRight(entryDirection)
        };

        candidates.Remove(mainExitDirection);
        Shuffle(candidates);

        int targetBranchCount = random.NextDouble() < RoomExtraExitChance ? 2 : 1;
        for (int i = 0; i < candidates.Count && blueprint.BranchFrontierCount < targetBranchCount; i++)
        {
            Frontier branch = CreateRoomExit(entranceCell, entryDirection, depth, leftCells, rightCells, candidates[i]);
            if (FrontierEquals(branch, blueprint.ExitFrontier))
                continue;

            blueprint.AddBranchFrontier(branch);
        }
    }

    private bool TryBuildHallwayBlueprint(GridCoord entranceCell, Direction entryDirection, out SegmentBlueprint blueprint)
    {
        SegmentBlueprint candidate = new SegmentBlueprint(SegmentType.Hallway);
        GridCoord current = entranceCell;
        Direction currentDirection = entryDirection;
        candidate.AddCell(new CellSpec(current, currentDirection));

        int firstLegLength = RollHallLength();
        for (int i = 0; i < firstLegLength; i++)
        {
            current = current.Step(currentDirection);
            candidate.AddCell(new CellSpec(current, currentDirection));
        }

        if (random.NextDouble() < HallwayTurnChance)
        {
            currentDirection = random.Next(2) == 0
                ? TurnLeft(currentDirection)
                : TurnRight(currentDirection);

            int turnLength = random.Next(1, 3);
            for (int i = 0; i < turnLength; i++)
            {
                current = current.Step(currentDirection);
                candidate.AddCell(new CellSpec(current, currentDirection));
            }

            if (random.NextDouble() < HallwaySecondTurnChance)
            {
                currentDirection = random.Next(2) == 0
                    ? TurnLeft(currentDirection)
                    : TurnRight(currentDirection);

                int secondTurnLength = random.Next(1, 3);
                for (int i = 0; i < secondTurnLength; i++)
                {
                    current = current.Step(currentDirection);
                    candidate.AddCell(new CellSpec(current, currentDirection));
                }
            }
        }
        else if (random.NextDouble() < 0.5d)
        {
            current = current.Step(currentDirection);
            candidate.AddCell(new CellSpec(current, currentDirection));
        }

        candidate.ExitFrontier = new Frontier(current, currentDirection);
        candidate.ClearBranchFrontiers();
        TryAddHallwayBranches(candidate);

        if (IsBlueprintValid(candidate))
        {
            blueprint = candidate;
            return true;
        }

        blueprint = default;
        return false;
    }

    private void TryAddHallwayBranches(SegmentBlueprint blueprint)
    {
        if (blueprint.CellCount < 2 || random.NextDouble() > HallwayBranchChance)
            return;

        List<int> interiorIndices = new List<int>();
        for (int i = 1; i < blueprint.CellCount - 1; i++)
            interiorIndices.Add(i);

        if (interiorIndices.Count == 0)
            interiorIndices.Add(blueprint.CellCount - 1);

        Shuffle(interiorIndices);
        int targetBranchCount = random.NextDouble() < 0.65d ? 2 : 1;
        for (int i = 0; i < interiorIndices.Count && blueprint.BranchFrontierCount < targetBranchCount; i++)
        {
            CellSpec cell = blueprint.Cells[interiorIndices[i]];
            Direction[] branchDirections =
            {
                TurnLeft(cell.PrimaryDirection),
                TurnRight(cell.PrimaryDirection)
            };

            Shuffle(branchDirections);
            for (int directionIndex = 0; directionIndex < branchDirections.Length; directionIndex++)
            {
                Frontier branch = new Frontier(cell.Coord, branchDirections[directionIndex]);
                if (FrontierEquals(branch, blueprint.ExitFrontier))
                    continue;

                blueprint.AddBranchFrontier(branch);
                break;
            }
        }
    }

    private bool IsBlueprintValid(SegmentBlueprint blueprint)
    {
        if (blueprint.CellCount == 0 || !blueprint.Contains(blueprint.ExitFrontier.SourceCell))
            return false;

        for (int i = 0; i < blueprint.CellCount; i++)
        {
            if (cells.ContainsKey(blueprint.Cells[i].Coord))
                return false;

            if (WouldConsumeExistingWindowView(blueprint.Cells[i].Coord))
                return false;
        }

        if (!IsFrontierAvailable(blueprint, blueprint.ExitFrontier))
            return false;

        for (int i = 0; i < blueprint.BranchFrontierCount; i++)
        {
            if (!IsFrontierAvailable(blueprint, blueprint.BranchFrontiers[i]))
                return false;
        }

        return true;
    }

    private bool IsFrontierAvailable(SegmentBlueprint blueprint, Frontier frontier)
    {
        if (!blueprint.Contains(frontier.SourceCell))
            return false;

        GridCoord nextCell = frontier.SourceCell.Step(frontier.Direction);
        if (cells.ContainsKey(nextCell) || blueprint.Contains(nextCell))
            return false;

        return true;
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
        Bounds bounds = default;
        int minX = int.MaxValue;
        int maxX = int.MinValue;
        int minZ = int.MaxValue;
        int maxZ = int.MinValue;
        for (int i = 0; i < blueprint.CellCount; i++)
        {
            CellSpec spec = blueprint.Cells[i];
            CellData cell = CreateCell(segment, spec);
            cells.Add(spec.Coord, cell);
            segment.Cells.Add(spec.Coord);
            minX = Mathf.Min(minX, spec.Coord.X);
            maxX = Mathf.Max(maxX, spec.Coord.X);
            minZ = Mathf.Min(minZ, spec.Coord.Z);
            maxZ = Mathf.Max(maxZ, spec.Coord.Z);

            Bounds cellBounds = GetCellBounds(spec.Coord);
            if (!hasBounds)
            {
                bounds = cellBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(cellBounds);
            }
        }

        AddInternalOpenBoundaries(blueprint);
        AddLoopDoors(blueprint);
        segment.WorldBounds = bounds;
        segment.ExitFrontier = blueprint.ExitFrontier;
        segment.MinX = minX;
        segment.MaxX = maxX;
        segment.MinZ = minZ;
        segment.MaxZ = maxZ;
        segment.WindowPattern = DetermineWindowPattern(blueprint.Type);
        return segment;
    }

    private CellData CreateCell(SegmentData segment, CellSpec spec)
    {
        Vector3 worldPosition = GetCellWorldPosition(spec.Coord);
        GameObject cellRoot = new GameObject($"Cell_{spec.Coord.X}_{spec.Coord.Z}");
        cellRoot.transform.SetParent(segment.Root, false);
        cellRoot.transform.position = worldPosition;

        Instantiate(floorPrefab, worldPosition, Quaternion.identity, cellRoot.transform);
        GameObject ceilingInstance = Instantiate(
            ceilingPrefab,
            worldPosition + Vector3.up * FloorHeight,
            Quaternion.identity,
            cellRoot.transform);
        DisableAllColliders(ceilingInstance);

        return new CellData
        {
            Coord = spec.Coord,
            Root = cellRoot,
            SegmentIndex = segment.Index,
            PrimaryDirection = spec.PrimaryDirection
        };
    }

    private static void DisableAllColliders(GameObject root)
    {
        if (root == null)
            return;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    private void AddInternalOpenBoundaries(SegmentBlueprint blueprint)
    {
        for (int i = 0; i < blueprint.CellCount; i++)
        {
            GridCoord cell = blueprint.Cells[i].Coord;
            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
            {
                Direction direction = AllDirections[directionIndex];
                if (blueprint.Contains(cell.Step(direction)))
                    openBoundaries.Add(GetCanonicalBoundary(cell, direction));
            }
        }
    }

    private void AddLoopDoors(SegmentBlueprint blueprint)
    {
        for (int i = 0; i < blueprint.CellCount; i++)
        {
            GridCoord cell = blueprint.Cells[i].Coord;
            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
            {
                Direction direction = AllDirections[directionIndex];
                GridCoord neighbor = cell.Step(direction);
                if (!cells.ContainsKey(neighbor) || blueprint.Contains(neighbor))
                    continue;

                SegmentType neighborType = GetSegmentTypeAt(neighbor);
                if (blueprint.Type == SegmentType.Room && neighborType == SegmentType.Room)
                    continue;

                BoundaryKey boundary = GetCanonicalBoundary(cell, direction);
                if (openBoundaries.Contains(boundary) || doorBoundaries.Contains(boundary))
                    continue;

                if (random.NextDouble() <= LoopDoorChance)
                    doorBoundaries.Add(boundary);
            }
        }
    }

    private void RefreshBoundariesForSegment(IReadOnlyList<GridCoord> coords, Frontier? entranceFrontier, SegmentBlueprint blueprint)
    {
        boundaryUpdateSet.Clear();

        if (entranceFrontier.HasValue)
            boundaryUpdateSet.Add(GetCanonicalBoundary(entranceFrontier.Value.SourceCell, entranceFrontier.Value.Direction));

        for (int i = 0; i < coords.Count; i++)
        {
            GridCoord cell = coords[i];
            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
                boundaryUpdateSet.Add(GetCanonicalBoundary(cell, AllDirections[directionIndex]));
        }

        boundaryUpdateSet.Add(GetCanonicalBoundary(blueprint.ExitFrontier.SourceCell, blueprint.ExitFrontier.Direction));
        for (int i = 0; i < blueprint.BranchFrontierCount; i++)
            boundaryUpdateSet.Add(GetCanonicalBoundary(blueprint.BranchFrontiers[i].SourceCell, blueprint.BranchFrontiers[i].Direction));

        foreach (BoundaryKey boundary in boundaryUpdateSet)
            UpdateBoundary(boundary, ResolveBoundaryVisual(boundary));
    }

    private void SealFrontier(Frontier frontier)
    {
        BoundaryKey boundary = GetCanonicalBoundary(frontier.SourceCell, frontier.Direction);
        doorBoundaries.Remove(boundary);
        UpdateBoundary(boundary, ResolveBoundaryVisual(boundary));
    }

    private void EnqueueBranchFrontiers(SegmentBlueprint blueprint, int remainingDepth)
    {
        if (remainingDepth <= 0)
            return;

        for (int i = 0; i < blueprint.BranchFrontierCount; i++)
            EnqueueFrontier(blueprint.BranchFrontiers[i], remainingDepth);
    }

    private void EnqueueFrontier(Frontier frontier, int remainingDepth, bool prioritizeFront = false)
    {
        if (remainingDepth <= 0)
            return;

        if (FrontierEquals(frontier, mainTailFrontier))
            return;

        for (int i = 0; i < pendingFrontiers.Count; i++)
        {
            if (FrontierEquals(pendingFrontiers[i].Frontier, frontier))
            {
                PendingFrontier updated = pendingFrontiers[i];
                if (remainingDepth > updated.RemainingDepth)
                    updated = new PendingFrontier(frontier, remainingDepth);

                pendingFrontiers.RemoveAt(i);
                if (prioritizeFront)
                    pendingFrontiers.Insert(0, updated);
                else
                    pendingFrontiers.Insert(Mathf.Min(i, pendingFrontiers.Count), updated);
                return;
            }
        }

        if (pendingFrontiers.Count >= MaxPendingFrontiers)
        {
            if (!prioritizeFront)
                return;

            pendingFrontiers.RemoveAt(pendingFrontiers.Count - 1);
        }

        PendingFrontier pendingFrontier = new PendingFrontier(frontier, remainingDepth);
        if (prioritizeFront)
            pendingFrontiers.Insert(0, pendingFrontier);
        else
            pendingFrontiers.Add(pendingFrontier);
    }

    private bool CanGenerateAtBoundary(BoundaryKey boundary, bool ignoreVisibility)
    {
        if (ignoreVisibility)
            return true;

        if (CanGenerateBehindDoor(boundary))
            return true;

        Transform referenceTransform = playerCamera != null ? playerCamera.transform : playerTransform;
        if (referenceTransform != null)
        {
            Vector3 boundaryPosition = GetBoundaryWorldPosition(boundary.Cell, boundary.Side);
            Vector3 referencePosition = referenceTransform.position;
            Vector2 horizontalDelta = new Vector2(boundaryPosition.x - referencePosition.x, boundaryPosition.z - referencePosition.z);
            if (horizontalDelta.magnitude < MinimumHiddenGenerationDistance)
                return false;
        }

        return IsBoundaryHidden(boundary);
    }

    private int FindCurrentMainPathIndex(Vector3 playerPosition)
    {
        for (int i = mainPathSegmentIndices.Count - 1; i >= 0; i--)
        {
            SegmentData segment = segments[mainPathSegmentIndices[i]];
            Bounds expanded = segment.WorldBounds;
            expanded.Expand(new Vector3(0.2f, 1f, 0.2f));
            if (expanded.Contains(playerPosition))
                return i;
        }

        int closestIndex = currentMainPathIndex;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < mainPathSegmentIndices.Count; i++)
        {
            Vector3 point = segments[mainPathSegmentIndices[i]].WorldBounds.ClosestPoint(playerPosition);
            float sqrDistance = (point - playerPosition).sqrMagnitude;
            if (sqrDistance >= closestDistance)
                continue;

            closestDistance = sqrDistance;
            closestIndex = i;
        }

        return closestIndex;
    }

    private void UpdateBoundary(BoundaryKey key, BoundaryVisualType type)
    {
        if (type == BoundaryVisualType.None)
        {
            if (boundaryObjects.TryGetValue(key, out GameObject existingNone) && existingNone != null)
                Destroy(existingNone);

            boundaryObjects.Remove(key);
            boundaryVisuals.Remove(key);
            return;
        }

        if (boundaryObjects.TryGetValue(key, out GameObject existing) &&
            existing != null &&
            boundaryVisuals.TryGetValue(key, out BoundaryVisualType existingType) &&
            existingType == type)
        {
            return;
        }

        if (existing != null)
            Destroy(existing);

        GameObject prefab = GetBoundaryPrefab(type);
        if (prefab == null)
        {
            boundaryObjects.Remove(key);
            boundaryVisuals.Remove(key);
            return;
        }

        Vector3 position = GetBoundaryWorldPosition(key.Cell, key.Side);
        Quaternion rotation = Quaternion.Euler(0f, GetDirectionYaw(key.Side), 0f);
        GameObject boundaryObject = Instantiate(prefab, position, rotation, transform);
        boundaryObject.name = $"{type}_{key.Cell.X}_{key.Cell.Z}_{key.Side}";
        boundaryObjects[key] = boundaryObject;
        boundaryVisuals[key] = type;
    }

    private BoundaryVisualType ResolveBoundaryVisual(BoundaryKey key)
    {
        bool hasA = cells.TryGetValue(key.Cell, out CellData cellA);
        GridCoord neighbor = key.Cell.Step(key.Side);
        bool hasB = cells.TryGetValue(neighbor, out CellData cellB);

        if (!hasA && !hasB)
            return BoundaryVisualType.None;

        if (openBoundaries.Contains(key))
            return BoundaryVisualType.None;

        if (doorBoundaries.Contains(key))
            return BoundaryVisualType.DoorWall;

        if (entryFrontConvertedToWindow &&
            key.Equals(entryFrontBoundary) &&
            (hasA ^ hasB))
        {
            return BoundaryVisualType.WindowWall;
        }

        if (hasA && hasB)
            return BoundaryVisualType.Wall;

        CellData exteriorCell = hasA ? cellA : cellB;
        Direction exteriorSide = hasA ? key.Side : Opposite(key.Side);
        if (ShouldUseWindow(exteriorCell, exteriorSide))
            return BoundaryVisualType.WindowWall;

        return BoundaryVisualType.Wall;
    }

    private bool ShouldUseWindow(CellData cell, Direction side)
    {
        if (!UseExteriorWindows || cell.SegmentIndex < 0 || cell.SegmentIndex >= segments.Count)
            return false;

        SegmentData segment = segments[cell.SegmentIndex];
        if (segment.WindowPattern == WindowPattern.None)
            return false;

        if (!IsExteriorBoundary(GetCanonicalBoundary(cell.Coord, side)))
            return false;

        if (!HasOutdoorViewClearance(cell.Coord, side))
            return false;

        switch (segment.WindowPattern)
        {
            case WindowPattern.RoomEastWest:
                return IsSymmetricRoomWindow(cell, segment, side, Direction.West, Direction.East);
            case WindowPattern.RoomNorthSouth:
                return IsSymmetricRoomWindow(cell, segment, side, Direction.South, Direction.North);
            case WindowPattern.HallAcross:
                return IsSymmetricHallWindow(cell, side);
            default:
                return false;
        }
    }

    private bool IsBoundaryHidden(BoundaryKey boundary)
    {
        Plane[] cameraPlanes = GetCurrentCameraPlanes();
        if (cameraPlanes == null)
            return false;

        return !GeometryUtility.TestPlanesAABB(cameraPlanes, GetBoundaryBounds(boundary));
    }

    private bool IsBlueprintHidden(Frontier entranceFrontier, SegmentBlueprint blueprint)
    {
        Plane[] cameraPlanes = GetCurrentCameraPlanes();
        if (cameraPlanes == null)
            return true;

        BoundaryKey entranceBoundary = GetCanonicalBoundary(entranceFrontier.SourceCell, entranceFrontier.Direction);
        GameObject coverObject = GetGenerationCoverObject(entranceBoundary);

        for (int i = 0; i < blueprint.CellCount; i++)
        {
            Vector3[] samplePoints = GetCellVisibilitySamplePoints(blueprint.Cells[i].Coord);
            for (int sampleIndex = 0; sampleIndex < samplePoints.Length; sampleIndex++)
            {
                if (IsPointExposedToCamera(samplePoints[sampleIndex], cameraPlanes, coverObject))
                    return false;
            }
        }

        HashSet<BoundaryKey> sampledBoundaries = new HashSet<BoundaryKey>();
        for (int i = 0; i < blueprint.CellCount; i++)
        {
            GridCoord cell = blueprint.Cells[i].Coord;
            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
            {
                Direction direction = AllDirections[directionIndex];
                GridCoord neighbor = cell.Step(direction);
                if (blueprint.Contains(neighbor))
                    continue;

                BoundaryKey boundary = GetCanonicalBoundary(cell, direction);
                if (!sampledBoundaries.Add(boundary))
                    continue;

                if (boundary.Equals(entranceBoundary) &&
                    boundaryVisuals.TryGetValue(boundary, out BoundaryVisualType existingType) &&
                    existingType == BoundaryVisualType.DoorWall)
                {
                    continue;
                }

                Vector3[] boundarySamplePoints = GetBoundarySamplePoints(boundary);
                for (int sampleIndex = 0; sampleIndex < boundarySamplePoints.Length; sampleIndex++)
                {
                    if (IsPointExposedToCamera(boundarySamplePoints[sampleIndex], cameraPlanes, coverObject))
                        return false;
                }
            }
        }

        return true;
    }

    private void RecycleStaleSegments()
    {
        if (playerTransform == null || segments.Count <= 1)
            return;

        int recycledCount = 0;
        while (recycledCount < MaxSegmentsRecycledPerUpdate)
        {
            int playerSegmentIndex = FindPlayerSegmentIndex(playerTransform.position);
            if (!TryGetBestRecycleCandidate(playerSegmentIndex, out int segmentIndex))
                break;

            if (!TryRecycleSegment(segmentIndex))
                break;

            recycledCount++;
        }
    }

    private bool TryRecycleSegment(int segmentIndex)
    {
        if (segmentIndex <= 0 || segmentIndex >= segments.Count)
            return false;

        SegmentData segment = segments[segmentIndex];
        if (segment == null || !segment.IsActive)
            return false;

        Bounds protectedBounds = segment.WorldBounds;
        protectedBounds.Expand(new Vector3(0.2f, 1.2f, 0.2f));
        if (protectedBounds.Contains(playerTransform.position))
            return false;

        if (!IsSegmentHidden(segment))
            return false;

        int removedMainPathIndex = -1;
        if (segment.IsMainPath)
            removedMainPathIndex = mainPathSegmentIndices.IndexOf(segment.Index);

        HashSet<BoundaryKey> affectedBoundaries = new HashSet<BoundaryKey>();
        List<Frontier> recycleFrontiers = new List<Frontier>();

        for (int i = pendingFrontiers.Count - 1; i >= 0; i--)
        {
            Frontier pending = pendingFrontiers[i].Frontier;
            if (segment.Cells.Contains(pending.SourceCell))
                pendingFrontiers.RemoveAt(i);
        }

        for (int i = 0; i < segment.Cells.Count; i++)
        {
            GridCoord cell = segment.Cells[i];
            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
            {
                Direction direction = AllDirections[directionIndex];
                BoundaryKey boundary = GetCanonicalBoundary(cell, direction);
                affectedBoundaries.Add(boundary);

                GridCoord neighbor = cell.Step(direction);
                if (!cells.TryGetValue(neighbor, out CellData neighborCell) || neighborCell.SegmentIndex == segmentIndex)
                    continue;

                Frontier recycleFrontier = new Frontier(neighbor, Opposite(direction));
                bool exists = false;
                for (int frontierIndex = 0; frontierIndex < recycleFrontiers.Count; frontierIndex++)
                {
                    if (FrontierEquals(recycleFrontiers[frontierIndex], recycleFrontier))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                    recycleFrontiers.Add(recycleFrontier);
            }
        }

        for (int i = 0; i < segment.Cells.Count; i++)
            cells.Remove(segment.Cells[i]);

        for (int i = 0; i < segment.Cells.Count; i++)
        {
            GridCoord cell = segment.Cells[i];
            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
            {
                BoundaryKey boundary = GetCanonicalBoundary(cell, AllDirections[directionIndex]);
                bool hasA = cells.ContainsKey(boundary.Cell);
                bool hasB = cells.ContainsKey(boundary.Cell.Step(boundary.Side));
                if (!hasA && !hasB)
                {
                    openBoundaries.Remove(boundary);
                    doorBoundaries.Remove(boundary);
                }
                else if (!(hasA && hasB))
                {
                    openBoundaries.Remove(boundary);
                }
            }
        }

        foreach (Frontier frontier in recycleFrontiers)
        {
            BoundaryKey boundary = GetCanonicalBoundary(frontier.SourceCell, frontier.Direction);
            openBoundaries.Remove(boundary);
            doorBoundaries.Add(boundary);
            EnqueueFrontier(frontier, MaxBranchDepth, prioritizeFront: true);
            affectedBoundaries.Add(boundary);
        }

        if (segment.Root != null)
            Destroy(segment.Root.gameObject);

        if (!segment.IsMainPath && branchSegmentCount > 0)
            branchSegmentCount--;

        if (removedMainPathIndex >= 0)
        {
            mainPathSegmentIndices.RemoveAt(removedMainPathIndex);
            if (removedMainPathIndex <= currentMainPathIndex)
                currentMainPathIndex = Mathf.Max(0, currentMainPathIndex - 1);
        }

        segment.IsActive = false;
        segment.Root = null;
        segment.Cells.Clear();

        foreach (BoundaryKey boundary in affectedBoundaries)
            UpdateBoundary(boundary, ResolveBoundaryVisual(boundary));

        return true;
    }

    private bool IsSegmentHidden(SegmentData segment)
    {
        Plane[] cameraPlanes = GetCurrentCameraPlanes();
        if (cameraPlanes == null || segment == null)
            return true;

        if (GeometryUtility.TestPlanesAABB(cameraPlanes, segment.WorldBounds))
            return false;

        HashSet<BoundaryKey> sampledBoundaries = new HashSet<BoundaryKey>();
        for (int i = 0; i < segment.Cells.Count; i++)
        {
            GridCoord coord = segment.Cells[i];
            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
            {
                Direction direction = AllDirections[directionIndex];
                GridCoord neighbor = coord.Step(direction);
                if (cells.TryGetValue(neighbor, out CellData neighborCell) && neighborCell.SegmentIndex == segment.Index)
                    continue;

                BoundaryKey boundary = GetCanonicalBoundary(coord, direction);
                if (!sampledBoundaries.Add(boundary))
                    continue;

                if (GeometryUtility.TestPlanesAABB(cameraPlanes, GetBoundaryBounds(boundary)))
                    return false;
            }
        }

        return true;
    }

    private bool CanGenerateBehindDoor(BoundaryKey boundary)
    {
        return boundaryVisuals.TryGetValue(boundary, out BoundaryVisualType type) &&
               type == BoundaryVisualType.DoorWall &&
               IsExteriorBoundary(boundary);
    }

    private void PrunePendingFrontiers()
    {
        for (int i = pendingFrontiers.Count - 1; i >= 0; i--)
        {
            Frontier frontier = pendingFrontiers[i].Frontier;
            if (!cells.ContainsKey(frontier.SourceCell))
                pendingFrontiers.RemoveAt(i);
        }
    }

    private static int CompareHitsByDistance(RaycastHit left, RaycastHit right)
    {
        return left.distance.CompareTo(right.distance);
    }

    private Plane[] GetCurrentCameraPlanes()
    {
        if (PlayerManager.PlayerManagerInstance != null &&
            PlayerManager.PlayerManagerInstance.cameraPlanes != null &&
            PlayerManager.PlayerManagerInstance.cameraPlanes.Length > 0)
        {
            return PlayerManager.PlayerManagerInstance.cameraPlanes;
        }

        if (playerCamera == null)
            return null;

        return GeometryUtility.CalculateFrustumPlanes(playerCamera);
    }

    private GameObject GetGenerationCoverObject(BoundaryKey entranceBoundary)
    {
        if (!boundaryVisuals.TryGetValue(entranceBoundary, out BoundaryVisualType type) || type != BoundaryVisualType.DoorWall)
            return null;

        boundaryObjects.TryGetValue(entranceBoundary, out GameObject coverObject);
        return coverObject;
    }

    private bool IsPointExposedToCamera(Vector3 point, Plane[] cameraPlanes, GameObject coverObject)
    {
        if (playerCamera == null)
            return false;

        Bounds sampleBounds = new Bounds(point, Vector3.one * 0.12f);
        if (!GeometryUtility.TestPlanesAABB(cameraPlanes, sampleBounds))
            return false;

        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = point - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance + VisibilityRayTolerance, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, CompareHitsByDistance);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
                continue;

            if (playerTransform != null && hitCollider.transform.IsChildOf(playerTransform))
                continue;

            if (coverObject != null && hitCollider.transform.IsChildOf(coverObject.transform))
                return false;

            if (hits[i].distance < distance - VisibilityRayTolerance)
                return false;

            return true;
        }

        return true;
    }

    private bool IsExteriorBoundary(BoundaryKey boundary)
    {
        bool hasA = cells.ContainsKey(boundary.Cell);
        bool hasB = cells.ContainsKey(boundary.Cell.Step(boundary.Side));
        return hasA ^ hasB;
    }

    private bool IsSymmetricRoomWindow(CellData cell, SegmentData segment, Direction side, Direction lowSide, Direction highSide)
    {
        if (side != lowSide && side != highSide)
            return false;

        GridCoord oppositeCell;
        if (side == lowSide)
        {
            if ((lowSide == Direction.West && cell.Coord.X != segment.MinX) ||
                (lowSide == Direction.South && cell.Coord.Z != segment.MinZ))
            {
                return false;
            }

            oppositeCell = lowSide == Direction.West
                ? new GridCoord(segment.MaxX, cell.Coord.Z)
                : new GridCoord(cell.Coord.X, segment.MaxZ);
        }
        else
        {
            if ((highSide == Direction.East && cell.Coord.X != segment.MaxX) ||
                (highSide == Direction.North && cell.Coord.Z != segment.MaxZ))
            {
                return false;
            }

            oppositeCell = highSide == Direction.East
                ? new GridCoord(segment.MinX, cell.Coord.Z)
                : new GridCoord(cell.Coord.X, segment.MinZ);
        }

        if (!cells.TryGetValue(oppositeCell, out CellData opposite) || opposite.SegmentIndex != cell.SegmentIndex)
            return false;

        BoundaryKey oppositeBoundary = GetCanonicalBoundary(oppositeCell, Opposite(side));
        return IsWindowEligibleBoundary(oppositeBoundary);
    }

    private bool IsSymmetricHallWindow(CellData cell, Direction side)
    {
        Direction left = TurnLeft(cell.PrimaryDirection);
        Direction right = TurnRight(cell.PrimaryDirection);
        if (side != left && side != right)
            return false;

        BoundaryKey oppositeBoundary = GetCanonicalBoundary(cell.Coord, side == left ? right : left);
        return IsWindowEligibleBoundary(oppositeBoundary);
    }

    private bool IsWindowEligibleBoundary(BoundaryKey boundary)
    {
        if (!IsExteriorBoundary(boundary) || doorBoundaries.Contains(boundary))
            return false;

        if (!TryGetExteriorBoundaryCell(boundary, out CellData cell, out Direction exteriorSide))
            return false;

        return HasOutdoorViewClearance(cell.Coord, exteriorSide);
    }

    private bool WouldExceedLiveCellCap(SegmentBlueprint blueprint)
    {
        return cells.Count + blueprint.CellCount > MaxLiveCells;
    }

    private bool HasOutdoorViewClearance(GridCoord cell, Direction side)
    {
        for (int forward = 1; forward <= WindowViewDepth; forward++)
        {
            GridCoord forwardCoord = StepCoord(cell, side, forward);
            for (int lateral = -WindowViewHalfWidth; lateral <= WindowViewHalfWidth; lateral++)
            {
                GridCoord sample = OffsetLateral(forwardCoord, side, lateral);
                if (cells.ContainsKey(sample))
                    return false;
            }
        }

        return true;
    }

    private bool WouldConsumeExistingWindowView(GridCoord coord)
    {
        foreach (KeyValuePair<BoundaryKey, BoundaryVisualType> pair in boundaryVisuals)
        {
            if (pair.Value != BoundaryVisualType.WindowWall || !IsExteriorBoundary(pair.Key))
                continue;

            if (IsCoordInWindowView(pair.Key, coord))
                return true;
        }

        return false;
    }

    private bool IsCoordInWindowView(BoundaryKey boundary, GridCoord coord)
    {
        if (!TryGetExteriorBoundaryCell(boundary, out CellData cell, out Direction exteriorSide))
            return false;

        for (int forward = 1; forward <= WindowViewDepth; forward++)
        {
            GridCoord forwardCoord = StepCoord(cell.Coord, exteriorSide, forward);
            for (int lateral = -WindowViewHalfWidth; lateral <= WindowViewHalfWidth; lateral++)
            {
                if (OffsetLateral(forwardCoord, exteriorSide, lateral).Equals(coord))
                    return true;
            }
        }

        return false;
    }

    private bool TryGetExteriorBoundaryCell(BoundaryKey boundary, out CellData cell, out Direction exteriorSide)
    {
        bool hasA = cells.TryGetValue(boundary.Cell, out CellData cellA);
        GridCoord neighbor = boundary.Cell.Step(boundary.Side);
        bool hasB = cells.TryGetValue(neighbor, out CellData cellB);
        if (hasA == hasB)
        {
            cell = null;
            exteriorSide = boundary.Side;
            return false;
        }

        cell = hasA ? cellA : cellB;
        exteriorSide = hasA ? boundary.Side : Opposite(boundary.Side);
        return true;
    }

    private bool TryGetBestRecycleCandidate(int playerSegmentIndex, out int segmentIndex)
    {
        segmentIndex = -1;
        if (playerSegmentIndex < 0)
            return false;

        Dictionary<int, int> graphDistances = BuildReachableSegmentDistances(playerSegmentIndex);
        int bestPriority = int.MaxValue;
        float bestDistance = float.PositiveInfinity;

        for (int i = 1; i < segments.Count; i++)
        {
            SegmentData segment = segments[i];
            if (segment == null || !segment.IsActive || segment.Index == playerSegmentIndex)
                continue;

            if (!TryGetRecyclePriority(segment.Index, segment, graphDistances, out int priority))
                continue;

            if (!IsSegmentHidden(segment))
                continue;

            float distance = segment.WorldBounds.SqrDistance(playerTransform.position);
            if (priority > bestPriority)
                continue;

            if (priority == bestPriority && distance >= bestDistance)
                continue;

            bestPriority = priority;
            bestDistance = distance;
            segmentIndex = segment.Index;
        }

        return segmentIndex >= 0;
    }

    private bool TryGetRecyclePriority(
        int segmentIndex,
        SegmentData segment,
        Dictionary<int, int> graphDistances,
        out int priority)
    {
        priority = int.MaxValue;
        if (!graphDistances.TryGetValue(segmentIndex, out int graphDistance))
        {
            priority = 0;
            return true;
        }

        if (segment.IsMainPath)
        {
            int mainPathIndex = mainPathSegmentIndices.IndexOf(segmentIndex);
            if (mainPathIndex > 0 && mainPathIndex < currentMainPathIndex - RetainedMainSegmentsBehind)
            {
                priority = 1 + (currentMainPathIndex - mainPathIndex - 1);
                return true;
            }

            return false;
        }

        if (graphDistance <= 1)
        {
            priority = 12;
            return true;
        }

        float recycleDistanceSqr = BranchRecycleDistance * BranchRecycleDistance;
        if (segment.WorldBounds.SqrDistance(playerTransform.position) > recycleDistanceSqr)
        {
            priority = 20 + graphDistance;
            return true;
        }

        return false;
    }

    private int FindPlayerSegmentIndex(Vector3 playerPosition)
    {
        int closestIndex = -1;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < segments.Count; i++)
        {
            SegmentData segment = segments[i];
            if (segment == null || !segment.IsActive)
                continue;

            Bounds expanded = segment.WorldBounds;
            expanded.Expand(new Vector3(0.2f, 1.2f, 0.2f));
            if (expanded.Contains(playerPosition))
                return segment.Index;

            float sqrDistance = segment.WorldBounds.SqrDistance(playerPosition);
            if (sqrDistance >= closestDistance)
                continue;

            closestDistance = sqrDistance;
            closestIndex = segment.Index;
        }

        return closestIndex;
    }

    private Dictionary<int, int> BuildReachableSegmentDistances(int startSegmentIndex)
    {
        Dictionary<int, HashSet<int>> adjacency = new Dictionary<int, HashSet<int>>();
        foreach (KeyValuePair<GridCoord, CellData> pair in cells)
        {
            CellData cell = pair.Value;
            if (cell == null || cell.SegmentIndex < 0 || cell.SegmentIndex >= segments.Count || !segments[cell.SegmentIndex].IsActive)
                continue;

            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
            {
                Direction direction = AllDirections[directionIndex];
                if (!cells.TryGetValue(cell.Coord.Step(direction), out CellData neighbor))
                    continue;

                if (neighbor.SegmentIndex == cell.SegmentIndex ||
                    neighbor.SegmentIndex < 0 ||
                    neighbor.SegmentIndex >= segments.Count ||
                    !segments[neighbor.SegmentIndex].IsActive)
                {
                    continue;
                }

                BoundaryKey boundary = GetCanonicalBoundary(cell.Coord, direction);
                if (!IsBoundaryPassable(boundary))
                    continue;

                AddAdjacency(adjacency, cell.SegmentIndex, neighbor.SegmentIndex);
                AddAdjacency(adjacency, neighbor.SegmentIndex, cell.SegmentIndex);
            }
        }

        Dictionary<int, int> distances = new Dictionary<int, int>();
        if (startSegmentIndex < 0 || startSegmentIndex >= segments.Count || !segments[startSegmentIndex].IsActive)
            return distances;

        Queue<int> queue = new Queue<int>();
        distances[startSegmentIndex] = 0;
        queue.Enqueue(startSegmentIndex);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out HashSet<int> neighbors))
                continue;

            foreach (int neighbor in neighbors)
            {
                if (distances.ContainsKey(neighbor))
                    continue;

                distances[neighbor] = distances[current] + 1;
                queue.Enqueue(neighbor);
            }
        }

        return distances;
    }

    private static void AddAdjacency(Dictionary<int, HashSet<int>> adjacency, int from, int to)
    {
        if (!adjacency.TryGetValue(from, out HashSet<int> neighbors))
        {
            neighbors = new HashSet<int>();
            adjacency[from] = neighbors;
        }

        neighbors.Add(to);
    }

    private bool IsBoundaryPassable(BoundaryKey boundary)
    {
        return openBoundaries.Contains(boundary) || doorBoundaries.Contains(boundary);
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

    private SegmentType GetSegmentTypeAt(GridCoord cell)
    {
        if (!cells.TryGetValue(cell, out CellData cellData))
            return SegmentType.Hallway;

        return segments[cellData.SegmentIndex].Type;
    }

    private WindowPattern DetermineWindowPattern(SegmentType type)
    {
        if (!UseExteriorWindows)
            return WindowPattern.None;

        if (type == SegmentType.Room && random.NextDouble() < RoomWindowChance)
            return random.Next(2) == 0 ? WindowPattern.RoomEastWest : WindowPattern.RoomNorthSouth;

        if (type == SegmentType.Hallway && random.NextDouble() < HallWindowChance)
            return WindowPattern.HallAcross;

        return WindowPattern.None;
    }

    private int RollRoomDimension()
    {
        double value = random.NextDouble();
        if (value < 0.55d)
            return 2;

        if (value < 0.9d)
            return 3;

        return 4;
    }

    private int RollHallLength()
    {
        double value = random.NextDouble();
        if (value < 0.2d)
            return 2;

        if (value < 0.75d)
            return 3;

        return 4;
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

    private static GridCoord StepCoord(GridCoord coord, Direction direction, int count)
    {
        GridCoord stepped = coord;
        for (int i = 0; i < count; i++)
            stepped = stepped.Step(direction);

        return stepped;
    }

    private static GridCoord OffsetLateral(GridCoord coord, Direction forward, int lateralSteps)
    {
        if (lateralSteps == 0)
            return coord;

        Direction lateralDirection = lateralSteps > 0 ? TurnRight(forward) : TurnLeft(forward);
        return StepCoord(coord, lateralDirection, Mathf.Abs(lateralSteps));
    }

    private Bounds GetCellBounds(GridCoord coord)
    {
        Vector3 center = GetCellWorldPosition(coord) + new Vector3(0f, FloorHeight * 0.5f, 0f);
        return new Bounds(center, new Vector3(CellSize, FloorHeight, CellSize));
    }

    private Vector3[] GetCellVisibilitySamplePoints(GridCoord coord)
    {
        Vector3 center = GetCellWorldPosition(coord);
        float inset = CellSize * 0.22f;
        return new[]
        {
            center + new Vector3(0f, VisibilityVerticalOffset, 0f),
            center + new Vector3(inset, VisibilityVerticalOffset, 0f),
            center + new Vector3(-inset, VisibilityVerticalOffset, 0f),
            center + new Vector3(0f, VisibilityVerticalOffset, inset),
            center + new Vector3(0f, VisibilityVerticalOffset, -inset),
            center + new Vector3(0f, FloorHeight * 0.82f, 0f)
        };
    }

    private Vector3 GetCellWorldPosition(GridCoord coord)
    {
        return worldGridOrigin + new Vector3(coord.X * CellSize, 0f, coord.Z * CellSize);
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

    private static bool FrontierEquals(Frontier left, Frontier right)
    {
        return left.SourceCell.Equals(right.SourceCell) && left.Direction == right.Direction;
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
        Entry,
        Room,
        Hallway
    }

    private enum BoundaryVisualType
    {
        None,
        Wall,
        WindowWall,
        DoorWall
    }

    private enum HiddenBuildResult
    {
        NoBlueprint,
        Deferred,
        Success
    }

    private enum WindowPattern
    {
        None,
        RoomEastWest,
        RoomNorthSouth,
        HallAcross
    }

    private readonly struct GridCoord : IEquatable<GridCoord>, IComparable<GridCoord>
    {
        public static readonly GridCoord Zero = new GridCoord(0, 0);

        public GridCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }

        public int Z { get; }

        public GridCoord Step(Direction direction)
        {
            switch (direction)
            {
                case Direction.North:
                    return new GridCoord(X, Z + 1);
                case Direction.East:
                    return new GridCoord(X + 1, Z);
                case Direction.South:
                    return new GridCoord(X, Z - 1);
                default:
                    return new GridCoord(X - 1, Z);
            }
        }

        public int CompareTo(GridCoord other)
        {
            if (Z != other.Z)
                return Z.CompareTo(other.Z);

            return X.CompareTo(other.X);
        }

        public bool Equals(GridCoord other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (X * 397) ^ Z;
        }
    }

    private readonly struct CellSpec
    {
        public CellSpec(GridCoord coord, Direction primaryDirection)
        {
            Coord = coord;
            PrimaryDirection = primaryDirection;
        }

        public GridCoord Coord { get; }

        public Direction PrimaryDirection { get; }
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

    private readonly struct PendingFrontier
    {
        public PendingFrontier(Frontier frontier, int remainingDepth)
        {
            Frontier = frontier;
            RemainingDepth = remainingDepth;
        }

        public Frontier Frontier { get; }

        public int RemainingDepth { get; }
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

    private sealed class SegmentBlueprint
    {
        private readonly HashSet<GridCoord> cellLookup = new HashSet<GridCoord>();
        private readonly List<CellSpec> cells = new List<CellSpec>();
        private readonly List<Frontier> branchFrontiers = new List<Frontier>();

        public SegmentBlueprint(SegmentType type)
        {
            Type = type;
        }

        public SegmentType Type { get; }

        public Frontier ExitFrontier { get; set; }

        public IReadOnlyList<CellSpec> Cells => cells;

        public int CellCount => cells.Count;

        public IReadOnlyList<Frontier> BranchFrontiers => branchFrontiers;

        public int BranchFrontierCount => branchFrontiers.Count;

        public void AddCell(CellSpec spec)
        {
            if (cellLookup.Add(spec.Coord))
                cells.Add(spec);
        }

        public bool Contains(GridCoord coord)
        {
            return cellLookup.Contains(coord);
        }

        public void ClearBranchFrontiers()
        {
            branchFrontiers.Clear();
        }

        public void AddBranchFrontier(Frontier frontier)
        {
            for (int i = 0; i < branchFrontiers.Count; i++)
            {
                if (FrontierEquals(branchFrontiers[i], frontier))
                    return;
            }

            branchFrontiers.Add(frontier);
        }
    }

    private sealed class SegmentData
    {
        public int Index { get; set; }

        public SegmentType Type { get; set; }

        public bool IsActive { get; set; }

        public bool IsMainPath { get; set; }

        public Transform Root { get; set; }

        public Bounds WorldBounds { get; set; }

        public Frontier ExitFrontier { get; set; }

        public int MinX { get; set; }

        public int MaxX { get; set; }

        public int MinZ { get; set; }

        public int MaxZ { get; set; }

        public WindowPattern WindowPattern { get; set; }

        public List<GridCoord> Cells { get; } = new List<GridCoord>();
    }

    private sealed class CellData
    {
        public GridCoord Coord { get; set; }

        public GameObject Root { get; set; }

        public int SegmentIndex { get; set; }

        public Direction PrimaryDirection { get; set; }
    }
}
